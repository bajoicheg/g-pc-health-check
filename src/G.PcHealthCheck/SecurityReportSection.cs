using System.Globalization;
using System.Net;
using System.Text;

namespace G.PcHealthCheck;

internal static class SecurityReportSection
{
    public static string BuildHtml(ScanResult scan, string language)
    {
        ArgumentNullException.ThrowIfNull(scan);
        var assessment = scan.Security;
        if (assessment is null)
            return "<section id='security-posture'><h2>" + H(T(language, "Security.Tab.Title")) + "</h2><p>" + H(T(language, "Security.Summary.Empty")) + "</p></section>";

        var sb = new StringBuilder();
        sb.Append("<section id='security-posture'><h2>").Append(H(T(language, "Security.Tab.Title"))).Append("</h2>");
        sb.Append("<p><b>").Append(H(T(language, "Security.Summary.Value",
            assessment.Score?.ToString(CultureInfo.InvariantCulture) ?? "—",
            BandText(language, assessment.DisplayBand),
            assessment.CoveragePercent,
            assessment.ModelVersion))).Append("</b></p>");
        if (assessment.CriticalOverrides.Count > 0)
            sb.Append("<p class='muted'>").Append(H(T(language, "Security.Summary.Overrides", string.Join(", ", assessment.CriticalOverrides)))).Append("</p>");

        sb.Append("<table><thead><tr><th>")
            .Append(H(T(language, "Security.Column.Control"))).Append("</th><th>")
            .Append(H(T(language, "Security.Column.Status"))).Append("</th><th>")
            .Append(H(T(language, "Security.Column.Points"))).Append("</th><th>")
            .Append(H(T(language, "Security.Column.Evidence"))).Append("</th><th>")
            .Append(H(T(language, "Security.Column.Recommendation"))).Append("</th><th>")
            .Append(H(T(language, "Security.Column.Source"))).Append("</th></tr></thead><tbody>");

        foreach (var control in assessment.Controls.Concat(assessment.Supplemental))
            AppendControlRow(sb, control, language);

        sb.Append("</tbody></table></section>");
        return sb.ToString();
    }

    public static string BuildClipboardSummary(ScanResult scan, string language)
    {
        ArgumentNullException.ThrowIfNull(scan);
        var assessment = scan.Security;
        if (assessment is null) return T(language, "Security.Summary.Empty");

        var sb = new StringBuilder();
        sb.AppendLine(T(language, "Security.Summary.Value",
            assessment.Score?.ToString(CultureInfo.InvariantCulture) ?? "—",
            BandText(language, assessment.DisplayBand),
            assessment.CoveragePercent,
            assessment.ModelVersion));
        if (assessment.CriticalOverrides.Count > 0)
            sb.AppendLine(T(language, "Security.Summary.Overrides", string.Join(", ", assessment.CriticalOverrides)));

        var attention = assessment.Controls
            .Where(x => x.Status is SecurityControlStatus.Fail or SecurityControlStatus.Warn or SecurityControlStatus.Unknown)
            .Take(8)
            .ToList();
        foreach (var control in attention)
            sb.AppendLine($"- {control.Id}: {StatusText(language, control.Status)}; {PointsText(control)}");

        return sb.ToString().TrimEnd();
    }

    private static void AppendControlRow(StringBuilder sb, SecurityControlResult control, string language)
    {
        var status = StatusText(language, control.Status);
        var points = PointsText(control);
        var evidence = control.Evidence.Count == 0
            ? T(language, "Security.Evidence.None")
            : string.Join("; ", control.Evidence.Select(x => $"{x.Key}={x.Value}"));
        var sources = string.Join(", ", control.Evidence.Select(x => x.Source)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase));
        var recommendation = control.Status is SecurityControlStatus.Pass or SecurityControlStatus.NotApplicable
            ? "—"
            : Guidance(language, control.GuidanceCode);
        var pill = control.Status switch
        {
            SecurityControlStatus.Pass => "ok",
            SecurityControlStatus.Fail => "crit",
            SecurityControlStatus.Warn => "warn",
            _ => "warn"
        };

        sb.Append("<tr><td>").Append(H(control.Id)).Append("</td><td><span class='pill ").Append(pill).Append("'>")
            .Append(H(status)).Append("</span></td><td>").Append(H(points)).Append("</td><td>")
            .Append(H(evidence)).Append("</td><td>").Append(H(recommendation)).Append("</td><td>")
            .Append(H(sources)).Append("</td></tr>");
    }

    private static string Guidance(string language, string guidanceCode)
    {
        var key = "Security.Guidance." + guidanceCode;
        var value = T(language, key);
        return value == key ? guidanceCode : value;
    }

    private static string PointsText(SecurityControlResult result)
    {
        if (result.Status is SecurityControlStatus.Unknown or SecurityControlStatus.NotApplicable) return "—";
        return $"{result.Weight * result.EarnedFraction:0.#}/{result.Weight}";
    }

    private static string StatusText(string language, SecurityControlStatus status) => status switch
    {
        SecurityControlStatus.Pass => T(language, "Security.Status.Pass"),
        SecurityControlStatus.Warn => T(language, "Security.Status.Warn"),
        SecurityControlStatus.Fail => T(language, "Security.Status.Fail"),
        SecurityControlStatus.NotApplicable => T(language, "Security.Status.NotApplicable"),
        _ => T(language, "Security.Status.Unknown")
    };

    private static string BandText(string language, SecurityBand band) => band switch
    {
        SecurityBand.High => T(language, "Security.Band.High"),
        SecurityBand.Good => T(language, "Security.Band.Good"),
        SecurityBand.NeedsAttention => T(language, "Security.Band.NeedsAttention"),
        SecurityBand.Low => T(language, "Security.Band.Low"),
        _ => T(language, "Security.Band.AssessmentIncomplete")
    };

    private static string T(string language, string key, params object?[] args)
    {
        var normalized = AppLocalization.NormalizeLanguage(language);
        var culture = CultureInfo.GetCultureInfo(normalized == "en" ? "en-US" : "ru-RU");
        var text = AppLocalization.TextForCulture(normalized, key);
        return args.Length == 0 ? text : string.Format(culture, text, args);
    }

    private static string H(string value) => WebUtility.HtmlEncode(value);
}
