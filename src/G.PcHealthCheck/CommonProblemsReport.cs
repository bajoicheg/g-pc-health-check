using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace G.PcHealthCheck;

internal static class CommonProblemsReport
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private static string Scope => AppLocalization.T("CommonProblems.Report.Scope");

    public static string Summary(CommonProblemSnapshot after, CommonProblemSnapshot? before, RemediationBatchResult? lastCommand)
    {
        ArgumentNullException.ThrowIfNull(after);
        var sb = new StringBuilder(AppLocalization.T("CommonProblems.Report.SummaryTitle", Application.ProductVersion) + "\n");
        sb.AppendLine(Scope);
        sb.AppendLine(AppLocalization.T("CommonProblems.Report.CurrentSnapshot", after.CollectedAt));
        if (before is not null) sb.AppendLine(AppLocalization.T("CommonProblems.Report.PreviousSnapshot", before.CollectedAt));
        foreach (var row in CommonProblemsAssessment.Assess(after))
        {
            sb.AppendLine($"[{row.Status}] {row.Title}");
            sb.AppendLine(row.Evidence);
            sb.AppendLine(AppLocalization.T("CommonProblems.Report.WhatToDo", row.Resolution));
        }
        if (lastCommand is not null)
            foreach (var action in lastCommand.Actions)
                sb.AppendLine(AppLocalization.T(
                    "CommonProblems.Report.LastCommand",
                    action.Id,
                    action.Success,
                    action.ExitCode,
                    action.Message));
        return sb.ToString();
    }

    public static string Json(CommonProblemSnapshot after, CommonProblemSnapshot? before, RemediationBatchResult? lastCommand)
    {
        ArgumentNullException.ThrowIfNull(after);
        return JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            Product = "G PC Health Check",
            Version = Application.ProductVersion,
            Scope,
            PreviousSnapshot = before,
            CurrentSnapshot = after,
            PreviousFindings = before is null ? null : CommonProblemsAssessment.Assess(before),
            CurrentFindings = CommonProblemsAssessment.Assess(after),
            LastCommand = lastCommand
        }, Options);
    }

    public static string Html(CommonProblemSnapshot after, CommonProblemSnapshot? before, RemediationBatchResult? lastCommand)
    {
        ArgumentNullException.ThrowIfNull(after);
        var sb = new StringBuilder("<!doctype html><html lang='")
            .Append(H(AppLocalization.Language))
            .Append("'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'><title>")
            .Append(H(AppLocalization.T("CommonProblems.Report.HtmlTitle")))
            .Append("</title><style>body{font:16px system-ui,sans-serif;max-width:1200px;margin:32px auto;padding:0 20px;color:#17334d}table{border-collapse:collapse;width:100%;margin:20px 0}th,td{border:1px solid #ccd6df;padding:12px;vertical-align:top;text-align:left;white-space:pre-wrap;overflow-wrap:anywhere}th{background:#edf3f7}p{line-height:1.6}h2{margin-top:32px}</style></head><body><h1>")
            .Append(H(AppLocalization.T("CommonProblems.Report.Heading")))
            .Append("</h1>");
        sb.Append("<p>G PC Health Check ").Append(H(Application.ProductVersion)).Append("</p><p>").Append(H(Scope)).Append("</p>");
        if (before is not null) Table(sb, AppLocalization.T("CommonProblems.Report.PreviousHeading"), before);
        Table(sb, AppLocalization.T("CommonProblems.Report.CurrentHeading"), after);
        if (lastCommand is not null)
        {
            sb.Append("<h2>").Append(H(AppLocalization.T("CommonProblems.Report.LastCommandHeading"))).Append("</h2><p>")
                .Append(H(AppLocalization.T("CommonProblems.Report.LastCommandNote"))).Append("</p><table><tr><th>")
                .Append(H(AppLocalization.T("CommonProblems.Report.Column.Action"))).Append("</th><th>")
                .Append(H(AppLocalization.T("CommonProblems.Report.Column.CommandResult"))).Append("</th><th>")
                .Append(H(AppLocalization.T("CommonProblems.Report.Column.Details"))).Append("</th></tr>");
            foreach (var action in lastCommand.Actions)
                sb.Append("<tr><td>").Append(H(action.Id)).Append("</td><td>")
                    .Append(H(AppLocalization.T(action.Success ? "CommonProblems.Report.CommandSuccess" : "CommonProblems.Report.CommandError")))
                    .Append("; code=").Append(H(action.ExitCode?.ToString() ?? "—")).Append("</td><td>")
                    .Append(H(action.Message)).Append("</td></tr>");
            sb.Append("</table>");
        }
        return sb.Append("<p>").Append(H(AppLocalization.T("CommonProblems.Report.Privacy"))).Append("</p></body></html>").ToString();
    }

    private static void Table(StringBuilder sb, string title, CommonProblemSnapshot snapshot)
    {
        sb.Append("<h2>").Append(H(title)).Append("</h2><p>").Append(H(snapshot.CollectedAt.ToString("O"))).Append("</p><table><tr><th>")
            .Append(H(AppLocalization.T("CommonProblems.Report.Column.Status"))).Append("</th><th>")
            .Append(H(AppLocalization.T("CommonProblems.Report.Column.Check"))).Append("</th><th>")
            .Append(H(AppLocalization.T("CommonProblems.Report.Column.Facts"))).Append("</th><th>")
            .Append(H(AppLocalization.T("CommonProblems.Report.Column.Steps"))).Append("</th></tr>");
        foreach (var row in CommonProblemsAssessment.Assess(snapshot))
            sb.Append("<tr><td>").Append(H(row.Status)).Append("</td><td>").Append(H(row.Title)).Append("</td><td>")
                .Append(H(row.Evidence)).Append("</td><td>").Append(H(row.Resolution)).Append("</td></tr>");
        sb.Append("</table>");
    }

    private static string H(string value) => WebUtility.HtmlEncode(value);
}
