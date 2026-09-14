using System.Net;
using System.Text;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class ResourceProbeReport
{
    public static string Boundary => AppLocalization.T("ResourceProbe.Boundary");

    public static string OutcomeText(string outcome) => outcome switch
    {
        "Connected" => AppLocalization.T("ResourceProbe.Outcome.Connected"),
        "Partial" => AppLocalization.T("ResourceProbe.Outcome.Partial"),
        "Failed" => AppLocalization.T("ResourceProbe.Outcome.Failed"),
        "Cancelled" => AppLocalization.T("ResourceProbe.Outcome.Cancelled"),
        "Resolved" => AppLocalization.T("ResourceProbe.Outcome.Resolved"),
        "Skipped" => AppLocalization.T("ResourceProbe.Outcome.Skipped"),
        "Refused" => AppLocalization.T("ResourceProbe.Outcome.Refused"),
        "Timeout" => AppLocalization.T("ResourceProbe.Outcome.Timeout"),
        "Unreachable" => AppLocalization.T("ResourceProbe.Outcome.Unreachable"),
        "Denied" => AppLocalization.T("ResourceProbe.Outcome.Denied"),
        _ => AppLocalization.T("ResourceProbe.Outcome.Unknown")
    };

    public static string Summary(ResourceProbeSnapshot current, ResourceProbeSnapshot? previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        var sb = new StringBuilder(AppLocalization.T("ResourceProbe.Report.Title") + "\n");
        AppendSummary(sb, current, AppLocalization.T("ResourceProbe.Report.CurrentAttempt"));
        if (previous is not null)
        {
            sb.AppendLine(AppLocalization.T(current.Target == previous.Target
                ? "ResourceProbe.Report.SameTarget"
                : "ResourceProbe.Report.DifferentTarget"));
            AppendSummary(sb, previous, AppLocalization.T("ResourceProbe.Report.PreviousAttempt"));
        }
        return sb.AppendLine(Boundary).ToString();
    }

    private static void AppendSummary(StringBuilder sb, ResourceProbeSnapshot s, string title)
    {
        sb.AppendLine($"{title}: {s.StartedAt:yyyy-MM-dd HH:mm:ss zzz} → {s.FinishedAt:HH:mm:ss zzz}");
        sb.AppendLine(AppLocalization.T("ResourceProbe.Report.Target", s.Target.Host, s.Target.Port, OutcomeText(s.Outcome), s.Outcome));
        sb.AppendLine(AppLocalization.T("ResourceProbe.Report.Limits", s.Options.DnsTimeoutMs, s.Options.TcpTimeoutMs, s.Options.MaxAddresses));
        sb.AppendLine(AppLocalization.T("ResourceProbe.Report.Addresses", s.Addresses.Count == 0 ? AppLocalization.T("ResourceProbe.Report.None") : string.Join(", ", s.Addresses)));
        foreach (var x in s.Steps)
            sb.AppendLine(AppLocalization.T("ResourceProbe.Report.Step", x.Stage, x.Endpoint, OutcomeText(x.Outcome), x.ElapsedMs, x.ErrorCode, x.LocalAddress, x.Detail));
        foreach (var w in s.Warnings)
            sb.AppendLine(AppLocalization.T("ResourceProbe.Report.Note", w));
    }

    public static string Json(ResourceProbeSnapshot current, ResourceProbeSnapshot? previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        return JsonSerializer.Serialize(new { SchemaVersion = 1, Kind = "ResourceDiagnostics", Boundary, Current = current, Previous = previous }, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string Html(ResourceProbeSnapshot current, ResourceProbeSnapshot? previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        var language = AppLocalization.Language;
        var sb = new StringBuilder("<!doctype html><html lang=\"")
            .Append(H(language))
            .Append("\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>")
            .Append(H(AppLocalization.T("ResourceProbe.Report.HtmlTitle")))
            .Append("</title><style>body{font:15px/1.5 'Segoe UI',sans-serif;background:#f4f7fa;color:#172432;margin:24px}main{max-width:1280px;margin:auto}section{background:white;padding:20px;margin:16px 0;border:1px solid #dce5ed;border-radius:12px}table{width:100%;border-collapse:collapse}td,th{padding:9px;text-align:left;border-bottom:1px solid #dce5ed;vertical-align:top;overflow-wrap:anywhere}pre{white-space:pre-wrap;overflow-wrap:anywhere}small{color:#526475}</style></head><body><main><h1>")
            .Append(H(AppLocalization.T("ResourceProbe.Report.Title")))
            .Append("</h1>");
        sb.Append("<p>").Append(H(Boundary)).Append("</p>");
        Section(sb, current, AppLocalization.T("ResourceProbe.Report.CurrentAttempt"));
        if (previous is not null)
        {
            sb.Append("<p>")
                .Append(H(AppLocalization.T(current.Target == previous.Target
                    ? "ResourceProbe.Report.HtmlSameTarget"
                    : "ResourceProbe.Report.HtmlDifferentTarget")))
                .Append("</p>");
            Section(sb, previous, AppLocalization.T("ResourceProbe.Report.PreviousAttempt"));
        }
        return sb.Append("</main></body></html>").ToString();
    }

    private static void Section(StringBuilder sb, ResourceProbeSnapshot s, string title)
    {
        sb.Append("<section><h2>").Append(H(title)).Append("</h2><p><b>").Append(H(s.Target.Host)).Append(" · TCP ").Append(s.Target.Port).Append(" · ").Append(H(OutcomeText(s.Outcome))).Append("</b></p>");
        sb.Append("<p>").Append(H($"{s.StartedAt:yyyy-MM-dd HH:mm:ss zzz} → {s.FinishedAt:yyyy-MM-dd HH:mm:ss zzz}")).Append("</p>");
        sb.Append("<p>").Append(H(AppLocalization.T("ResourceProbe.Report.HtmlLimits", s.Options.DnsTimeoutMs, s.Options.TcpTimeoutMs, s.Options.MaxAddresses))).Append("</p>");
        sb.Append("<p>").Append(H(AppLocalization.T("ResourceProbe.Report.HtmlAddresses", string.Join(", ", s.Addresses)))).Append("</p><table><thead><tr><th>")
            .Append(H(AppLocalization.T("ResourceProbe.Report.HtmlStageAddress")))
            .Append("</th><th>").Append(H(AppLocalization.T("ResourceProbe.Report.HtmlResult")))
            .Append("</th><th>").Append(H(AppLocalization.T("ResourceProbe.Report.HtmlDuration")))
            .Append("</th><th>").Append(H(AppLocalization.T("ResourceProbe.Report.HtmlLocalIp")))
            .Append("</th><th>").Append(H(AppLocalization.T("ResourceProbe.Report.HtmlCodeDetails")))
            .Append("</th></tr></thead><tbody>");
        foreach (var x in s.Steps)
            sb.Append("<tr><td>").Append(H(x.Stage + " " + x.Endpoint)).Append("</td><td>").Append(H(OutcomeText(x.Outcome))).Append("</td><td>").Append(H(x.ElapsedMs.ToString("0.##", AppLocalization.Culture))).Append("</td><td>").Append(H(x.LocalAddress)).Append("</td><td>").Append(H(x.ErrorCode + " " + x.Detail)).Append("</td></tr>");
        sb.Append("</tbody></table>");
        foreach (var w in s.Warnings) sb.Append("<p>").Append(H(w)).Append("</p>");
        sb.Append("</section>");
    }

    private static string H(string value) => WebUtility.HtmlEncode(value);
}
