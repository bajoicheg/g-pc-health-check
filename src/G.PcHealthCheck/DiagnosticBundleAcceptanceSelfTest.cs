namespace G.PcHealthCheck;

internal static class DiagnosticBundleAcceptanceSelfTest
{
    public static int Run()
    {
        var failures = new List<string>(); var count = 0;
        void Test(string name, Action action) { count++; try { action(); } catch (Exception ex) { failures.Add(name + ": " + ex.Message); } }

        Test("collector surface contains only approved read-only categories", () =>
        {
            var names = typeof(IDiagnosticBundleCollector).GetMethods().Select(m => m.Name).OrderBy(x => x).ToArray();
            var expected = new[] { "CollectEndpoints", "CollectEvents", "CollectHealthAsync", "CollectPerformanceAsync", "CollectProcesses", "CollectStorage" };
            Require(names.SequenceEqual(expected), "Unexpected collector operation: " + string.Join(",", names));
            Require(names.All(name => !name.Contains("Probe", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("Remediation", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("Temp", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("FileUse", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("Observation", StringComparison.OrdinalIgnoreCase)), "Forbidden operation exposed.");
        });

        Test("Extended deterministic performance payload and marker survive report", () =>
        {
            var collector = new ExtendedFakeCollector();
            var options = new DiagnosticBundleOptions(DiagnosticBundleMode.Extended,
                new HashSet<DiagnosticBundleCategory> { DiagnosticBundleCategory.Performance }, 30, 5);
            var snapshot = new DiagnosticBundleService().CollectAsync(options, collector, null, null, null, CancellationToken.None).GetAwaiter().GetResult();
            Require(collector.ReceivedOptions == new PerformanceSessionOptions(30, 5), "Coordinator changed timing options.");
            Require(snapshot.Performance.Payload is { Samples.Count: 6, Markers.Count: 1 } && snapshot.Outcome == "Complete", "Timed payload lost.");
            var html = DiagnosticBundleReport.Html(snapshot); var json = DiagnosticBundleReport.ManifestJson(snapshot);
            Require(html.Contains("synthetic symptom", StringComparison.Ordinal) && json.Contains("Performance", StringComparison.Ordinal), "Performance evidence missing from report.");
        });

        Test("production Quick reduced envelope never requests performance", () =>
        {
            // Use one fast native category here: --selftest also runs against the packaged and renamed portable EXE,
            // so exercising the full WMI/Event Log Quick set would multiply slow provider waits without increasing
            // coverage of coordinator ordering (covered synthetically above).
            var options = new DiagnosticBundleOptions(DiagnosticBundleMode.Quick,
                new HashSet<DiagnosticBundleCategory> { DiagnosticBundleCategory.Endpoints });
            var snapshot = new DiagnosticBundleService().CollectAsync(options, new WindowsDiagnosticBundleCollector(),
                ExecutionContextService.Capture(), null, null, CancellationToken.None).GetAwaiter().GetResult();
            Require(!snapshot.Performance.Requested && snapshot.Performance.State == "NotRequested", "Quick started timed performance.");
            Require(snapshot.Endpoints.Requested, "Native endpoint category was not requested.");
            Require(snapshot.Endpoints.State is "Complete" or "Partial" or "Unavailable", "Native endpoint source did not reach a terminal state.");
            Require(snapshot.Sources.Where(x => x.Category != DiagnosticBundleCategory.Endpoints).All(x => !x.Requested && x.State == "NotRequested"),
                "Reduced envelope collected an unrequested source.");
            Require(snapshot.Sources.All(x => x.State is not "Pending" and not "Running"), "Coordinator left unfinished source state.");
            Require(snapshot.Outcome is "Complete" or "Partial" or "Unavailable", "Unexpected Quick outcome.");
        });

        Console.WriteLine($"Diagnostic bundle acceptance: {count - failures.Count}/{count} passed.");
        foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 252;
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class ExtendedFakeCollector : IDiagnosticBundleCollector
    {
        public PerformanceSessionOptions? ReceivedOptions { get; private set; }
        public Task<DiagnosticBundleHealthPayload> CollectHealthAsync(IProgress<string>? progress, CancellationToken ct) => throw new InvalidOperationException("Not requested");
        public ProcessReviewSnapshot CollectProcesses(CancellationToken ct) => throw new InvalidOperationException("Not requested");
        public EndpointSnapshot CollectEndpoints(ExecutionContextInfo? context, IProgress<string>? progress, CancellationToken ct) => throw new InvalidOperationException("Not requested");
        public IncidentSnapshot CollectEvents(IncidentWindow window, CancellationToken ct) => throw new InvalidOperationException("Not requested");
        public DiskDetailsSnapshot CollectStorage(CancellationToken ct) => throw new InvalidOperationException("Not requested");
        public Task<PerformanceSessionSnapshot> CollectPerformanceAsync(PerformanceSessionOptions options, IProgress<PerformanceSample>? progress, CancellationToken ct)
        {
            ReceivedOptions = options;
            var started = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
            var snapshot = new PerformanceSessionSnapshot { Options = options, StartedAt = started, FinishedAt = started.AddSeconds(30), ElapsedMs = 30000, Outcome = "Completed" };
            for (var second = 5; second <= 30; second += 5)
                snapshot.Samples.Add(new PerformanceSample(second * 1000L, 1, started.AddSeconds(second), new PerformanceReading(10, 20, 5, 1, [])));
            snapshot.Markers.Add(new PerformanceMarker(10000, "synthetic symptom"));
            return Task.FromResult(snapshot);
        }
    }
}
