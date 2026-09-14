using System.Net;
using System.Text;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class FileUseReport
{
    public static string Meaning => AppLocalization.T("FileUse.Report.Meaning");
    public static string NextSteps => AppLocalization.T("FileUse.Report.NextSteps");
    public static string Summary(FileUseSnapshot current, FileUseSnapshot? previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        var s = new StringBuilder();
        s.AppendLine(AppLocalization.T("FileUse.Report.Title"));
        s.AppendLine(AppLocalization.T("FileUse.Report.Target", current.TargetPath));
        s.AppendLine(AppLocalization.T("FileUse.Report.Computer", current.ComputerName, current.StartedAt, current.FinishedAt));
        s.AppendLine(AppLocalization.T("FileUse.Report.Collection", FileUseCore.StateText(current.State), current.Stage));
        s.AppendLine(FileUseCore.Verdict(current));
        s.AppendLine(AppLocalization.T("FileUse.Report.Counts", current.Processes.Count, current.Processes.Count(x => x.IdentityState == "Matched"), current.QueryAttempts));
        s.AppendLine(AppLocalization.T("FileUse.Report.Errors", current.ErrorCode?.ToString() ?? "—", current.EndSessionCode?.ToString() ?? "—"));
        foreach (var warning in current.Warnings) s.AppendLine("! " + warning);
        if (previous is not null)
        {
            s.AppendLine(AppLocalization.T("FileUse.Report.Previous", previous.TargetPath, previous.StartedAt, FileUseCore.StateText(previous.State)));
            s.AppendLine(AppLocalization.T("FileUse.Report.PreviousMeaning"));
        }
        s.AppendLine(Meaning); s.AppendLine(NextSteps);
        s.AppendLine(ExecutionPolicy.Describe(current.ExecutionContext));
        s.AppendLine(AppLocalization.T("FileUse.Report.RmLifecycle"));
        s.AppendLine(AppLocalization.T("FileUse.Report.Privacy"));
        return s.ToString().TrimEnd();
    }
    public static string Json(FileUseSnapshot current, FileUseSnapshot? previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        return JsonSerializer.Serialize(new { SchemaVersion = 1, Current = current, Previous = previous, Interpretation = Meaning, NextSteps }, new JsonSerializerOptions { WriteIndented = true });
    }
    public static string Html(FileUseSnapshot current, FileUseSnapshot? previous)
    {
        var s = new StringBuilder("<!doctype html><html lang='").Append(AppLocalization.Language)
            .Append("'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>")
            .Append(H(AppLocalization.T("FileUse.Report.Title")))
            .Append("</title><style>body{font:14px/1.5 'Segoe UI',sans-serif;margin:24px}pre{white-space:pre-wrap;overflow-wrap:anywhere}table{border-collapse:collapse;width:100%}td,th{border:1px solid #ddd;text-align:left;padding:8px;vertical-align:top;overflow-wrap:anywhere}th{background:#f3f5f7}section{overflow:auto;margin-bottom:24px}</style></head><body><h1>")
            .Append(H(AppLocalization.T("FileUse.Report.Heading"))).Append("</h1><pre>");
        s.Append(H(Summary(current, previous))).Append("</pre>");
        Snapshot(s, current, AppLocalization.T("FileUse.Report.Current"));
        if (previous is not null) Snapshot(s, previous, AppLocalization.T("FileUse.Report.PreviousHeading"));
        return s.Append("</body></html>").ToString();
    }
    private static void Snapshot(StringBuilder s, FileUseSnapshot data, string title)
    {
        s.Append("<section><h2>").Append(H(title)).Append("</h2><pre>").Append(H(Summary(data, null))).Append("</pre>");
        s.Append("<table><thead><tr><th>").Append(H(AppLocalization.T("FileUse.Report.Column.PidCreated")))
            .Append("</th><th>").Append(H(AppLocalization.T("FileUse.Report.Column.Application")))
            .Append("</th><th>").Append(H(AppLocalization.T("FileUse.Report.Column.Service")))
            .Append("</th><th>").Append(H(AppLocalization.T("FileUse.Report.Column.TypeSession")))
            .Append("</th><th>").Append(H(AppLocalization.T("FileUse.Report.Column.Exe")))
            .Append("</th><th>").Append(H(AppLocalization.T("FileUse.Report.Column.Details"))).Append("</th></tr></thead><tbody>");
        foreach (var row in data.Processes)
        {
            s.Append("<tr><td>").Append(row.Pid).Append("<br>").Append(H(Time(row.StartFileTime)))
                .Append("</td><td>").Append(H(Value(row.ApplicationName))).Append("</td><td>").Append(H(Value(row.ServiceName)))
                .Append("</td><td>").Append(H(FileUseCore.TypeText(row.ApplicationType))).Append("<br>").Append(H(FileUseCore.Session(row)))
                .Append("</td><td>").Append(H(Value(row.ImagePath))).Append("</td><td><pre>").Append(H(Detail(row))).Append("</pre></td></tr>");
        }
        s.Append("</tbody></table><p>").Append(H(AppLocalization.T("FileUse.Report.RebootReasons", data.RebootReasons?.ToString() ?? "—")))
            .Append("</p></section>");
    }
    public static string Detail(FileUseProcess row)
        => AppLocalization.T(
            "FileUse.Report.Detail",
            row.Pid,
            Time(row.StartFileTime),
            Value(row.ApplicationName),
            Value(row.ServiceName),
            FileUseCore.TypeText(row.ApplicationType),
            row.ApplicationType,
            FileUseCore.Session(row),
            Value(row.ImagePath),
            FileUseCore.IdentityText(row.IdentityState),
            row.IdentityError?.ToString() ?? "—",
            row.ApplicationStatus.ToString("X8"),
            row.Restartable,
            NextSteps);
    public static string Save(FileUseSnapshot current, FileUseSnapshot? previous, string parent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parent);
        var html = Html(current, previous); var json = Json(current, previous);
        var folder = Path.Combine(parent, $"FileUse_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"); Directory.CreateDirectory(folder);
        Write(Path.Combine(folder, "file-use.html"), html); Write(Path.Combine(folder, "file-use.json"), json); return folder;
    }
    private static void Write(string path, string content)
    { using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); using var writer = new StreamWriter(stream, new UTF8Encoding(false)); writer.Write(content); }
    private static string Time(ulong fileTime) => FileUseCore.StartTime(fileTime)?.ToString("O") ?? "—";
    private static string Value(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? "");
}
