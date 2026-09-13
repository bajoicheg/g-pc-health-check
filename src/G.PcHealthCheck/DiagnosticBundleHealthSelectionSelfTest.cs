namespace G.PcHealthCheck;

internal static class DiagnosticBundleHealthSelectionSelfTest
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

        Test("operator-excluded Health is not reported as unavailable", () =>
        {
            var snapshot = Snapshot(requested: false, state: "NotRequested");
            var summary = DiagnosticBundleReport.Summary(snapshot);
            Require(summary.Contains("Health Check исключён оператором; Health Score в пакет не включён.", StringComparison.Ordinal));
            Require(!summary.Contains("Health Check не дал полезного payload", StringComparison.Ordinal));

            var html = DiagnosticBundleReport.Html(snapshot);
            Require(html.Contains("Health Check исключён оператором", StringComparison.Ordinal));
            Require(!html.Contains("Отсутствующий источник не считается здоровым", StringComparison.Ordinal));
        });

        Test("requested unavailable Health keeps missing-evidence warning", () =>
        {
            var snapshot = Snapshot(requested: true, state: "Unavailable");
            var summary = DiagnosticBundleReport.Summary(snapshot);
            Require(summary.Contains("Health Check не дал полезного payload; отсутствие оценки не считается здоровым состоянием.", StringComparison.Ordinal));

            var html = DiagnosticBundleReport.Html(snapshot);
            Require(html.Contains("Health Check: Недоступно. Отсутствующий источник не считается здоровым.", StringComparison.Ordinal));
        });

        Console.WriteLine($"Diagnostic bundle Health selection regression: {count - failures.Count}/{count} passed.");
        foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 252;
    }

    private static DiagnosticBundleSnapshot Snapshot(bool requested, string state)
    {
        var categories = requested
            ? new HashSet<DiagnosticBundleCategory> { DiagnosticBundleCategory.Health }
            : new HashSet<DiagnosticBundleCategory> { DiagnosticBundleCategory.Processes };
        return new DiagnosticBundleSnapshot
        {
            Options = new DiagnosticBundleOptions(DiagnosticBundleMode.Quick, categories),
            Outcome = requested ? "Unavailable" : "Partial",
            Health = new BundleSourceResult<DiagnosticBundleHealthPayload>
            {
                Category = DiagnosticBundleCategory.Health,
                Requested = requested,
                State = state
            }
        };
    }

    private static void Require(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected Health selection reporting invariant was not satisfied.");
    }
}
