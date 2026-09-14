using System.Net;
using System.Text;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class IncidentReport
{
    public static string Boundary => AppLocalization.T("Incident.Report.Boundary");

    public static string StateText(string state) => state switch
    {
        "Complete" => AppLocalization.T("Incident.State.Complete"),
        "Partial" => AppLocalization.T("Incident.State.Partial"),
        "Unavailable" => AppLocalization.T("Incident.State.Unavailable"),
        "Cancelled" => AppLocalization.T("Incident.State.Cancelled"),
        "Verified" => AppLocalization.T("Incident.State.Verified"),
        "Stale" => AppLocalization.T("Incident.State.Stale"),
        "Unverified" => AppLocalization.T("Incident.State.Unverified"),
        _ => AppLocalization.T("Incident.State.Unknown")
    };

    public static string LevelText(int? level) => level switch
    {
        0 => AppLocalization.T("Incident.Level.None"),
        1 => AppLocalization.T("Incident.Level.Critical"),
        2 => AppLocalization.T("Incident.Level.Error"),
        3 => AppLocalization.T("Incident.Level.Warning"),
        4 => AppLocalization.T("Incident.Level.Information"),
        5 => AppLocalization.T("Incident.Level.Verbose"),
        _ => AppLocalization.T("Incident.Level.Unknown")
    };

    public static string MessageStateText(string state) => state switch
    {
        "Available" => AppLocalization.T("Incident.MessageState.Available"),
        "Truncated" => AppLocalization.T("Incident.MessageState.Truncated"),
        "Unavailable" => AppLocalization.T("Incident.MessageState.Unavailable"),
        _ => AppLocalization.T("Incident.MessageState.Unknown")
    };

    public static string Json(object snapshot, object filter)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(filter);
        var visible = Count(snapshot, filter);
        return JsonSerializer.Serialize(new { SchemaVersion = 1, Snapshot = snapshot, Filter = filter, VisibleCount = visible, Boundary }, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string Summary(object snapshot, object filter)
    {
        var visible = Count(snapshot, filter); var sb = new StringBuilder(AppLocalization.T("Incident.Report.Title") + "\n");
        if (snapshot is IncidentSnapshot events)
        {
            sb.AppendLine(AppLocalization.T("Incident.Report.Collection", events.StartedAt, events.FinishedAt, StateText(events.State)));
            sb.AppendLine(AppLocalization.T("Incident.Report.Interval", events.Window.From, events.Window.To, events.Window.MaxPerLog));
            sb.AppendLine(AppLocalization.T("Incident.Report.EventCounts", events.Logs.Sum(x => x.Events.Count), visible));
            foreach (var log in events.Logs)
            {
                sb.AppendLine(AppLocalization.T("Incident.Report.LogCounts", log.Log, StateText(log.State), log.Events.Count, log.Warnings.Count));
                foreach (var warning in log.Warnings.Take(10)) sb.AppendLine("  " + warning);
            }
            foreach (var e in IncidentQueries.Events(events, (IncidentFilter)filter).Take(12))
                sb.AppendLine($"{e.Timestamp:O} | {e.Log} | {e.Provider} | ID {e.EventId} | {LevelText(e.Level)}");
        }
        else if (snapshot is ProcessReviewSnapshot processes)
        {
            sb.AppendLine(AppLocalization.T("Incident.Report.Collection", processes.StartedAt, processes.FinishedAt, StateText(processes.State)));
            sb.AppendLine(AppLocalization.T("Incident.Report.ProcessCounts", processes.Processes.Count, visible, processes.Processes.Count(x => x.Warnings.Count > 0)));
            foreach (var w in processes.Warnings) sb.AppendLine(w);
            foreach (var p in IncidentQueries.Processes(processes, (string)filter).Take(12))
                sb.AppendLine(AppLocalization.T("Incident.Report.ProcessLine", p.Pid, p.Name, p.CreatedAt, p.WorkingSetBytes?.ToString() ?? "—"));
            foreach (var o in processes.OwnerChecks.TakeLast(10))
                sb.AppendLine($"{o.CheckedAt:O} PID {o.Pid}: {StateText(o.State)}; {o.Owner}; {o.Detail}");
            sb.AppendLine(AppLocalization.T("Incident.Report.ProcessBoundary"));
        }
        sb.AppendLine(AppLocalization.T("Incident.Report.FullEvidence"));
        sb.AppendLine(Boundary); return sb.ToString();
    }

    public static string Html(object snapshot, object filter)
    {
        var summary = Summary(snapshot, filter);
        var title = AppLocalization.T("Incident.Report.Title");
        var sb = new StringBuilder("<!doctype html><html lang='")
            .Append(H(AppLocalization.Language))
            .Append("'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>")
            .Append(H(title))
            .Append("</title><style>body{font:14px/1.5 'Segoe UI',sans-serif;margin:24px}table{border-collapse:collapse;width:100%}th,td{border:1px solid #bbb;padding:8px;text-align:left;vertical-align:top;overflow-wrap:anywhere}pre{white-space:pre-wrap;overflow-wrap:anywhere}h2{margin-top:28px}</style><h1>")
            .Append(H(title)).Append("</h1><pre>");
        sb.Append(H(summary)).Append("</pre><h2>").Append(H(AppLocalization.T("Incident.Report.FullSnapshot"))).Append("</h2>");
        if (snapshot is IncidentSnapshot events)
        {
            sb.Append("<table><tr>");
            foreach (var header in AppLocalization.T("Incident.Report.Events.Headers").Split('|')) sb.Append("<th>").Append(H(header)).Append("</th>");
            sb.Append("</tr>");
            foreach (var e in IncidentQueries.Events(events, new()))
                sb.Append("<tr><td>").Append(H($"{e.Timestamp:O}\n{e.Log}\nRecord {e.RecordId}"))
                    .Append("</td><td>").Append(H($"{e.EventId}\n{e.Provider}"))
                    .Append("</td><td>").Append(H($"{LevelText(e.Level)}\n{e.EmitterPid}"))
                    .Append("</td><td><pre>").Append(H(e.Message)).Append("</pre>")
                    .Append(H(MessageStateText(e.MessageState))).Append("</td></tr>");
            sb.Append("</table>");
            foreach (var log in events.Logs)
            {
                sb.Append("<h2>").Append(H(AppLocalization.T("Incident.Report.LogWarnings", log.Log))).Append("</h2><pre>")
                    .Append(H(string.Join("\n", log.Warnings))).Append("</pre>");
            }
        }
        else if (snapshot is ProcessReviewSnapshot processes)
        {
            sb.Append("<table><tr>");
            foreach (var header in AppLocalization.T("Incident.Report.Process.Headers").Split('|')) sb.Append("<th>").Append(H(header)).Append("</th>");
            sb.Append("</tr>");
            foreach (var p in IncidentQueries.Processes(processes, ""))
                sb.Append("<tr><td>").Append(H($"{p.Pid}\n{p.Name}"))
                    .Append("</td><td>").Append(H($"{p.CreatedAt:O}\n{p.CreationKey}\nPPID {p.ParentPid}; session {p.SessionId}"))
                    .Append("</td><td><pre>").Append(H(p.Executable + "\n" + p.CommandLine)).Append("</pre></td><td>")
                    .Append(H($"{p.WorkingSetBytes?.ToString() ?? "—"} bytes; threads {p.Threads}; handles {p.Handles}"))
                    .Append("</td><td>").Append(H(string.Join("; ", p.Warnings))).Append("</td></tr>");
            sb.Append("</table><h2>").Append(H(AppLocalization.T("Incident.Report.OwnerChecks"))).Append("</h2><pre>");
            foreach (var o in processes.OwnerChecks)
                sb.Append(H($"{o.CheckedAt:O} | PID {o.Pid} | {o.CreationKey} | {StateText(o.State)} | {o.Owner} | {o.Detail}\n"));
            sb.Append("</pre>");
        }
        return sb.Append("</html>").ToString();
    }

    private static int Count(object snapshot, object filter) => snapshot switch
    {
        IncidentSnapshot s when filter is IncidentFilter f => IncidentQueries.Events(s, f).Count,
        ProcessReviewSnapshot s when filter is string f => IncidentQueries.Processes(s, f).Count,
        _ => throw new ArgumentException(AppLocalization.T("Incident.Report.Unsupported"))
    };

    private static string H(string value) => WebUtility.HtmlEncode(value);
}
