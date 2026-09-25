using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.Management;

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
                [new SecurityEvidence("CollectionError", ex.GetType().Name, "WindowsUpdate")],
                "SEC-OS-UPDATES");
        }

        var evidence = new List<SecurityEvidence>
        {
            new("LastSuccessfulQualifyingUpdate", value.LastSuccessfulQualifyingUpdate?.ToString("O") ?? "Unknown", value.Source),
            new("PendingQualifyingUpdates", value.PendingQualifyingUpdates?.ToString() ?? "Unknown", value.Source),
            new("PendingReboot", value.PendingReboot switch { true => "true", false => "false", null => "Unknown" }, value.Source),
            new("UpdateService", string.IsNullOrWhiteSpace(value.ServiceSource) ? "Unknown" : value.ServiceSource, value.Source)
        };

        if (value.LastSuccessfulQualifyingUpdate is not DateTime last)
            return new SecurityControlObservation("SEC-OS-UPDATES", SecurityControlStatus.Unknown, evidence, "SEC-OS-UPDATES");

        var ageDays = Math.Max(0, (int)Math.Floor((at - last).TotalDays));
        evidence.Add(new SecurityEvidence("UpdateAgeDays", ageDays.ToString(CultureInfo.InvariantCulture), value.Source));

        var status = ageDays > 60
            ? SecurityControlStatus.Fail
            : value.PendingQualifyingUpdates is null
                ? SecurityControlStatus.Warn
                : value.PendingQualifyingUpdates.Value > 0 || ageDays >= 46
                    ? SecurityControlStatus.Warn
                    : SecurityControlStatus.Pass;
        return new SecurityControlObservation("SEC-OS-UPDATES", status, evidence, "SEC-OS-UPDATES");
    }
}

internal sealed class WindowsUpdateSecuritySource : IWindowsUpdateSecuritySource
{
    private static readonly TimeSpan PendingSearchTimeout = TimeSpan.FromSeconds(5);

    private const string PendingSearchScript =
        "$ErrorActionPreference='Stop';" +
        "$ids=@('0FA1201D-4330-4FA8-8AE9-B877473B6441','E6CF1350-C01B-414D-A61F-263D14D133B4','28BC880E-0592-4CBF-8F95-C79B17911D5F','CD5FFD1E-E932-4E3A-BF74-18BF0B1BBD83','68C5B0A3-D1A6-4553-AE49-01D3A7827828');" +
        "$s=New-Object -ComObject Microsoft.Update.Session;" +
        "$q=$s.CreateUpdateSearcher();" +
        "$r=$q.Search(\"IsInstalled=0 and Type='Software' and IsHidden=0\");" +
        "$n=0;" +
        "for($i=0;$i -lt $r.Updates.Count;$i++){" +
        "$u=$r.Updates.Item($i);$match=$false;" +
        "for($j=0;$j -lt $u.Categories.Count;$j++){" +
        "$id=[string]$u.Categories.Item($j).CategoryID;if($ids -contains $id){$match=$true;break}}" +
        "if($match){$n++}}" +
        "[Console]::Out.Write($n)";

    public WindowsUpdateSecurityObservation Read()
    {
        DateTime? last = null;
        try { last = ReadLastInstalledWindowsUpdate(); } catch { }

        int? pending = null;
        try { pending = ReadPendingQualifyingUpdatesBounded(); } catch { }

        return new WindowsUpdateSecurityObservation(
            last,
            pending,
            ReadPendingReboot(),
            DescribeService(),
            "QFE + bounded WUA");
    }

    private static DateTime? ReadLastInstalledWindowsUpdate()
    {
        DateTime? latest = null;
        using var searcher = new ManagementObjectSearcher(
            "root\\CIMV2",
            "SELECT HotFixID,InstalledOn,Description FROM Win32_QuickFixEngineering");
        using var collection = searcher.Get();
        foreach (ManagementObject item in collection)
        {
            using (item)
            {
                var hotfixId = Convert.ToString(item["HotFixID"]) ?? "";
                if (string.IsNullOrWhiteSpace(hotfixId)) continue;
                var date = ParseInstalledOn(item["InstalledOn"]);
                if (date is null) continue;
                if (latest is null || date.Value > latest.Value) latest = date.Value;
            }
        }
        return latest;
    }

    private static DateTime? ParseInstalledOn(object? value)
    {
        if (value is DateTime date) return date;
        var raw = Convert.ToString(value);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out date)) return date;
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date)) return date;
        try
        {
            var dmtf = ManagementDateTimeConverter.ToDateTime(raw);
            return dmtf.Year >= 2000 ? dmtf : null;
        }
        catch { return null; }
    }

    private static int? ReadPendingQualifyingUpdatesBounded()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windows)) return null;
        var powershell = Path.Combine(windows, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell)) return null;

        var psi = new ProcessStartInfo
        {
            FileName = powershell,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-NoLogo");
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(PendingSearchScript);

        using var process = Process.Start(psi);
        if (process is null) return null;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit((int)PendingSearchTimeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            try { process.WaitForExit(1000); } catch { }
            return null;
        }

        var output = stdout.GetAwaiter().GetResult().Trim();
        _ = stderr.GetAwaiter().GetResult();
        return process.ExitCode == 0
            && int.TryParse(output, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            && count >= 0
            ? count
            : null;
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

    private static string DescribeService()
    {
        try
        {
            using var au = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU");
            var raw = au?.GetValue("UseWUServer");
            var useWsus = raw switch { int i => i != 0, uint u => u != 0, _ => false };
            if (useWsus) return "WSUS policy";
            if (raw is not null) return "Windows Update policy";
        }
        catch { }
        return "Windows Update default/policy";
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
}
