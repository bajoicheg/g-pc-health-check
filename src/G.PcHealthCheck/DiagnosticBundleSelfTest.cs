namespace G.PcHealthCheck;

internal static class DiagnosticBundleSelfTest
{
    public static int Run()
    {
        var failures = new List<string>();
        var count = 0;
        void Test(string name, Action action)
        {
            count++;
            try { action(); }
            catch (Exception ex) { failures.Add(name + ": " + ex.Message); }
        }

        Test("Quick defaults exclude performance", () =>
        {
            var options = DiagnosticBundleCore.DefaultOptions(DiagnosticBundleMode.Quick);
            Require(options.Categories.SetEquals(new[]
            {
                DiagnosticBundleCategory.Health,
                DiagnosticBundleCategory.Processes,
                DiagnosticBundleCategory.Endpoints,
                DiagnosticBundleCategory.Events,
                DiagnosticBundleCategory.Storage
            }));
            Require(!options.Categories.Contains(DiagnosticBundleCategory.Performance));
        });

        Test("Extended defaults include performance 60s 2s", () =>
        {
            var options = DiagnosticBundleCore.DefaultOptions(DiagnosticBundleMode.Extended);
            Require(options.Categories.Contains(DiagnosticBundleCategory.Performance));
            Require(options.PerformanceSeconds == 60 && options.PerformanceIntervalSeconds == 2);
        });

        Test("options copy caller category set", () =>
        {
            var categories = new HashSet<DiagnosticBundleCategory> { DiagnosticBundleCategory.Health };
            var options = new DiagnosticBundleOptions(DiagnosticBundleMode.Quick, categories);
            categories.Add(DiagnosticBundleCategory.Performance);
            Require(options.Categories.SetEquals(new[] { DiagnosticBundleCategory.Health }));
        });

        Test("NotRequested is distinct from unavailable", () =>
        {
            var snapshot = new DiagnosticBundleSnapshot
            {
                Processes = Source(DiagnosticBundleCategory.Processes, "Complete", true, new ProcessReviewSnapshot()),
                Events = Source<IncidentSnapshot>(DiagnosticBundleCategory.Events, "NotRequested", false)
            };
            Require(DiagnosticBundleCore.CalculateOutcome(snapshot) == "Complete");
        });

        Test("useful partial bundle remains Partial", () =>
        {
            var snapshot = new DiagnosticBundleSnapshot
            {
                Processes = Source(DiagnosticBundleCategory.Processes, "Complete", true, new ProcessReviewSnapshot()),
                Events = Source<IncidentSnapshot>(DiagnosticBundleCategory.Events, "Unavailable", true)
            };
            Require(DiagnosticBundleCore.CalculateOutcome(snapshot) == "Partial");
        });

        Test("all requested unavailable is Unavailable", () =>
        {
            var snapshot = new DiagnosticBundleSnapshot
            {
                Events = Source<IncidentSnapshot>(DiagnosticBundleCategory.Events, "Unavailable", true),
                Storage = Source<DiskDetailsSnapshot>(DiagnosticBundleCategory.Storage, "Unavailable", true)
            };
            Require(DiagnosticBundleCore.CalculateOutcome(snapshot) == "Unavailable");
        });

        Test("cancel before useful evidence is Cancelled", () =>
        {
            var snapshot = new DiagnosticBundleSnapshot
            {
                CancellationRequested = true,
                Processes = Source<ProcessReviewSnapshot>(DiagnosticBundleCategory.Processes, "Cancelled", true)
            };
            Require(DiagnosticBundleCore.CalculateOutcome(snapshot) == "Cancelled");
        });

        Test("cancel after useful evidence is Partial", () =>
        {
            var snapshot = new DiagnosticBundleSnapshot
            {
                CancellationRequested = true,
                Processes = Source(DiagnosticBundleCategory.Processes, "Complete", true, new ProcessReviewSnapshot()),
                Events = Source<IncidentSnapshot>(DiagnosticBundleCategory.Events, "Cancelled", true)
            };
            Require(DiagnosticBundleCore.CalculateOutcome(snapshot) == "Partial");
        });

        Test("all requested complete is Complete", () =>
        {
            var snapshot = new DiagnosticBundleSnapshot
            {
                Processes = Source(DiagnosticBundleCategory.Processes, "Complete", true, new ProcessReviewSnapshot()),
                Events = Source(DiagnosticBundleCategory.Events, "Complete", true, new IncidentSnapshot())
            };
            Require(DiagnosticBundleCore.CalculateOutcome(snapshot) == "Complete");
        });

        Test("Quick rejects performance category", () => ThrowsArgument(() => DiagnosticBundleCore.Validate(
            new DiagnosticBundleOptions(DiagnosticBundleMode.Quick,
                new HashSet<DiagnosticBundleCategory> { DiagnosticBundleCategory.Health, DiagnosticBundleCategory.Performance }))));

        Test("zero requested categories rejected", () => ThrowsArgument(() => DiagnosticBundleCore.Validate(
            new DiagnosticBundleOptions(DiagnosticBundleMode.Quick, new HashSet<DiagnosticBundleCategory>()))));

        foreach (var seconds in new[] { 0, 29, 31, 61 })
            Test("invalid Extended duration " + seconds, () => ThrowsArgument(() => DiagnosticBundleCore.Validate(
                new DiagnosticBundleOptions(DiagnosticBundleMode.Extended,
                    new HashSet<DiagnosticBundleCategory> { DiagnosticBundleCategory.Health }, seconds, 2))));

        foreach (var interval in new[] { 0, 3, 4, 6 })
            Test("invalid Extended interval " + interval, () => ThrowsArgument(() => DiagnosticBundleCore.Validate(
                new DiagnosticBundleOptions(DiagnosticBundleMode.Extended,
                    new HashSet<DiagnosticBundleCategory> { DiagnosticBundleCategory.Health }, 60, interval))));

        Test("valid Extended opt-out still validates", () =>
        {
            var options = new DiagnosticBundleOptions(DiagnosticBundleMode.Extended,
                new HashSet<DiagnosticBundleCategory> { DiagnosticBundleCategory.Health }, 30, 5);
            DiagnosticBundleCore.Validate(options);
        });

        Test("one source failure preserves later sources", () =>
        {
            var fake = FakeBundleCollector.AllSuccess();
            fake.EventsException = new UnauthorizedAccessException("synthetic");
            var snapshot = Collect(fake, DiagnosticBundleCore.DefaultOptions(DiagnosticBundleMode.Quick));
            Require(snapshot.Events.State == "Unavailable");
            Require(snapshot.Health.State == "Complete" && snapshot.Processes.State == "Complete"
                && snapshot.Endpoints.State == "Complete" && snapshot.Storage.State == "Complete");
            Require(snapshot.Outcome == "Partial");
            Require(fake.Calls.SequenceEqual(new[]
            {
                DiagnosticBundleCategory.Health, DiagnosticBundleCategory.Processes,
                DiagnosticBundleCategory.Endpoints, DiagnosticBundleCategory.Events,
                DiagnosticBundleCategory.Storage
            }));
        });

        Test("category opt-out makes no collector call", () =>
        {
            var fake = FakeBundleCollector.AllSuccess();
            var options = Without(DiagnosticBundleCore.DefaultOptions(DiagnosticBundleMode.Quick), DiagnosticBundleCategory.Endpoints);
            var snapshot = Collect(fake, options);
            Require(!fake.Calls.Contains(DiagnosticBundleCategory.Endpoints));
            Require(!snapshot.Endpoints.Requested && snapshot.Endpoints.State == "NotRequested");
            Require(snapshot.Outcome == "Complete");
        });

        Test("pre-cancel makes zero collector calls", () =>
        {
            var fake = FakeBundleCollector.AllSuccess();
            var snapshot = Collect(fake, DiagnosticBundleCore.DefaultOptions(DiagnosticBundleMode.Quick), new CancellationToken(true));
            Require(fake.Calls.Count == 0);
            Require(snapshot.CancellationRequested && snapshot.Outcome == "Cancelled");
            Require(snapshot.Sources.Where(x => x.Requested).All(x => x.State == "Cancelled"));
        });

        Test("mid-source cancel keeps prior sources and stops later sources", () =>
        {
            using var cancellation = new CancellationTokenSource();
            var fake = FakeBundleCollector.AllSuccess();
            fake.OnProcesses = cancellation.Cancel;
            fake.Processes = new ProcessReviewSnapshot { State = "Cancelled" };
            var snapshot = Collect(fake, DiagnosticBundleCore.DefaultOptions(DiagnosticBundleMode.Quick), cancellation.Token);
            Require(fake.Calls.SequenceEqual(new[] { DiagnosticBundleCategory.Health, DiagnosticBundleCategory.Processes }));
            Require(snapshot.Health.State == "Complete" && snapshot.Health.PayloadAvailable);
            Require(snapshot.Processes.State == "Cancelled");
            Require(snapshot.Endpoints.State == "Cancelled" && snapshot.Events.State == "Cancelled" && snapshot.Storage.State == "Cancelled");
            Require(snapshot.Outcome == "Partial");
        });

        Test("cancel before Extended performance keeps point-in-time evidence", () =>
        {
            using var cancellation = new CancellationTokenSource();
            var fake = FakeBundleCollector.AllSuccess();
            fake.OnStorage = cancellation.Cancel;
            var snapshot = Collect(fake, DiagnosticBundleCore.DefaultOptions(DiagnosticBundleMode.Extended), cancellation.Token);
            Require(!fake.Calls.Contains(DiagnosticBundleCategory.Performance));
            Require(snapshot.Storage.State == "Complete" && snapshot.Storage.PayloadAvailable);
            Require(snapshot.Performance.Requested && snapshot.Performance.State == "Cancelled");
            Require(snapshot.CancellationRequested && snapshot.Outcome == "Partial");
        });

        Test("cancel during performance retains completed samples", () =>
        {
            using var cancellation = new CancellationTokenSource();
            var fake = FakeBundleCollector.AllSuccess();
            fake.OnPerformance = cancellation.Cancel;
            fake.Performance = StoppedPerformanceWithSample();
            var snapshot = Collect(fake, DiagnosticBundleCore.DefaultOptions(DiagnosticBundleMode.Extended), cancellation.Token);
            Require(fake.Calls.Last() == DiagnosticBundleCategory.Performance);
            Require(snapshot.Performance.State == "Cancelled" && snapshot.Performance.Payload is { Samples.Count: 1 });
            Require(snapshot.CancellationRequested && snapshot.Outcome == "Partial");
        });

        Test("progress names source phase without hard-timeout claim", () =>
        {
            var fake = FakeBundleCollector.AllSuccess();
            var progress = new CaptureProgress<DiagnosticBundleProgress>();
            var options = new DiagnosticBundleOptions(DiagnosticBundleMode.Quick,
                new HashSet<DiagnosticBundleCategory> { DiagnosticBundleCategory.Health });
            var snapshot = Collect(fake, options, default, progress);
            Require(snapshot.Outcome == "Complete");
            Require(progress.Values.Any(x => x.Category == DiagnosticBundleCategory.Health && x.Phase == "Starting"));
            Require(progress.Values.Any(x => x.Category == DiagnosticBundleCategory.Health && x.Phase == "Finished"));
            Require(progress.Values.All(x => !x.Message.Contains("таймаут", StringComparison.OrdinalIgnoreCase)
                && !x.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)));
        });

        Console.WriteLine($"Diagnostic bundle regression: {count - failures.Count}/{count} passed.");
        foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 249;
    }

    private static DiagnosticBundleSnapshot Collect(FakeBundleCollector fake, DiagnosticBundleOptions options,
        CancellationToken ct = default, IProgress<DiagnosticBundleProgress>? progress = null)
        => new DiagnosticBundleService().CollectAsync(options, fake, null, progress, null, ct).GetAwaiter().GetResult();

    private static DiagnosticBundleOptions Without(DiagnosticBundleOptions options, DiagnosticBundleCategory category)
        => new(options.Mode, options.Categories.Where(x => x != category).ToHashSet(), options.PerformanceSeconds, options.PerformanceIntervalSeconds);

    private static PerformanceSessionSnapshot StoppedPerformanceWithSample()
        => new()
        {
            Options = new PerformanceSessionOptions(60, 2),
            Outcome = "Stopped",
            StartedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            FinishedAt = DateTimeOffset.Parse("2026-01-01T00:00:02Z"),
            ElapsedMs = 2000,
            Samples =
            [
                new PerformanceSample(2000, 1, DateTimeOffset.Parse("2026-01-01T00:00:02Z"),
                    new PerformanceReading(10, 20, 5, 0, []))
            ],
            Warnings = ["Сеанс остановлен."]
        };

    private static BundleSourceResult<T> Source<T>(DiagnosticBundleCategory category, string state, bool requested, T? payload = null) where T : class
        => new() { Category = category, State = state, Requested = requested, Payload = payload };

    private static void Require(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected diagnostic bundle invariant was not satisfied.");
    }

    private static void ThrowsArgument(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Expected ArgumentException.");
    }

    private sealed class CaptureProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];
        public void Report(T value) => Values.Add(value);
    }

    private sealed class FakeBundleCollector : IDiagnosticBundleCollector
    {
        public List<DiagnosticBundleCategory> Calls { get; } = [];
        public Exception? EventsException { get; set; }
        public Action? OnProcesses { get; set; }
        public Action? OnStorage { get; set; }
        public Action? OnPerformance { get; set; }
        public ProcessReviewSnapshot Processes { get; set; } = new() { State = "Complete" };
        public EndpointSnapshot Endpoints { get; set; } = new() { State = "Complete" };
        public IncidentSnapshot Events { get; set; } = new() { State = "Complete" };
        public DiskDetailsSnapshot Storage { get; set; } = new() { Outcome = "Completed" };
        public PerformanceSessionSnapshot? Performance { get; set; }

        public static FakeBundleCollector AllSuccess() => new();

        public Task<DiagnosticBundleHealthPayload> CollectHealthAsync(IProgress<string>? progress, CancellationToken ct)
        {
            Calls.Add(DiagnosticBundleCategory.Health); ct.ThrowIfCancellationRequested();
            var data = new DiagnosticData();
            var assessment = new ScanResult { Data = data, Assessment = new Assessment { CoveragePercent = 100, CoverageStatus = "HIGH" } };
            return Task.FromResult(new DiagnosticBundleHealthPayload(data, assessment));
        }

        public ProcessReviewSnapshot CollectProcesses(CancellationToken ct)
        {
            Calls.Add(DiagnosticBundleCategory.Processes); ct.ThrowIfCancellationRequested(); OnProcesses?.Invoke(); return Processes;
        }

        public EndpointSnapshot CollectEndpoints(ExecutionContextInfo? context, IProgress<string>? progress, CancellationToken ct)
        {
            Calls.Add(DiagnosticBundleCategory.Endpoints); ct.ThrowIfCancellationRequested(); return Endpoints;
        }

        public IncidentSnapshot CollectEvents(IncidentWindow window, CancellationToken ct)
        {
            Calls.Add(DiagnosticBundleCategory.Events); ct.ThrowIfCancellationRequested();
            if (EventsException is not null) throw EventsException;
            return Events;
        }

        public DiskDetailsSnapshot CollectStorage(CancellationToken ct)
        {
            Calls.Add(DiagnosticBundleCategory.Storage); ct.ThrowIfCancellationRequested(); OnStorage?.Invoke(); return Storage;
        }

        public Task<PerformanceSessionSnapshot> CollectPerformanceAsync(PerformanceSessionOptions options,
            IProgress<PerformanceSample>? progress, CancellationToken ct)
        {
            Calls.Add(DiagnosticBundleCategory.Performance); ct.ThrowIfCancellationRequested(); OnPerformance?.Invoke();
            if (Performance is not null) return Task.FromResult(Performance);
            var snapshot = new PerformanceSessionSnapshot
            {
                Options = options,
                Outcome = "Completed",
                StartedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                FinishedAt = DateTimeOffset.Parse("2026-01-01T00:01:00Z"),
                ElapsedMs = options.DurationSeconds * 1000L
            };
            for (var second = options.IntervalSeconds; second <= options.DurationSeconds; second += options.IntervalSeconds)
                snapshot.Samples.Add(new PerformanceSample(second * 1000L, 1,
                    snapshot.StartedAt.AddSeconds(second), new PerformanceReading(10, 20, 5, 0, [])));
            return Task.FromResult(snapshot);
        }
    }
}
