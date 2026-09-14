using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace G.PcHealthCheck;

internal sealed record DiagnosticBundleSaveResult(string Folder, string? Zip);

internal static class DiagnosticBundleReport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Summary(DiagnosticBundleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var builder = new StringBuilder(AppLocalization.T("Bundle.Report.Title") + Environment.NewLine);
        builder.AppendLine(AppLocalization.T("Bundle.Report.Version", Value(snapshot.ApplicationVersion)));
        builder.AppendLine(AppLocalization.T("Bundle.Report.Computer", Value(snapshot.ComputerName)));
        builder.AppendLine(AppLocalization.T("Bundle.Report.Mode", ModeText(snapshot.Options.Mode)));
        builder.AppendLine(AppLocalization.T("Bundle.Report.StartEnd", snapshot.StartedAt, snapshot.FinishedAt));
        builder.AppendLine(AppLocalization.T("Bundle.Report.Completeness", OutcomeText(snapshot.Outcome)) + $" ({snapshot.Outcome}). " + AppLocalization.T("Bundle.Report.CompletenessMeaning"));
        if (snapshot.CancellationRequested) builder.AppendLine(AppLocalization.T("Bundle.Report.CancelledMeaning"));
        builder.AppendLine();
        builder.AppendLine(AppLocalization.T("Bundle.Report.Context"));
        builder.AppendLine(ExecutionPolicy.Describe(snapshot.ExecutionContext));
        builder.AppendLine();
        builder.AppendLine(AppLocalization.T("Bundle.Report.Sources"));
        foreach (var source in snapshot.Sources)
        {
            var duration = source.StartedAt != default && source.FinishedAt != default
                ? Math.Max(0, (source.FinishedAt - source.StartedAt).TotalMilliseconds)
                : 0;
            builder.AppendLine("- " + AppLocalization.T("Bundle.Report.SourceLine",
                SourceName(source.Category), StateText(source.State), YesNo(source.Requested), duration, source.Warnings.Count));
            if (!source.Requested) builder.AppendLine("  " + AppLocalization.T("Bundle.Report.Excluded"));
            foreach (var warning in source.Warnings) builder.AppendLine("  ! " + warning);
        }

        builder.AppendLine();
        builder.AppendLine(AppLocalization.T("Bundle.Report.Findings"));
        if (snapshot.Health.Payload is { } health)
        {
            builder.AppendLine(AppLocalization.T("Bundle.Report.HealthScore",
                health.Assessment.Assessment.Score,
                health.Assessment.Assessment.Status,
                health.Assessment.Assessment.CoveragePercent,
                health.Assessment.Assessment.CoverageStatus));
            var findings = health.Assessment.Assessment.Findings
                .Where(item => item.Severity is "CRIT" or "WARN")
                .OrderBy(item => item.Severity == "CRIT" ? 0 : 1)
                .ThenByDescending(item => item.Penalty)
                .ThenBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToArray();
            if (findings.Length == 0) builder.AppendLine(AppLocalization.T("Bundle.Report.NoCriticalFindings"));
            foreach (var finding in findings)
                builder.AppendLine($"{finding.Severity}: {finding.Category} — {finding.Title}; {finding.Value}. {finding.Recommendation}".TrimEnd());
        }
        else if (!snapshot.Health.Requested)
        {
            builder.AppendLine(AppLocalization.T("Bundle.Report.HealthExcludedSummary"));
        }
        else
        {
            builder.AppendLine(AppLocalization.T("Bundle.Report.HealthUnavailableSummary"));
        }
        builder.AppendLine(AppLocalization.T("Bundle.Report.Privacy"));
        return builder.ToString().TrimEnd();
    }

    public static string ManifestJson(DiagnosticBundleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var manifest = new
        {
            snapshot.SchemaVersion,
            snapshot.ApplicationVersion,
            snapshot.ComputerName,
            Mode = snapshot.Options.Mode.ToString(),
            RequestedCategories = snapshot.Options.Categories.OrderBy(item => item).Select(item => item.ToString()).ToArray(),
            snapshot.StartedAt,
            snapshot.FinishedAt,
            snapshot.Outcome,
            snapshot.CancellationRequested,
            snapshot.ExecutionContext,
            Sources = snapshot.Sources.Select(source => new
            {
                Category = source.Category.ToString(),
                source.Requested,
                source.State,
                source.StartedAt,
                source.FinishedAt,
                source.PayloadAvailable,
                source.Warnings,
                File = source.PayloadAvailable ? PayloadFile(source.Category) : null
            }).ToArray()
        };
        return JsonSerializer.Serialize(manifest, JsonOptions);
    }

    public static string Html(DiagnosticBundleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var builder = new StringBuilder("<!doctype html><html lang='").Append(AppLocalization.Language)
            .Append("'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>");
        builder.Append("<title>").Append(H(AppLocalization.T("Bundle.Report.Title"))).Append("</title><style>");
        builder.Append("body{margin:0;background:#f4f7fa;color:#172432;font:14px/1.5 'Segoe UI',Arial,sans-serif}main{max-width:1350px;margin:auto;padding:24px}");
        builder.Append("section{background:#fff;border:1px solid #dce5ed;border-radius:12px;padding:20px;margin:0 0 18px}h1,h2{color:#15344f}pre{white-space:pre-wrap;overflow-wrap:anywhere;font:inherit}");
        builder.Append("table{border-collapse:collapse;width:100%;font-size:13px}th,td{padding:8px;border-bottom:1px solid #dce5ed;text-align:left;vertical-align:top;overflow-wrap:anywhere}th{background:#f0f5f8}.table{overflow-x:auto}.muted{color:#607285}.warn{font-weight:600}.files a{display:inline-block;margin-right:12px}@media(max-width:650px){main{padding:8px}}");
        builder.Append("</style></head><body><main><h1>G PC Health Check</h1><h2>").Append(H(AppLocalization.T("Bundle.Report.Heading"))).Append("</h2>");
        builder.Append("<section><pre>").Append(H(Summary(snapshot))).Append("</pre></section>");
        AppendSourceMatrix(builder, snapshot);
        AppendFindings(builder, snapshot);
        AppendEvents(builder, snapshot);
        AppendProcesses(builder, snapshot);
        AppendEndpoints(builder, snapshot);
        AppendStorage(builder, snapshot);
        AppendPerformance(builder, snapshot);
        AppendFiles(builder, snapshot);
        builder.Append("<p class='muted'>").Append(H(AppLocalization.T("Bundle.Report.ExternalReview"))).Append("</p></main></body></html>");
        return builder.ToString();
    }

    public static DiagnosticBundleSaveResult Save(DiagnosticBundleSnapshot snapshot, string parent, bool createZip)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(parent) || !Path.IsPathFullyQualified(parent))
            throw new ArgumentException(AppLocalization.T("Bundle.Report.SavePath"), nameof(parent));

        Directory.CreateDirectory(parent);
        var name = $"G-PC-DiagnosticBundle_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}";
        var folder = Path.Combine(parent, name);
        Directory.CreateDirectory(folder);

        WriteNew(Path.Combine(folder, "summary.html"), Html(snapshot));
        WriteNew(Path.Combine(folder, "manifest.json"), ManifestJson(snapshot));
        WritePayloads(snapshot, folder);

        string? zip = null;
        if (createZip)
        {
            zip = Path.Combine(parent, name + ".zip");
            try
            {
                ZipFile.CreateFromDirectory(folder, zip, CompressionLevel.Optimal, includeBaseDirectory: false);
            }
            catch
            {
                try { if (File.Exists(zip)) File.Delete(zip); } catch { }
                throw;
            }
        }
        return new DiagnosticBundleSaveResult(folder, zip);
    }

    private static void AppendSourceMatrix(StringBuilder builder, DiagnosticBundleSnapshot snapshot)
    {
        builder.Append("<section><h2>").Append(H(AppLocalization.T("Bundle.Report.SourceMatrixHeading"))).Append("</h2><p class='muted'>")
            .Append(H(AppLocalization.T("Bundle.Report.SourceMatrixMeaning"))).Append("</p><div class='table'><table><tr><th>")
            .Append(H(AppLocalization.T("Bundle.Report.Column.Source"))).Append("</th><th>")
            .Append(H(AppLocalization.T("Bundle.Report.Column.Requested"))).Append("</th><th>")
            .Append(H(AppLocalization.T("Bundle.Report.Column.State"))).Append("</th><th>")
            .Append(H(AppLocalization.T("Bundle.Report.Column.Started"))).Append("</th><th>")
            .Append(H(AppLocalization.T("Bundle.Report.Column.Finished"))).Append("</th><th>")
            .Append(H(AppLocalization.T("Bundle.Report.Column.Warnings"))).Append("</th></tr>");
        foreach (var source in snapshot.Sources)
        {
            builder.Append("<tr><td>").Append(H(SourceName(source.Category))).Append("</td><td>").Append(H(YesNo(source.Requested)))
                .Append("</td><td>").Append(H(StateText(source.State))).Append("</td><td>").Append(H(Time(source.StartedAt)))
                .Append("</td><td>").Append(H(Time(source.FinishedAt))).Append("</td><td>").Append(H(string.Join("; ", source.Warnings))).Append("</td></tr>");
        }
        builder.Append("</table></div></section>");
    }

    private static void AppendFindings(StringBuilder builder, DiagnosticBundleSnapshot snapshot)
    {
        builder.Append("<section><h2>").Append(H(AppLocalization.T("Bundle.Report.HealthFindingsHeading"))).Append("</h2>");
        if (!snapshot.Health.Requested)
        {
            builder.Append("<p>").Append(H(AppLocalization.T("Bundle.Report.HealthExcludedSummary"))).Append("</p></section>");
            return;
        }
        if (snapshot.Health.Payload is not { } health)
        {
            builder.Append("<p class='warn'>").Append(H(SourceName(DiagnosticBundleCategory.Health) + ": " + StateText(snapshot.Health.State) + ". " + AppLocalization.T("Bundle.Report.HealthUnavailableMeaning"))).Append("</p></section>");
            return;
        }

        builder.Append("<p>").Append(H(AppLocalization.T("Bundle.Report.HealthScore",
            health.Assessment.Assessment.Score,
            health.Assessment.Assessment.Status,
            health.Assessment.Assessment.CoveragePercent,
            health.Assessment.Assessment.CoverageStatus))).Append("</p>");
        builder.Append("<div class='table'><table><tr><th>").Append(H(AppLocalization.T("Bundle.Report.Column.Severity"))).Append("</th><th>")
            .Append(H(AppLocalization.T("Bundle.Report.Column.Category"))).Append("</th><th>")
            .Append(H(AppLocalization.T("Bundle.Report.Column.Finding"))).Append("</th><th>")
            .Append(H(AppLocalization.T("Bundle.Report.Column.Value"))).Append("</th><th>")
            .Append(H(AppLocalization.T("Bundle.Report.Column.Recommendation"))).Append("</th></tr>");
        foreach (var finding in health.Assessment.Assessment.Findings
            .Where(item => item.Severity is "CRIT" or "WARN")
            .OrderBy(item => item.Severity == "CRIT" ? 0 : 1)
            .ThenByDescending(item => item.Penalty)
            .ThenBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(12))
        {
            Row(builder, finding.Severity, finding.Category, finding.Title, finding.Value, finding.Recommendation);
        }
        builder.Append("</table></div></section>");
    }

    private static void AppendEvents(StringBuilder builder, DiagnosticBundleSnapshot snapshot)
    {
        if (!snapshot.Events.Requested) return;
        builder.Append("<section><h2>").Append(H(AppLocalization.T("Bundle.Report.Events"))).Append("</h2>");
        if (snapshot.Events.Payload is not { } events)
        {
            builder.Append("<p class='warn'>").Append(H(AppLocalization.T("Bundle.Report.EventsUnavailable", StateText(snapshot.Events.State)))).Append("</p></section>");
            return;
        }
        builder.Append("<p class='muted'>").Append(H(AppLocalization.T("Bundle.Report.EventsNote"))).Append("</p>");
        var highlights = DiagnosticBundleCore.EventHighlights(events);
        if (highlights.Count == 0) builder.Append("<p>").Append(H(AppLocalization.T("Bundle.Report.EventsNone"))).Append("</p>");
        else
        {
            builder.Append("<div class='table'><table><tr><th>").Append(H(AppLocalization.T("Bundle.Report.Column.Level"))).Append("</th><th>")
                .Append(H(AppLocalization.T("Bundle.Report.Column.Log"))).Append("</th><th>")
                .Append(H(AppLocalization.T("Bundle.Report.Column.Provider"))).Append("</th><th>Event ID</th><th>")
                .Append(H(AppLocalization.T("Bundle.Report.Column.Count"))).Append("</th><th>")
                .Append(H(AppLocalization.T("Bundle.Report.Column.Latest"))).Append("</th></tr>");
            foreach (var item in highlights) Row(builder, EventLevelText(item.Level), item.Log, item.Provider, item.EventId, item.Count, item.Latest?.ToString("O") ?? "—");
            builder.Append("</table></div>");
        }
        builder.Append("</section>");
    }

    private static void AppendProcesses(StringBuilder builder, DiagnosticBundleSnapshot snapshot)
    {
        if (!snapshot.Processes.Requested) return;
        builder.Append("<section><h2>").Append(H(AppLocalization.T("Bundle.Report.ProcessesHeading"))).Append("</h2>");
        if (snapshot.Processes.Payload is not { } processes)
        {
            builder.Append("<p class='warn'>").Append(H(SourceName(DiagnosticBundleCategory.Processes) + ": " + StateText(snapshot.Processes.State) + ".")).Append("</p></section>");
            return;
        }
        builder.Append("<p class='muted'>").Append(H(AppLocalization.T("Bundle.Report.ProcessesWorkingNote"))).Append("</p>");
        builder.Append("<div class='table'><table><tr><th>").Append(H(AppLocalization.T("Bundle.Report.Column.Process"))).Append("</th><th>PID</th><th>")
            .Append(H(AppLocalization.T("Bundle.Report.Column.WorkingSet"))).Append("</th><th>")
            .Append(H(AppLocalization.T("Bundle.Report.Column.Path"))).Append("</th><th>")
            .Append(H(AppLocalization.T("Bundle.Report.Column.Command"))).Append("</th></tr>");
        foreach (var process in DiagnosticBundleCore.ProcessWorkingSetHighlights(processes))
            Row(builder, process.Name, process.Pid, Bytes(process.WorkingSetBytes), process.Executable, process.CommandLine);
        builder.Append("</table></div></section>");
    }

    private static void AppendEndpoints(StringBuilder builder, DiagnosticBundleSnapshot snapshot)
    {
        if (!snapshot.Endpoints.Requested) return;
        builder.Append("<section><h2>").Append(H(AppLocalization.T("Bundle.Report.EndpointsHeading"))).Append("</h2>");
        if (snapshot.Endpoints.Payload is not { } endpoints)
        {
            builder.Append("<p class='warn'>").Append(H(AppLocalization.T("Bundle.Report.EndpointsUnavailable", StateText(snapshot.Endpoints.State)))).Append("</p></section>");
            return;
        }
        var counts = DiagnosticBundleCore.EndpointCounts(endpoints);
        builder.Append("<p>").Append(H(AppLocalization.T("Bundle.Report.EndpointsCounts", counts.Total, counts.TcpListeners, counts.TcpEstablished, counts.UdpBindings))).Append("</p>");
        builder.Append("<p class='muted'>").Append(H(AppLocalization.T("Bundle.Report.EndpointsCaveat"))).Append("</p></section>");
    }

    private static void AppendStorage(StringBuilder builder, DiagnosticBundleSnapshot snapshot)
    {
        if (!snapshot.Storage.Requested) return;
        builder.Append("<section><h2>").Append(H(AppLocalization.T("Bundle.Report.Storage"))).Append("</h2>");
        if (snapshot.Storage.Payload is not { } storage)
        {
            builder.Append("<p class='warn'>").Append(H(AppLocalization.T("Bundle.Report.StorageUnavailable", StateText(snapshot.Storage.State)))).Append("</p></section>");
            return;
        }
        var highlights = DiagnosticBundleCore.StorageHighlights(storage);
        if (highlights.Count == 0) builder.Append("<p>").Append(H(AppLocalization.T("Bundle.Report.StorageNone"))).Append("</p>");
        else
        {
            builder.Append("<ul>");
            foreach (var value in highlights) builder.Append("<li>").Append(H(value)).Append("</li>");
            builder.Append("</ul>");
        }
        builder.Append("<p class='muted'>").Append(H(AppLocalization.T("Bundle.Report.StorageCaveat"))).Append("</p></section>");
    }

    private static void AppendPerformance(StringBuilder builder, DiagnosticBundleSnapshot snapshot)
    {
        if (!snapshot.Performance.Requested) return;
        builder.Append("<section><h2>").Append(H(AppLocalization.T("Performance.Report.Heading"))).Append("</h2>");
        if (snapshot.Performance.Payload is not { } performance)
        {
            builder.Append("<p class='warn'>").Append(H(AppLocalization.T("Bundle.Report.PerformanceUnavailable", StateText(snapshot.Performance.State)))).Append("</p></section>");
            return;
        }
        builder.Append("<pre>").Append(H(PerformanceSessionReport.Summary(performance))).Append("</pre>");
        builder.Append("<p><a href='performance.html'>").Append(H(AppLocalization.T("Bundle.Report.PerformanceCharts"))).Append("</a> · <a href='performance.json'>")
            .Append(H(AppLocalization.T("Bundle.Report.PerformanceJson"))).Append("</a></p>");
        builder.Append("<p class='muted'>").Append(H(AppLocalization.T("Bundle.Report.PerformanceNote"))).Append("</p></section>");
    }

    private static void AppendFiles(StringBuilder builder, DiagnosticBundleSnapshot snapshot)
    {
        builder.Append("<section class='files'><h2>").Append(H(AppLocalization.T("Bundle.Report.Files"))).Append("</h2><a href='manifest.json'>manifest.json</a>");
        foreach (var source in snapshot.Sources.Where(source => source.PayloadAvailable))
            builder.Append("<a href='").Append(PayloadFile(source.Category)).Append("'>").Append(PayloadFile(source.Category)).Append("</a>");
        if (snapshot.Performance.PayloadAvailable) builder.Append("<a href='performance.html'>performance.html</a>");
        builder.Append("<p class='muted'>").Append(H(AppLocalization.T("Bundle.Report.FilesNote"))).Append("</p></section>");
    }

    private static void WritePayloads(DiagnosticBundleSnapshot snapshot, string folder)
    {
        if (snapshot.Health.Payload is { } health) WriteNew(Path.Combine(folder, "health.json"), SourceJson("Health", health));
        if (snapshot.Processes.Payload is { } processes) WriteNew(Path.Combine(folder, "processes.json"), SourceJson("Processes", processes));
        if (snapshot.Endpoints.Payload is { } endpoints) WriteNew(Path.Combine(folder, "endpoints.json"), SourceJson("Endpoints", endpoints));
        if (snapshot.Events.Payload is { } events) WriteNew(Path.Combine(folder, "events.json"), SourceJson("Events", events));
        if (snapshot.Storage.Payload is { } storage) WriteNew(Path.Combine(folder, "storage.json"), SourceJson("Storage", storage));
        if (snapshot.Performance.Payload is { } performance)
        {
            WriteNew(Path.Combine(folder, "performance.json"), PerformanceSessionReport.Json(performance));
            WriteNew(Path.Combine(folder, "performance.html"), PerformanceSessionReport.Html(performance));
        }
    }

    private static string SourceJson<T>(string category, T snapshot) where T : class
        => JsonSerializer.Serialize(new { SchemaVersion = 1, Category = category, Snapshot = snapshot }, JsonOptions);

    private static void WriteNew(string path, string content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false, true));
        writer.Write(content);
    }

    private static void Row(StringBuilder builder, params object?[] values)
    {
        builder.Append("<tr>");
        foreach (var value in values)
            builder.Append("<td>").Append(H(value is null ? "—" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "—")).Append("</td>");
        builder.Append("</tr>");
    }

    private static string PayloadFile(DiagnosticBundleCategory category) => category switch
    {
        DiagnosticBundleCategory.Health => "health.json",
        DiagnosticBundleCategory.Processes => "processes.json",
        DiagnosticBundleCategory.Endpoints => "endpoints.json",
        DiagnosticBundleCategory.Events => "events.json",
        DiagnosticBundleCategory.Storage => "storage.json",
        DiagnosticBundleCategory.Performance => "performance.json",
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };

    private static string SourceName(DiagnosticBundleCategory category) => AppLocalization.T(category switch
    {
        DiagnosticBundleCategory.Health => "Bundle.Source.Health",
        DiagnosticBundleCategory.Processes => "Bundle.Source.Processes",
        DiagnosticBundleCategory.Endpoints => "Bundle.Source.Endpoints",
        DiagnosticBundleCategory.Events => "Bundle.Source.Events",
        DiagnosticBundleCategory.Storage => "Bundle.Source.Storage",
        DiagnosticBundleCategory.Performance => "Bundle.Source.Performance",
        _ => "Bundle.Source.Health"
    });

    private static string StateText(string state) => state switch
    {
        "Complete" => AppLocalization.T("Bundle.State.Complete"),
        "Partial" => AppLocalization.T("Bundle.State.Partial"),
        "Unavailable" => AppLocalization.T("Bundle.State.Unavailable"),
        "Cancelled" => AppLocalization.T("Bundle.State.Cancelled"),
        "NotRequested" => AppLocalization.T("Bundle.State.NotRequested"),
        "Pending" => AppLocalization.T("Bundle.State.Pending"),
        "Running" => AppLocalization.T("Bundle.State.Running"),
        _ => AppLocalization.T("Bundle.State.Unknown", state)
    };

    private static string OutcomeText(string outcome) => outcome switch
    {
        "Complete" => AppLocalization.T("Bundle.State.Complete"),
        "Partial" => AppLocalization.T("Bundle.State.Partial"),
        "Unavailable" => AppLocalization.T("Bundle.State.Unavailable"),
        "Cancelled" or "Stopped" => AppLocalization.T("Bundle.State.Cancelled"),
        "Running" => AppLocalization.T("Bundle.State.Running"),
        _ => AppLocalization.T("Bundle.State.Unknown", outcome)
    };

    private static string ModeText(DiagnosticBundleMode mode) => AppLocalization.T(mode == DiagnosticBundleMode.Extended ? "Bundle.Mode.Extended" : "Bundle.Mode.Quick");
    private static string EventLevelText(int? level) => level switch { 1 => "Critical", 2 => "Error", 3 => "Warning", _ => "—" };
    private static string YesNo(bool value) => AppLocalization.T(value ? "Bundle.Report.Yes" : "Bundle.Report.No");
    private static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? AppLocalization.T("Bundle.Report.NotAvailable") : value;
    private static string Time(DateTimeOffset value) => value == default ? "—" : value.ToString("O");
    private static string Bytes(ulong? value) => value is ulong bytes
        ? $"{bytes / 1048576d:0.##} MiB ({AppLocalization.T("Bundle.Report.Bytes", bytes)})"
        : "—";
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? "");
}
