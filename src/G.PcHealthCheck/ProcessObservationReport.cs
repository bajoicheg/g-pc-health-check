using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class ProcessObservationReport
{
    public static string Boundary => AppLocalization.T("ProcessObservation.Report.Boundary");
    public static string Name(ProcessMetric metric) => metric switch
    {
        ProcessMetric.Cpu => AppLocalization.T("ProcessObservation.Metric.Cpu"),
        ProcessMetric.WorkingSet => AppLocalization.T("ProcessObservation.Metric.Working"),
        ProcessMetric.PrivateCommit => AppLocalization.T("ProcessObservation.Metric.Private"),
        ProcessMetric.ReadRate => AppLocalization.T("ProcessObservation.Metric.Read"),
        ProcessMetric.WriteRate => AppLocalization.T("ProcessObservation.Metric.Write"),
        _ => throw new ArgumentOutOfRangeException(nameof(metric))
    };
    public static string Statistics(ProcessObservationSnapshot snapshot, ProcessMetric metric)
    {
        var values = snapshot.Samples.Select(s => ProcessObservationCore.Value(s.Reading, metric)).Where(x => x.HasValue).Select(x => x!.Value).Order().ToArray();
        if (values.Length == 0) return AppLocalization.T("ProcessObservation.Report.StatsNone", Name(metric), snapshot.Samples.Count);
        var n = values.Length; var median = n % 2 == 1 ? values[n / 2] : values[n / 2 - 1] / 2 + values[n / 2] / 2;
        return AppLocalization.T("ProcessObservation.Report.StatsLine", Name(metric), F(values[0]), F(median), F(values[(int)Math.Ceiling(n * .95) - 1]), F(values[^1]), n, snapshot.Samples.Count);
    }
    public static string Summary(ProcessObservationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot); var s = new StringBuilder(AppLocalization.T("ProcessObservation.Report.Title") + Environment.NewLine);
        s.AppendLine(AppLocalization.T("ProcessObservation.Report.Target", snapshot.ComputerName, snapshot.Target.Name, snapshot.Target.Pid, snapshot.Target.CreatedAt));
        s.AppendLine(AppLocalization.T("ProcessObservation.Report.StartEnd", snapshot.System.StartedAt, snapshot.System.FinishedAt, PerformanceSessionReport.Outcome(snapshot.System.Outcome)));
        s.AppendLine(AppLocalization.T("ProcessObservation.Report.Plan", snapshot.System.Options.DurationSeconds, snapshot.System.Options.IntervalSeconds, snapshot.System.ElapsedMs / 1000d, snapshot.Samples.Count, snapshot.System.MissedSlots));
        var last = snapshot.Samples.LastOrDefault();
        if (last is not null) s.AppendLine(AppLocalization.T("ProcessObservation.Report.LastState", ProcessObservationCore.StateText(last.Process.Counters.State)));
        foreach (var metric in Enum.GetValues<ProcessMetric>()) s.AppendLine(Statistics(snapshot, metric));
        s.AppendLine(AppLocalization.T("ProcessObservation.Report.StatsMeaning"));
        s.AppendLine(AppLocalization.T("ProcessObservation.Report.Computer", PerformanceStatistics.Completeness(snapshot.System)));
        foreach (var marker in snapshot.System.Markers) s.AppendLine(AppLocalization.T("ProcessObservation.Report.Marker", marker.OffsetMs / 1000d, marker.Note));
        foreach (var warning in snapshot.System.Warnings.Concat(snapshot.Samples.SelectMany(x => x.Reading.Warnings)).Distinct(StringComparer.Ordinal)) s.AppendLine("! " + warning);
        s.AppendLine(ExecutionPolicy.Describe(snapshot.ExecutionContext));
        s.AppendLine(Boundary);
        s.AppendLine(AppLocalization.T("ProcessObservation.Report.Privacy"));
        return s.ToString();
    }
    public static string Json(ProcessObservationSnapshot snapshot)
        => JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
    public static string Html(ProcessObservationSnapshot snapshot)
    {
        var s = new StringBuilder("<!doctype html><html lang='").Append(AppLocalization.Language)
            .Append("'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>")
            .Append(H(AppLocalization.T("ProcessObservation.Report.Title")))
            .Append("</title><style>body{font:14px/1.5 'Segoe UI',Arial,sans-serif;margin:24px;color:#18364c;background:#f4f7fa}main{max-width:1250px;margin:auto}section{background:white;border:1px solid #dce5ed;border-radius:10px;padding:18px;margin:15px 0}pre{white-space:pre-wrap;overflow-wrap:anywhere}table{border-collapse:collapse;width:100%;font-size:12px}td,th{padding:7px;border:1px solid #dce5ed;text-align:left;vertical-align:top}svg{width:100%;height:auto}.scroll{overflow:auto}</style></head><body><main><h1>")
            .Append(H(AppLocalization.T("ProcessObservation.Report.Heading"))).Append("</h1><section><pre>");
        s.Append(H(Summary(snapshot))).Append("</pre></section>");
        foreach (var metric in Enum.GetValues<ProcessMetric>())
            s.Append("<section><h2>").Append(H(Name(metric))).Append("</h2>").Append(Svg(snapshot, ProcessObservationCore.Segments(snapshot, metric), Name(metric), metric == ProcessMetric.Cpu)).Append("</section>");
        s.Append("<section><h2>").Append(H(AppLocalization.T("ProcessObservation.Report.HostContext"))).Append("</h2><pre>").Append(H(PerformanceSessionReport.Summary(snapshot.System))).Append("</pre></section>");
        foreach (var metric in Enum.GetValues<SessionMetric>())
            s.Append("<section><h2>").Append(H(PerformanceSessionReport.Name(metric))).Append("</h2>").Append(Svg(snapshot, PerformanceStatistics.Segments(snapshot.System, metric), PerformanceSessionReport.Name(metric), metric != SessionMetric.DiskQueue)).Append("</section>");
        s.Append("<section class='scroll'><h2>").Append(H(AppLocalization.T("ProcessObservation.Report.Pairs"))).Append("</h2><table><tr><th>")
            .Append(H(AppLocalization.T("ProcessObservation.Report.Column.ProcessTime"))).Append("</th><th>")
            .Append(H(AppLocalization.T("ProcessObservation.Report.Column.State"))).Append("</th><th>")
            .Append(H(AppLocalization.T("ProcessObservation.Report.Column.Exe"))).Append("</th><th>CPU, %</th><th>")
            .Append(H(AppLocalization.T("ProcessObservation.Report.Column.Working"))).Append("</th><th>")
            .Append(H(AppLocalization.T("ProcessObservation.Metric.Private"))).Append("</th><th>")
            .Append(H(AppLocalization.T("ProcessObservation.Report.Column.Read"))).Append("</th><th>")
            .Append(H(AppLocalization.T("ProcessObservation.Report.Column.Write"))).Append("</th><th>")
            .Append(H(AppLocalization.T("ProcessObservation.Report.Column.Interval"))).Append("</th><th>")
            .Append(H(AppLocalization.T("ProcessObservation.Report.Column.HostTime"))).Append("</th><th>CPU, %</th><th>RAM, %</th><th>")
            .Append(H(AppLocalization.T("ProcessObservation.Report.Column.Disks"))).Append("</th><th>")
            .Append(H(AppLocalization.T("ProcessObservation.Report.Column.Queue"))).Append("</th><th>")
            .Append(H(AppLocalization.T("ProcessObservation.Report.Column.Collection"))).Append("</th><th>")
            .Append(H(AppLocalization.T("ProcessObservation.Report.Column.Warnings"))).Append("</th></tr>");
        foreach (var pair in snapshot.Samples)
        {
            s.Append("<tr><td>").Append(H($"{pair.Process.OffsetMs / 1000d:0.000} / {pair.Process.CollectedAt:O}")).Append("</td><td>").Append(H(ProcessObservationCore.StateText(pair.Process.Counters.State)))
                .Append("</td><td>").Append(H(pair.Process.Counters.ImagePath)).Append("</td>");
            foreach (var metric in Enum.GetValues<ProcessMetric>()) s.Append("<td>").Append(H(F(ProcessObservationCore.Value(pair.Reading, metric)))).Append("</td>");
            s.Append("<td>").Append(H(pair.Reading.IntervalMs?.ToString() ?? "—")).Append("</td><td>").Append(H($"{pair.System.OffsetMs / 1000d:0.000} / {pair.System.CollectedAt:O}")).Append("</td>");
            foreach (var metric in Enum.GetValues<SessionMetric>()) s.Append("<td>").Append(H(F(PerformanceValues.Value(pair.System.Reading, metric)))).Append("</td>");
            s.Append("<td>").Append(pair.System.CollectionMs).Append("</td><td>").Append(H(string.Join("; ", pair.Reading.Warnings.Concat(pair.System.Reading.Warnings)))).Append("</td></tr>");
        }
        s.Append("</table></section><section><details><summary>").Append(H(AppLocalization.T("ProcessObservation.Report.RawJson"))).Append("</summary><pre>")
            .Append(H(Json(snapshot))).Append("</pre></details></section></main></body></html>");
        return s.ToString();
    }
    public static double EndMs(ProcessObservationSnapshot snapshot)
        => Math.Max(1000d, Math.Max(snapshot.System.ElapsedMs, Math.Max(snapshot.Samples.Select(x => x.Process.OffsetMs).DefaultIfEmpty(0).Max(), snapshot.System.Samples.Select(x => x.OffsetMs).DefaultIfEmpty(0).Max())));
    private static string Svg(ProcessObservationSnapshot snapshot, List<List<PerformancePoint>> segments, string title, bool percentage)
    {
        var xmax = EndMs(snapshot); var ymax = percentage ? 100 : Math.Max(1, segments.SelectMany(x => x).Select(x => x.Value).DefaultIfEmpty(1).Max());
        double X(long ms) => 64 + ms / xmax * 910; double Y(double v) => 205 - v / ymax * 175;
        var s = new StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 1000 250' role='img' aria-label='").Append(H(title)).Append("'><rect width='1000' height='250' fill='white'/>");
        for (var i = 0; i <= 4; i++) s.Append($"<path d='M64 {N(Y(ymax * i / 4))}H974' stroke='#e2e8ee'/><text x='3' y='{N(Y(ymax * i / 4) + 4)}' font-size='12'>{N(ymax * i / 4)}</text>");
        foreach (var marker in snapshot.System.Markers.Where(x => x.OffsetMs >= 0 && x.OffsetMs <= xmax)) s.Append($"<path d='M{N(X(marker.OffsetMs))} 30V205' stroke='#a56a00' stroke-dasharray='4 4'><title>{H(marker.Note)}</title></path>");
        foreach (var segment in segments)
        {
            if (segment.Count > 1) s.Append("<polyline fill='none' stroke='#2386c0' stroke-width='2' points='").Append(string.Join(" ", segment.Select(p => N(X(p.OffsetMs)) + "," + N(Y(p.Value))))).Append("'/>");
            foreach (var point in segment)
                s.Append($"<circle cx='{N(X(point.OffsetMs))}' cy='{N(Y(point.Value))}' r='2.5' fill='#2386c0'><title>{H(AppLocalization.T("ProcessObservation.Unit.Seconds", N(point.OffsetMs / 1000d)))}: {N(point.Value)}</title></circle>");
        }
        if (segments.Count == 0) s.Append("<text x='90' y='90' font-size='16'>").Append(H(AppLocalization.T("ProcessObservation.Report.NoValues"))).Append("</text>");
        return s.Append("<text x='64' y='235' font-size='12'>").Append(H(AppLocalization.T("ProcessObservation.Unit.Seconds", "0")))
            .Append("</text><text x='880' y='235' font-size='12'>").Append(H(AppLocalization.T("ProcessObservation.Unit.Seconds", N(xmax / 1000)))).Append("</text></svg>").ToString();
    }
    public static string Detail(ProcessObservationSample sample)
        => AppLocalization.T("ProcessObservation.Report.Detail",
            sample.Process.OffsetMs / 1000d,
            sample.Process.CollectedAt,
            sample.System.OffsetMs / 1000d,
            sample.System.CollectedAt,
            sample.System.CollectionMs,
            sample.Reading.IntervalMs?.ToString() ?? "—",
            ProcessObservationCore.StateText(sample.Process.Counters.State),
            sample.Process.Counters.ImagePath,
            sample.Process.Counters.CreatedFileTime,
            sample.Process.Counters.LogicalProcessors,
            string.Join("\r\n", sample.Reading.Warnings.Concat(sample.System.Reading.Warnings)),
            Boundary);
    public static string Save(ProcessObservationSnapshot snapshot, string parent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parent); var html = Html(snapshot); var json = Json(snapshot);
        var root = Path.Combine(parent, $"G-PC-Process_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        Write(Path.Combine(root, "observation.json"), json); Write(Path.Combine(root, "report.html"), html); return root;
    }
    private static void Write(string path, string text)
    { using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); using var writer = new StreamWriter(stream, new UTF8Encoding(false, true)); writer.Write(text); }
    private static string F(double? n) => PerformanceSessionReport.F(n);
    private static string H(string? text) => WebUtility.HtmlEncode(text ?? "");
    private static string N(double n) => n.ToString("0.###", CultureInfo.InvariantCulture);
}
