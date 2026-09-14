using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace G.PcHealthCheck;

internal static class PerformanceSessionReport
{
    public static string Boundary => AppLocalization.T("Performance.Report.Boundary");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static string Json(PerformanceSessionSnapshot snapshot)
        => JsonSerializer.Serialize(new { SchemaVersion = 1, Product = "G PC Health Check", Boundary, DataCompleteness = PerformanceStatistics.Completeness(snapshot), Snapshot = snapshot, Statistics = Enum.GetValues<SessionMetric>().Select(m => PerformanceStatistics.For(snapshot, m)).ToArray() }, Options);

    public static string Summary(PerformanceSessionSnapshot snapshot)
    {
        var sb = new StringBuilder(AppLocalization.T("Performance.Report.Title") + Environment.NewLine);
        sb.AppendLine(AppLocalization.T("Performance.Report.StartEnd", snapshot.StartedAt, snapshot.FinishedAt));
        sb.AppendLine(AppLocalization.T("Performance.Report.State", Outcome(snapshot.Outcome), snapshot.ElapsedMs / 1000d, snapshot.Options.DurationSeconds, snapshot.Options.IntervalSeconds));
        sb.AppendLine(AppLocalization.T("Performance.Report.Counts", snapshot.Samples.Count, snapshot.MissedSlots, PerformanceStatistics.Completeness(snapshot)));
        foreach (var metric in Enum.GetValues<SessionMetric>())
        {
            var s = PerformanceStatistics.For(snapshot, metric);
            sb.AppendLine(AppLocalization.T("Performance.Report.Stats", Name(metric), s.Valid, s.Total, F(s.Minimum), F(s.Median), F(s.P95), F(s.Maximum), PerformanceStatistics.Threshold(metric), s.HighCount));
        }
        foreach (var marker in snapshot.Markers)
            sb.AppendLine(AppLocalization.T("Performance.Report.Marker", marker.OffsetMs / 1000d, marker.Note));
        foreach (var warning in snapshot.Warnings.Concat(snapshot.Samples.SelectMany(x => x.Reading.Warnings)).Distinct(StringComparer.Ordinal))
            sb.AppendLine(AppLocalization.T("Performance.Report.Warning", warning));
        sb.AppendLine(Boundary);
        return sb.ToString();
    }

    public static string Html(PerformanceSessionSnapshot snapshot)
    {
        var language = AppLocalization.Language;
        var sb = new StringBuilder("<!doctype html><html lang='").Append(language).Append("'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>")
            .Append(H(AppLocalization.T("Performance.Report.Title")))
            .Append("</title><style>body{font:15px/1.5 'Segoe UI',Arial,sans-serif;margin:24px;color:#15344f;background:#f4f7fa}main{max-width:1200px;margin:auto}section{background:white;padding:20px;border:1px solid #dce5ed;border-radius:12px;margin:16px 0}pre{white-space:pre-wrap;overflow-wrap:anywhere}table{width:100%;border-collapse:collapse;font-size:13px}td,th{padding:7px;border-bottom:1px solid #dce5ed;text-align:left;overflow-wrap:anywhere}svg{width:100%;height:auto}.table{overflow:auto}</style><main><h1>G PC Health Check</h1><h2>")
            .Append(H(AppLocalization.T("Performance.Report.Heading"))).Append("</h2><section><pre>");
        sb.Append(H(Summary(snapshot))).Append("</pre></section>");
        foreach (var metric in Enum.GetValues<SessionMetric>())
            sb.Append("<section><h2>").Append(H(Name(metric))).Append("</h2>").Append(Svg(snapshot, metric)).Append("</section>");
        sb.Append("<section class='table'><h2>").Append(H(AppLocalization.T("Performance.Report.AllMeasurements"))).Append("</h2><table><tr><th>")
            .Append(H(AppLocalization.T("Performance.Report.Column.Offset"))).Append("</th><th>")
            .Append(H(AppLocalization.T("Performance.Report.Column.Time"))).Append("</th><th>CPU, %</th><th>RAM, %</th><th>")
            .Append(H(AppLocalization.T("Performance.Report.Column.Busy"))).Append("</th><th>")
            .Append(H(AppLocalization.T("Performance.Report.Column.Queue"))).Append("</th><th>")
            .Append(H(AppLocalization.T("Performance.Report.Column.Collection"))).Append("</th><th>")
            .Append(H(AppLocalization.T("Performance.Report.Column.Warnings"))).Append("</th></tr>");
        foreach (var sample in snapshot.Samples)
        {
            sb.Append("<tr><td>").Append(H((sample.OffsetMs / 1000d).ToString("0.000", AppLocalization.Culture))).Append("</td><td>").Append(H(sample.CollectedAt.ToString("O"))).Append("</td>");
            foreach (var metric in Enum.GetValues<SessionMetric>()) sb.Append("<td>").Append(H(F(PerformanceValues.Value(sample.Reading, metric)))).Append("</td>");
            sb.Append("<td>").Append(sample.CollectionMs).Append("</td><td>").Append(H(string.Join("; ", sample.Reading.Warnings))).Append("</td></tr>");
        }
        return sb.Append("</table></section></main></html>").ToString();
    }

    private static string Svg(PerformanceSessionSnapshot snapshot, SessionMetric metric)
    {
        var segments = PerformanceStatistics.Segments(snapshot, metric);
        var xmax = Math.Max(1000d, Math.Max(snapshot.ElapsedMs, snapshot.Samples.Select(x => x.OffsetMs).DefaultIfEmpty(0).Max()));
        var ymax = metric == SessionMetric.DiskQueue ? Math.Max(1, segments.SelectMany(x => x).Select(x => x.Value).DefaultIfEmpty(1).Max()) : 100;
        double X(long ms) => 58 + ms / xmax * 900;
        double Y(double v) => 210 - v / ymax * 170;
        var sb = new StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 1000 250' role='img' aria-label='").Append(H(Name(metric))).Append("'><rect width='1000' height='250' fill='white'/><path d='M58 35V210H958' stroke='#687a8b' fill='none'/>");
        for (var i = 0; i <= 4; i++)
        {
            var y = 210 - i * 42.5;
            sb.Append($"<path d='M58 {N(y)}H958' stroke='#e5ebf0'/><text x='4' y='{N(y + 4)}' font-size='12'>{N(ymax * i / 4)}</text>");
        }
        foreach (var marker in snapshot.Markers)
            sb.Append($"<path d='M{N(X(marker.OffsetMs))} 35V210' stroke='#a56a00' stroke-dasharray='4 4'><title>{H(marker.Note)}</title></path>");
        foreach (var segment in segments)
        {
            if (segment.Count > 1) sb.Append("<polyline fill='none' stroke='#2386c0' stroke-width='2' points='").Append(string.Join(" ", segment.Select(p => N(X(p.OffsetMs)) + "," + N(Y(p.Value))))).Append("'/>");
            foreach (var p in segment)
                sb.Append($"<circle cx='{N(X(p.OffsetMs))}' cy='{N(Y(p.Value))}' r='2.5' fill='#2386c0'><title>{H(AppLocalization.T("Performance.Unit.Seconds", N(p.OffsetMs / 1000d)))}: {N(p.Value)}</title></circle>");
        }
        if (segments.Count == 0) sb.Append("<text x='90' y='100' font-size='16'>").Append(H(AppLocalization.T("Performance.Report.NoMeasurements"))).Append("</text>");
        sb.Append("<text x='58' y='235' font-size='12'>").Append(H(AppLocalization.T("Performance.Unit.Seconds", "0"))).Append("</text><text x='890' y='235' font-size='12'>")
            .Append(H(AppLocalization.T("Performance.Unit.Seconds", N(xmax / 1000)))).Append("</text></svg>");
        return sb.ToString();
    }

    public static string Name(SessionMetric metric) => metric switch
    {
        SessionMetric.Cpu => "CPU, %",
        SessionMetric.Memory => AppLocalization.T("Performance.Metric.Memory"),
        SessionMetric.DiskBusy => AppLocalization.T("Performance.Metric.DiskBusy"),
        SessionMetric.DiskQueue => AppLocalization.T("Performance.Metric.DiskQueue"),
        _ => metric.ToString()
    };

    public static string Outcome(string outcome) => outcome switch
    {
        "Completed" => AppLocalization.T("Performance.Outcome.Completed"),
        "Stopped" => AppLocalization.T("Performance.Outcome.Stopped"),
        "Running" => AppLocalization.T("Performance.Outcome.Running"),
        "Failed" => AppLocalization.T("Performance.Outcome.Failed"),
        _ => AppLocalization.T("Performance.Outcome.NotStarted")
    };

    public static string F(double? value) => value is double v && double.IsFinite(v) ? v.ToString("0.##", AppLocalization.Culture) : "—";
    private static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string H(string? text) => WebUtility.HtmlEncode(text ?? "");
}
