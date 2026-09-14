using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace G.PcHealthCheck;

internal interface IWindowsUpdateSecuritySource
{
    WindowsUpdateSecurityObservation Read();
}

internal static class WindowsUpdateSecurityCollector
{
    public static SecurityControlObservation Collect(IWindowsUpdateSecuritySource? source = null, DateTime? now = null)
    {
        source ??= new WindowsUpdateSecuritySource();
        var at = now ?? DateTime.Now;
        WindowsUpdateSecurityObservation value;
        try { value = source.Read(); }
        catch (Exception ex)
        {
            return new SecurityControlObservation(
                "SEC-OS-UPDATES",
                SecurityControlStatus.Unknown,
                [new SecurityEvidence("CollectionError", ex.GetType().Name, "WUA")],
                "SEC-OS-UPDATES");
        }

        var evidence = new List<SecurityEvidence>
        {
            new("LastSuccessfulQualifyingUpdate", value.LastSuccessfulQualifyingUpdate?.ToString("O") ?? "Unknown", value.Source),
            new("PendingQualifyingUpdates", value.PendingQualifyingUpdates?.ToString() ?? "Unknown", value.Source),
            new("PendingReboot", value.PendingReboot switch { true => "true", false => "false", null => "Unknown" }, value.Source),
            new("UpdateService", string.IsNullOrWhiteSpace(value.ServiceSource) ? "Unknown" : value.ServiceSource, value.Source)
        };

        if (value.LastSuccessfulQualifyingUpdate is not DateTime last || value.PendingQualifyingUpdates is null)
            return new SecurityControlObservation("SEC-OS-UPDATES", SecurityControlStatus.Unknown, evidence, "SEC-OS-UPDATES");

        var ageDays = Math.Max(0, (int)Math.Floor((at - last).TotalDays));
        evidence.Add(new SecurityEvidence("UpdateAgeDays", ageDays.ToString(), value.Source));

        var status = ageDays > 60
            ? SecurityControlStatus.Fail
            : value.PendingQualifyingUpdates.Value > 0 || ageDays >= 46
                ? SecurityControlStatus.Warn
                : SecurityControlStatus.Pass;
        return new SecurityControlObservation("SEC-OS-UPDATES", status, evidence, "SEC-OS-UPDATES");
    }
}

internal sealed class WindowsUpdateSecuritySource : IWindowsUpdateSecuritySource
{
    private const int MaxHistoryEntries = 200;

    public WindowsUpdateSecurityObservation Read()
    {
        var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session", throwOnError: false)
            ?? throw new PlatformNotSupportedException("Windows Update Agent COM session is unavailable.");
        object? rawSession = null;
        object? rawSearcher = null;
        try
        {
            rawSession = Activator.CreateInstance(sessionType) ?? throw new InvalidOperationException("Cannot create Windows Update session.");
            dynamic session = rawSession;
            rawSearcher = session.CreateUpdateSearcher();
            dynamic searcher = rawSearcher;

            var service = DescribeService(searcher);
            var last = ReadLastSuccessfulQualifyingUpdate(searcher);
            var pending = ReadPendingQualifyingUpdates(searcher);
            return new WindowsUpdateSecurityObservation(last, pending, ReadPendingReboot(), service, "Windows Update Agent");
        }
        finally
        {
            ReleaseCom(rawSearcher);
            ReleaseCom(rawSession);
        }
    }

    private static DateTime? ReadLastSuccessfulQualifyingUpdate(dynamic searcher)
    {
        var total = Convert.ToInt32(searcher.GetTotalHistoryCount());
        if (total <= 0) return null;
        var count = Math.Min(total, MaxHistoryEntries);
        object? rawHistory = null;
        try
        {
            rawHistory = searcher.QueryHistory(0, count);
            dynamic history = rawHistory;
            var historyCount = Convert.ToInt32(history.Count);
            for (var i = 0; i < historyCount; i++)
            {
                object? rawEntry = null;
                try
                {
                    rawEntry = history.Item(i);
                    dynamic entry = rawEntry;
                    var operation = Convert.ToInt32(entry.Operation);
                    var resultCode = Convert.ToInt32(entry.ResultCode);
                    var title = Convert.ToString(entry.Title) ?? "";
                    if (operation != 1 || (resultCode != 2 && resultCode != 3) || !IsQualifyingOsUpdate(title)) continue;
                    return Convert.ToDateTime(entry.Date);
                }
                finally { ReleaseCom(rawEntry); }
            }
            return null;
        }
        finally { ReleaseCom(rawHistory); }
    }

    private static int? ReadPendingQualifyingUpdates(dynamic searcher)
    {
        object? rawSearchResult = null;
        object? rawUpdates = null;
        try
        {
            rawSearchResult = searcher.Search("IsInstalled=0 and Type='Software' and IsHidden=0");
            dynamic searchResult = rawSearchResult;
            rawUpdates = searchResult.Updates;
            dynamic updates = rawUpdates;
            var count = Convert.ToInt32(updates.Count);
            var qualifying = 0;
            for (var i = 0; i < count; i++)
            {
                object? rawUpdate = null;
                try
                {
                    rawUpdate = updates.Item(i);
                    dynamic update = rawUpdate;
                    var title = Convert.ToString(update.Title) ?? "";
                    if (IsQualifyingOsUpdate(title)) qualifying++;
                }
                finally { ReleaseCom(rawUpdate); }
            }
            return qualifying;
        }
        finally
        {
            ReleaseCom(rawUpdates);
            ReleaseCom(rawSearchResult);
        }
    }

    internal static bool IsQualifyingOsUpdate(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        if (title.Contains("Security Intelligence Update", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Definition Update", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Microsoft Defender Antivirus", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Driver", StringComparison.OrdinalIgnoreCase))
            return false;

        return title.Contains("Cumulative Update", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Security Update", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Critical Update", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Update Rollup", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Servicing Stack Update", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Update for Microsoft Windows", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Quality Update", StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribeService(dynamic searcher)
    {
        try
        {
            var serviceId = Convert.ToString(searcher.ServiceID);
            if (!string.IsNullOrWhiteSpace(serviceId)) return "ServiceID:" + serviceId;
        }
        catch { }
        try { return "ServerSelection:" + Convert.ToInt32(searcher.ServerSelection); }
        catch { return "Configured Windows Update service"; }
    }

    private static bool ReadPendingReboot()
    {
        static bool Exists(string path)
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            return key is not null;
        }
        try
        {
            if (Exists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending")) return true;
            if (Exists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired")) return true;
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager");
            return key?.GetValue("PendingFileRenameOperations") is not null;
        }
        catch { return false; }
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }
}
