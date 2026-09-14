using System.Text.Json;
using System.Text.RegularExpressions;

namespace G.PcHealthCheck;

internal static class DiagnosticBundleLocalizationSelfTest
{
    private static readonly Regex Cyrillic = new("[А-Яа-яЁё]", RegexOptions.CultureInvariant);

    public static int Run()
    {
        var failures = new List<string>();
        var original = AppLocalization.Language;
        try
        {
            AppLocalization.SetLanguage("en");
            const string rawWarning = "RAW-ПРЕДУПРЕЖДЕНИЕ";
            var snapshot = new DiagnosticBundleSnapshot
            {
                ApplicationVersion = "0.16.0-test",
                ComputerName = "SYNTHETIC-PC",
                Options = new DiagnosticBundleOptions(
                    DiagnosticBundleMode.Quick,
                    new HashSet<DiagnosticBundleCategory> { DiagnosticBundleCategory.Health }),
                StartedAt = DateTimeOffset.Parse("2026-09-14T10:00:00Z"),
                FinishedAt = DateTimeOffset.Parse("2026-09-14T10:00:01Z"),
                Outcome = "Partial",
                ExecutionContext = new ExecutionContextInfo
                {
                    ProcessAccount = "SYNTHETIC\\user",
                    ProcessSid = "S-1-5-21-SYNTHETIC",
                    SessionId = 1,
                    SessionAccount = "SYNTHETIC\\user",
                    SessionSid = "S-1-5-21-SYNTHETIC"
                }
            };
            snapshot.Health.Requested = true;
            snapshot.Health.State = "Unavailable";
            snapshot.Health.StartedAt = snapshot.StartedAt;
            snapshot.Health.FinishedAt = snapshot.FinishedAt;
            snapshot.Health.Warnings.Add(rawWarning);

            Check("English Diagnostic Bundle summary localizes framing and preserves raw evidence", () =>
            {
                var summary = DiagnosticBundleReport.Summary(snapshot);
                var framing = summary.Replace(rawWarning, "", StringComparison.Ordinal);
                Require(!Cyrillic.IsMatch(framing), "English diagnostic bundle summary still contains Russian framing: " + OneLine(framing));
                Require(summary.Contains(rawWarning, StringComparison.Ordinal), "Raw diagnostic warning was translated or removed.");
            });

            Check("English Diagnostic Bundle HTML localizes framing and preserves raw evidence", () =>
            {
                var html = DiagnosticBundleReport.Html(snapshot);
                var framing = html.Replace(rawWarning, "", StringComparison.Ordinal);
                Require(!Cyrillic.IsMatch(framing), "English diagnostic bundle HTML still contains Russian framing.");
                Require(html.Contains("lang='en'", StringComparison.OrdinalIgnoreCase) || html.Contains("lang=\"en\"", StringComparison.OrdinalIgnoreCase),
                    "English diagnostic bundle HTML language metadata is not en.");
                Require(html.Contains(rawWarning, StringComparison.Ordinal), "Raw diagnostic warning was translated or removed from HTML.");
            });

            Check("Diagnostic Bundle manifest keeps stable schema IDs and raw evidence", () =>
            {
                var json = DiagnosticBundleReport.ManifestJson(snapshot);
                using var parsed = JsonDocument.Parse(json);
                var root = parsed.RootElement;
                Require(root.GetProperty("Mode").GetString() == "Quick", "Stable bundle mode ID changed with language.");
                Require(root.GetProperty("Outcome").GetString() == "Partial", "Stable bundle outcome ID changed with language.");
                var health = root.GetProperty("Sources").EnumerateArray().Single(x => x.GetProperty("Category").GetString() == "Health");
                Require(health.GetProperty("State").GetString() == "Unavailable", "Stable source state ID changed with language.");
                Require(health.GetProperty("Warnings")[0].GetString() == rawWarning, "Raw diagnostic warning changed in manifest.");
            });

            Check("Complete performance source stays Complete in English mode", () =>
            {
                var started = DateTimeOffset.Parse("2026-09-14T10:00:00Z");
                var performance = new PerformanceSessionSnapshot
                {
                    Options = new PerformanceSessionOptions(30, 1),
                    Outcome = "Completed",
                    StartedAt = started,
                    FinishedAt = started.AddSeconds(30),
                    ElapsedMs = 30000,
                    Samples = Enumerable.Range(1, 30)
                        .Select(i => new PerformanceSample(i * 1000L, 1, started.AddSeconds(i), new PerformanceReading(10, 40, 5, 0, [])))
                        .ToList()
                };
                var options = new DiagnosticBundleOptions(
                    DiagnosticBundleMode.Extended,
                    new HashSet<DiagnosticBundleCategory> { DiagnosticBundleCategory.Performance },
                    PerformanceSeconds: 30,
                    PerformanceIntervalSeconds: 1);
                var result = new DiagnosticBundleService().CollectAsync(options, new PerformanceOnlyCollector(performance), null, null, null, default)
                    .GetAwaiter().GetResult();
                Require(result.Performance.State == "Complete",
                    "Localized performance completeness changed stable bundle state: " + result.Performance.State);
            });
        }
        finally
        {
            AppLocalization.SetLanguage(original);
        }

        Console.WriteLine($"Diagnostic Bundle localization self-test: {4 - failures.Count}/4 passed.");
        foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 210;

        void Check(string name, Action action)
        {
            try { action(); }
            catch (Exception ex) { failures.Add(name + ": " + ex.GetBaseException().Message); }
        }
    }

    private sealed class PerformanceOnlyCollector(PerformanceSessionSnapshot performance) : IDiagnosticBundleCollector
    {
        public Task<DiagnosticBundleHealthPayload> CollectHealthAsync(IProgress<string>? progress, CancellationToken ct)
            => throw new InvalidOperationException("Health must not be requested.");
        public ProcessReviewSnapshot CollectProcesses(CancellationToken ct)
            => throw new InvalidOperationException("Processes must not be requested.");
        public EndpointSnapshot CollectEndpoints(ExecutionContextInfo? context, IProgress<string>? progress, CancellationToken ct)
            => throw new InvalidOperationException("Endpoints must not be requested.");
        public IncidentSnapshot CollectEvents(IncidentWindow window, CancellationToken ct)
            => throw new InvalidOperationException("Events must not be requested.");
        public DiskDetailsSnapshot CollectStorage(CancellationToken ct)
            => throw new InvalidOperationException("Storage must not be requested.");
        public Task<PerformanceSessionSnapshot> CollectPerformanceAsync(PerformanceSessionOptions options, IProgress<PerformanceSample>? progress, CancellationToken ct)
            => Task.FromResult(performance);
    }

    private static string OneLine(string text)
        => Regex.Replace(text, "\\s+", " ").Trim() is var one && one.Length > 220 ? one[..220] + "…" : one;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
