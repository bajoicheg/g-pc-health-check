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

        Console.WriteLine($"Diagnostic bundle regression: {count - failures.Count}/{count} passed.");
        foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 249;
    }

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
}
