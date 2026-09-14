using System.Diagnostics;
using System.Globalization;

namespace G.PcHealthCheck;

internal static class IncidentQueries
{
    public static void Validate(IncidentWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.To <= window.From || window.To - window.From > TimeSpan.FromDays(7))
            throw new ArgumentException(AppLocalization.T("Incident.Query.InvalidWindow"));
        if (window.MaxPerLog is < 1 or > 5000)
            throw new ArgumentOutOfRangeException(nameof(window), AppLocalization.T("Incident.Query.InvalidLimit"));
    }

    public static string XPath(IncidentWindow window)
    {
        Validate(window);
        static string Utc(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
        return $"*[System[TimeCreated[@SystemTime >= '{Utc(window.From)}' and @SystemTime <= '{Utc(window.To)}']]]";
    }

    public static List<IncidentEvent> Events(IncidentSnapshot snapshot, IncidentFilter filter)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(filter);
        if (filter.EventId is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(filter));
        var text = filter.Text.Trim();
        return snapshot.Logs.SelectMany(x => x.Events)
            .Where(x => (filter.Log.Length == 0 || x.Log.Equals(filter.Log, StringComparison.OrdinalIgnoreCase))
                && (!filter.EventId.HasValue || x.EventId == filter.EventId)
                && (!filter.WarningsOnly || x.Level is >= 1 and <= 3)
                && (text.Length == 0 || new[] { x.Provider, x.Message, x.EventId.ToString(CultureInfo.InvariantCulture), x.EmitterPid?.ToString(CultureInfo.InvariantCulture) ?? "" }
                    .Any(v => v.Contains(text, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(x => x.Timestamp).ThenBy(x => x.Log, StringComparer.Ordinal).ThenByDescending(x => x.RecordId).ToList();
    }

    public static List<ProcessReviewEntry> Processes(ProcessReviewSnapshot snapshot, string text)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(text); text = text.Trim();
        return snapshot.Processes.Where(x => text.Length == 0 || new[] { x.Name, x.Executable, x.CommandLine,
            x.Pid.ToString(CultureInfo.InvariantCulture), x.ParentPid?.ToString(CultureInfo.InvariantCulture) ?? "", x.SessionId?.ToString(CultureInfo.InvariantCulture) ?? "" }
            .Any(v => v.Contains(text, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Pid).ToList();
    }

    public static string Error(Exception ex)
        => (ex is UnauthorizedAccessException ? AppLocalization.T("Incident.Query.AccessDenied") : "") + $"{ex.GetType().Name}; 0x{ex.HResult:X8}";
}

internal sealed class IncidentEvents(IIncidentEventSource source)
{
    public IncidentSnapshot Collect(IncidentWindow window, CancellationToken ct)
    {
        IncidentQueries.Validate(window);
        var snapshot = new IncidentSnapshot { Window = window };
        foreach (var log in new[] { "Application", "System" })
        {
            var result = new IncidentLogResult { Log = log }; snapshot.Logs.Add(result);
            if (ct.IsCancellationRequested) { result.State = "Cancelled"; continue; }
            var watch = Stopwatch.StartNew(); var observed = 0;
            try
            {
                using var iterator = source.Read(log, window, ct).GetEnumerator();
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    if (watch.Elapsed >= TimeSpan.FromSeconds(20))
                    {
                        result.Warnings.Add(AppLocalization.T("Incident.Events.Timeout"));
                        break;
                    }
                    if (!iterator.MoveNext()) break;
                    ct.ThrowIfCancellationRequested();
                    if (++observed > window.MaxPerLog)
                    {
                        result.Warnings.Add(AppLocalization.T("Incident.Events.Limit", window.MaxPerLog));
                        break;
                    }
                    var e = iterator.Current;
                    if (e.Timestamp is null || e.Timestamp < window.From || e.Timestamp > window.To)
                    {
                        result.Warnings.Add(AppLocalization.T("Incident.Events.BadTimestamp"));
                        continue;
                    }
                    result.Events.Add(new IncidentEvent
                    {
                        Log = log,
                        RecordId = e.RecordId,
                        Timestamp = e.Timestamp,
                        EventId = e.EventId,
                        Level = e.Level,
                        Provider = e.Provider,
                        Message = e.Message,
                        MessageState = e.MessageState,
                        EmitterPid = e.EmitterPid
                    });
                    if (e.MessageState != "Available")
                        result.Warnings.Add(AppLocalization.T("Incident.Events.MessageUnavailable", e.RecordId, IncidentReport.MessageStateText(e.MessageState)));
                }
                ct.ThrowIfCancellationRequested();
                result.State = result.Warnings.Count == 0 ? "Complete" : "Partial";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                result.State = "Cancelled";
                result.Warnings.Add(AppLocalization.T("Incident.Events.Cancelled"));
            }
            catch (Exception ex)
            {
                result.State = ct.IsCancellationRequested ? "Cancelled" : result.Events.Count > 0 || observed > 0 ? "Partial" : "Unavailable";
                result.Warnings.Add(IncidentQueries.Error(ex));
            }
        }
        snapshot.State = ct.IsCancellationRequested || snapshot.Logs.Any(x => x.State == "Cancelled") ? "Cancelled"
            : snapshot.Logs.All(x => x.State == "Unavailable") ? "Unavailable"
            : snapshot.Logs.All(x => x.State == "Complete") ? "Complete" : "Partial";
        snapshot.FinishedAt = DateTimeOffset.Now; return snapshot;
    }
}

internal sealed class ProcessReview(IProcessReviewSource source)
{
    public ProcessReviewSnapshot Collect(int maximum, CancellationToken ct)
    {
        if (maximum is < 1 or > 5000) throw new ArgumentOutOfRangeException(nameof(maximum));
        var snapshot = new ProcessReviewSnapshot(); var watch = Stopwatch.StartNew();
        try
        {
            ct.ThrowIfCancellationRequested(); using var iterator = source.Read(ct).GetEnumerator();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (watch.Elapsed >= TimeSpan.FromSeconds(20))
                {
                    snapshot.Warnings.Add(AppLocalization.T("Incident.Processes.Timeout"));
                    break;
                }
                if (!iterator.MoveNext()) break;
                ct.ThrowIfCancellationRequested();
                if (snapshot.Processes.Count >= maximum)
                {
                    snapshot.Warnings.Add(AppLocalization.T("Incident.Processes.Limit", maximum));
                    break;
                }
                snapshot.Processes.Add(iterator.Current);
            }
            ct.ThrowIfCancellationRequested();
            snapshot.State = snapshot.Warnings.Count > 0 || snapshot.Processes.Any(x => x.Warnings.Count > 0) ? "Partial" : "Complete";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            snapshot.State = "Cancelled";
            snapshot.Warnings.Add(AppLocalization.T("Incident.Processes.Cancelled"));
        }
        catch (Exception ex)
        {
            snapshot.State = ct.IsCancellationRequested ? "Cancelled" : snapshot.Processes.Count == 0 ? "Unavailable" : "Partial";
            snapshot.Warnings.Add(IncidentQueries.Error(ex));
        }
        snapshot.FinishedAt = DateTimeOffset.Now; return snapshot;
    }

    public ProcessOwnerEvidence Owner(ProcessReviewEntry selected, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ProcessOwnerEvidence Result(string state, string owner, string detail)
            => new(selected.Pid, selected.CreationKey, state, owner, detail, DateTimeOffset.Now);
        bool Same(ProcessReviewEntry? row) => row is not null && row.Pid == selected.Pid && row.CreationKey == selected.CreationKey;
        try
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(selected.CreationKey))
                return Result("Unverified", "", AppLocalization.T("Incident.Owner.NoCreation"));
            if (!Same(source.Lookup(selected.Pid, ct)))
                return Result("Stale", "", AppLocalization.T("Incident.Owner.Stale"));
            ct.ThrowIfCancellationRequested(); var owner = source.ReadOwner(selected.Pid, ct); ct.ThrowIfCancellationRequested();
            if (!Same(source.Lookup(selected.Pid, ct)))
                return Result("Stale", "", AppLocalization.T("Incident.Owner.Changed"));
            ct.ThrowIfCancellationRequested();
            return string.IsNullOrWhiteSpace(owner)
                ? Result("Unavailable", "", AppLocalization.T("Incident.Owner.Unavailable"))
                : Result("Verified", owner, AppLocalization.T("Incident.Owner.Verified"));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Result("Cancelled", "", AppLocalization.T("Incident.Owner.Cancelled"));
        }
        catch (Exception ex)
        {
            return Result(ct.IsCancellationRequested ? "Cancelled" : "Unavailable", "", IncidentQueries.Error(ex));
        }
    }
}
