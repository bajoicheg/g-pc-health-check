using Microsoft.Win32;
using System.Security.Principal;

namespace G.PcHealthCheck;

/// <summary>Read registrations, never execute/expand their commands or resolve shortcut targets.</summary>
internal static class StartupReviewService
{
    public static string ScopeNote => AppLocalization.T("Review.Startup.ScopeNote");
    private const int SourceLimit = 2000;

    public static List<StartupReviewEntry> Filter(StartupReviewSnapshot snapshot, string? query)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var text = query?.Trim() ?? "";
        return snapshot.Entries.Where(x => text.Length == 0 || new[] { x.Name, x.Command, x.Scope, x.Source, x.State }
            .Any(value => value.Contains(text, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    public static StartupReviewSnapshot Collect(CancellationToken ct, IProgress<string>? progress = null)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var account = identity.Name;
        var allUsers = AppLocalization.T("Review.Startup.AllUsers");
        var providers = new List<Func<CancellationToken, (ReviewSource Source, List<StartupReviewEntry> Entries)>>();
        foreach (var keyName in new[] { "Run", "RunOnce" })
        {
            var key = keyName;
            providers.Add(token => RegistrySource(RegistryHive.CurrentUser, RegistryView.Default, key, account, token));
            foreach (var view in Environment.Is64BitOperatingSystem ? new[] { RegistryView.Registry64, RegistryView.Registry32 } : new[] { RegistryView.Default })
            {
                var capturedView = view;
                providers.Add(token => RegistrySource(RegistryHive.LocalMachine, capturedView, key, allUsers, token));
            }
        }
        providers.Add(token => FolderSource(Environment.SpecialFolder.Startup, account, token));
        providers.Add(token => FolderSource(Environment.SpecialFolder.CommonStartup, allUsers, token));
        var result = CollectSources(providers.Select(provider => (Func<CancellationToken, (ReviewSource, List<StartupReviewEntry>)>)(token =>
        {
            progress?.Report(AppLocalization.T("Review.Startup.Progress")); return provider(token);
        })), ct);
        result.Account = account;
        if (DiagnosticsService.IsAdministrator())
            result.Issues.Add(AppLocalization.T("Review.Startup.ElevatedIssue"));
        return result;
    }

    public static StartupReviewSnapshot CollectSources(IEnumerable<Func<CancellationToken, (ReviewSource Source, List<StartupReviewEntry> Entries)>> sources, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var result = new StartupReviewSnapshot();
        foreach (var collect in sources)
        {
            ct.ThrowIfCancellationRequested();
            if (result.Sources.Count >= 32) { result.Issues.Add(AppLocalization.T("Review.Startup.SourceCountLimit")); break; }
            ReviewSource source; List<StartupReviewEntry> entries;
            try { (source, entries) = collect(ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                source = new ReviewSource { Name = AppLocalization.T("Review.Startup.UnknownSource", result.Sources.Count + 1), State = ReviewCollectionState.Unavailable, Detail = Describe(ex) };
                entries = [];
            }
            ct.ThrowIfCancellationRequested();
            if (entries.Count > SourceLimit)
            {
                entries = entries.Take(SourceLimit).ToList(); source.State = ReviewCollectionState.Partial;
                source.Detail += AppLocalization.T("Review.Startup.SourceItemLimit", SourceLimit);
            }
            source.Items = entries.Count;
            result.Sources.Add(source); result.Entries.AddRange(entries);
            if (source.State is ReviewCollectionState.Partial or ReviewCollectionState.Unavailable)
                result.Issues.Add(source.Name + ": " + source.Detail);
        }
        result.Entries = result.Entries.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Source, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Command, StringComparer.Ordinal).ToList();
        result.State = result.Sources.Count == 0 || result.Sources.All(x => x.State == ReviewCollectionState.Unavailable)
            ? ReviewCollectionState.Unavailable : result.Issues.Count > 0 ? ReviewCollectionState.Partial : ReviewCollectionState.Complete;
        result.CollectedAt = DateTime.Now;
        return result;
    }

    private static (ReviewSource Source, List<StartupReviewEntry> Entries) RegistrySource(RegistryHive hive, RegistryView view, string keyName, string scope, CancellationToken ct)
    {
        var path = @"Software\Microsoft\Windows\CurrentVersion\" + keyName;
        var source = new ReviewSource { Name = $"{(hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM")}\\{path} [{view}]", Scope = scope, State = ReviewCollectionState.Complete };
        var entries = new List<StartupReviewEntry>();
        try
        {
            ct.ThrowIfCancellationRequested();
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(path, writable: false);
            if (key is null) { source.State = ReviewCollectionState.Missing; source.Detail = AppLocalization.T("Review.Startup.RegistryMissing"); return (source, entries); }
            var names = key.GetValueNames();
            if (names.Length > SourceLimit) { source.State = ReviewCollectionState.Partial; source.Detail = AppLocalization.T("Review.Startup.RegistryLimit", SourceLimit); }
            foreach (var name in names.Take(SourceLimit))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    var command = value as string;
                    if (command is null)
                    {
                        source.State = ReviewCollectionState.Partial;
                        source.Detail = AppLocalization.T("Review.Startup.RegistryNonString");
                    }
                    entries.Add(new StartupReviewEntry { Name = name.Length == 0 ? AppLocalization.T("Review.Startup.DefaultValue") : name,
                        Command = command ?? AppLocalization.T("Review.Startup.CommandUnavailable"), Scope = scope, Source = source.Name });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    source.State = ReviewCollectionState.Partial; source.Detail = Describe(ex);
                    entries.Add(new StartupReviewEntry { Name = name, Source = source.Name, Scope = scope, Command = AppLocalization.T("Review.Startup.ValueUnavailable") });
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { source.State = entries.Count > 0 ? ReviewCollectionState.Partial : ReviewCollectionState.Unavailable; source.Detail = Describe(ex); }
        return (source, entries);
    }

    private static (ReviewSource Source, List<StartupReviewEntry> Entries) FolderSource(Environment.SpecialFolder folder, string scope, CancellationToken ct)
    {
        var source = new ReviewSource { Name = folder.ToString(), Scope = scope, State = ReviewCollectionState.Complete };
        var entries = new List<StartupReviewEntry>();
        try
        {
            ct.ThrowIfCancellationRequested();
            var path = Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrWhiteSpace(path)) throw new IOException("Startup path is unavailable.");
            source.Name = "Startup: " + path;
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Startup root is a reparse point; not traversed.");
            using var iterator = Directory.EnumerateFileSystemEntries(path, "*", new System.IO.EnumerationOptions
                { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0 }).GetEnumerator();
            var visited = 0;
            while (iterator.MoveNext())
            {
                ct.ThrowIfCancellationRequested();
                if (visited++ >= SourceLimit) { source.State = ReviewCollectionState.Partial; source.Detail = AppLocalization.T("Review.Startup.FolderLimit", SourceLimit); break; }
                var item = iterator.Current;
                var attr = File.GetAttributes(item);
                if ((attr & FileAttributes.Directory) != 0) continue;
                if (Path.GetFileName(item).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                entries.Add(new StartupReviewEntry { Name = Path.GetFileName(item), Command = item, Scope = scope,
                    Source = source.Name + AppLocalization.T((attr & FileAttributes.ReparsePoint) != 0 ? "Review.Startup.LinkSuffix" : "Review.Startup.PathSuffix") });
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        { source.State = entries.Count == 0 ? ReviewCollectionState.Missing : ReviewCollectionState.Partial; source.Detail = AppLocalization.T("Review.Startup.FolderMissing"); }
        catch (Exception ex) { source.State = entries.Count > 0 ? ReviewCollectionState.Partial : ReviewCollectionState.Unavailable; source.Detail = Describe(ex); }
        return (source, entries);
    }
    private static string Describe(Exception ex) => $"{ex.GetType().Name} (0x{ex.HResult:X8})";
}
