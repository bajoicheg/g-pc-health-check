using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace G.PcHealthCheck;

internal static class ReviewReport
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public static string Title(object snapshot) => snapshot switch
    {
        TempPreviewSnapshot => AppLocalization.T("Review.Report.Title.Temp"),
        StartupReviewSnapshot => AppLocalization.T("Review.Report.Title.Startup"),
        _ => throw new ArgumentException(AppLocalization.T("Review.SnapshotTypeMismatch"), nameof(snapshot))
    };
    public static string State(ReviewCollectionState state) => state switch
    {
        ReviewCollectionState.Complete => AppLocalization.T("Review.Report.State.Complete"),
        ReviewCollectionState.Partial => AppLocalization.T("Review.Report.State.Partial"),
        ReviewCollectionState.Missing => AppLocalization.T("Review.Report.State.Missing"),
        _ => AppLocalization.T("Review.Report.State.Unavailable")
    };
    public static string Bytes(long bytes) => HumanSize.Megabytes(bytes);

    internal static string StartupStateText(string value)
        => string.Equals(value, "Не определено", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Not determined", StringComparison.OrdinalIgnoreCase)
            ? AppLocalization.T("Common.NotDetermined")
            : value;

    internal static string StartupScopeText(string value)
        => string.Equals(value, "Все пользователи", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "All users", StringComparison.OrdinalIgnoreCase)
            ? AppLocalization.T("Review.Startup.AllUsers")
            : value;

    public static string Overview(object snapshot)
    {
        var sb = new StringBuilder(); sb.AppendLine(Title(snapshot));
        if (snapshot is TempPreviewSnapshot t)
        {
            sb.AppendLine(AppLocalization.T("Review.Report.Snapshot", State(t.State), t.CompletedAt));
            sb.AppendLine(AppLocalization.T("Review.Report.TempOwner", ExecutionPolicy.Value(t.TargetAccount), ExecutionPolicy.Value(t.TargetSid)));
            sb.AppendLine(ExecutionPolicy.Describe(t.ExecutionContext));
            sb.AppendLine(AppLocalization.T("Review.Report.Scope", t.Root.Length == 0 ? AppLocalization.T("ExecutionContext.Scope.Undefined") : t.Root));
            sb.AppendLine(AppLocalization.T("Review.Report.Rule", t.Cutoff, t.OlderThanDays));
            if (t.State == ReviewCollectionState.Unavailable) sb.AppendLine(AppLocalization.T("Review.Report.TempUnavailable"));
            else sb.AppendLine(AppLocalization.T("Review.Report.TempEstimate", t.CandidateFiles, Bytes(t.CandidateBytes)));
            sb.AppendLine(AppLocalization.T("Review.Report.TempVisited", t.VisitedEntries, t.SkippedLinks, t.Errors));
            sb.AppendLine(AppLocalization.T("Review.Report.TempLargest", t.LargestFiles.Count, t.CandidateFiles, t.MaxEntries, t.TimeLimitSeconds));
            sb.AppendLine(AppLocalization.T("Review.Report.TempBoundary"));
            sb.AppendLine(AppLocalization.T(t.CleanupAvailability == "Ready"
                ? "Review.Report.TempCleanupReady"
                : "Review.Report.TempCleanupUnavailable"));
            foreach (var issue in t.Issues) sb.AppendLine("! " + issue);
            if (t.OmittedIssues > 0) sb.AppendLine(AppLocalization.T("Review.Report.MoreMessages", t.OmittedIssues));
        }
        else if (snapshot is StartupReviewSnapshot s)
        {
            sb.AppendLine(AppLocalization.T("Review.Report.Snapshot", State(s.State), s.CollectedAt));
            sb.AppendLine(AppLocalization.T("Review.Report.StartupAccount", s.Account, s.Entries.Count, s.Sources.Count));
            sb.AppendLine(ExecutionPolicy.Describe(s.ExecutionContext));
            sb.AppendLine(StartupReviewService.ScopeNote);
            sb.AppendLine(AppLocalization.T("Review.Report.StartupBoundary"));
            foreach (var source in s.Sources)
                sb.AppendLine(AppLocalization.T("Review.Report.StartupSource", source.Name, StartupScopeText(source.Scope), State(source.State), source.Items, source.Detail));
            foreach (var issue in s.Issues) sb.AppendLine("! " + issue);
        }
        return sb.ToString().TrimEnd();
    }
    public static string Summary(object snapshot)
    {
        var sb = new StringBuilder("G PC Health Check\n"); sb.AppendLine(Overview(snapshot));
        if (snapshot is TempPreviewSnapshot t)
            foreach (var row in t.LargestFiles) sb.AppendLine($"{HumanSize.Megabytes(row.Bytes)} | {row.LastWriteTime:O} | {row.Path}");
        else if (snapshot is StartupReviewSnapshot s)
            foreach (var row in s.Entries)
                sb.AppendLine($"{row.Name} | {StartupScopeText(row.Scope)} | {row.Source} | {StartupStateText(row.State)}\n{row.Command}");
        return sb.ToString();
    }
    public static string Json(object snapshot)
    {
        _ = Title(snapshot);
        return JsonSerializer.Serialize(new { SchemaVersion = 1, Kind = snapshot is TempPreviewSnapshot ? "TempPreview" : "StartupReview", Snapshot = snapshot }, Options);
    }
    public static string Html(object snapshot)
    {
        var title = Title(snapshot);
        var sb = new StringBuilder("<!doctype html><html lang='")
            .Append(H(AppLocalization.Language))
            .Append("'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>");
        sb.Append(H(title)).Append("</title><style>body{margin:0;background:#f4f7fa;color:#172432;font:14px/1.5 'Segoe UI',Arial,sans-serif}header{padding:24px;background:#15344f;color:white}main{padding:24px;max-width:1500px;margin:auto}section{background:white;border:1px solid #dce5ed;border-radius:12px;padding:18px;margin-bottom:18px}pre{white-space:pre-wrap;font:inherit;overflow-wrap:anywhere}table{width:100%;border-collapse:collapse}th,td{padding:10px;text-align:left;vertical-align:top;border-bottom:1px solid #dce5ed;white-space:pre-wrap;overflow-wrap:anywhere}th{background:#edf3f8}.table{overflow-x:auto}footer{padding:24px;color:#556}h1{margin:0;font-size:24px}</style></head><body><header><h1>G PC Health Check · ");
        sb.Append(H(title)).Append("</h1></header><main><section><pre>").Append(H(Overview(snapshot))).Append("</pre></section><section class='table'><table><thead><tr>");
        if (snapshot is TempPreviewSnapshot t)
        {
            foreach (var h in AppLocalization.T("Review.Report.Html.TempHeaders").Split('|')) sb.Append("<th>").Append(H(h)).Append("</th>");
            sb.Append("</tr></thead><tbody>");
            foreach (var row in t.LargestFiles) Row(sb, HumanSize.Megabytes(row.Bytes), row.LastWriteTime.ToString("O"), row.Path);
        }
        else if (snapshot is StartupReviewSnapshot s)
        {
            foreach (var h in AppLocalization.T("Review.Report.Html.StartupHeaders").Split('|')) sb.Append("<th>").Append(H(h)).Append("</th>");
            sb.Append("</tr></thead><tbody>");
            foreach (var row in s.Entries) Row(sb, row.Name, row.Command, StartupScopeText(row.Scope), row.Source, StartupStateText(row.State));
        }
        return sb.Append("</tbody></table></section></main><footer>")
            .Append(H(AppLocalization.T("Review.Report.Footer")))
            .Append("</footer></body></html>").ToString();
    }
    private static void Row(StringBuilder sb, params string[] values)
    {
        sb.Append("<tr>"); foreach (var value in values) sb.Append("<td>").Append(H(value)).Append("</td>"); sb.Append("</tr>");
    }
    private static string H(string value) => WebUtility.HtmlEncode(value);
}
