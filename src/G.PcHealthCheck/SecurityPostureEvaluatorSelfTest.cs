namespace G.PcHealthCheck;

internal static class SecurityPostureEvaluatorSelfTest
{
    public static int Run()
    {
        var failures = 0;
        void Test(string name, Action body)
        {
            try
            {
                body();
                Console.WriteLine($"Security posture evaluator self-test: PASS — {name}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.Error.WriteLine($"Security posture evaluator self-test: FAIL — {name}: {ex.Message}");
            }
        }

        Test("final control catalog is exactly 100 points", () =>
        {
            Require(SecurityControlCatalog.All.Sum(x => x.Weight) == 100, "Security weights must equal 100.");
            Require(SecurityControlCatalog.All.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() == SecurityControlCatalog.All.Count,
                "Security control IDs must be unique.");
            Require(SecurityControlCatalog.All.Single(x => x.Id == "SEC-LOCAL-ADMINS").Weight == 8,
                "Local Administrators weight must remain 8.");
        });

        Test("all-pass posture is 100 with full coverage", () =>
        {
            var assessment = SecurityPostureEvaluator.Evaluate(All(SecurityControlStatus.Pass));
            Require(assessment.Score == 100, "All-pass score must be 100.");
            Require(assessment.CoveragePercent == 100, "All-pass coverage must be 100.");
            Require(assessment.RawBand == SecurityBand.High && assessment.DisplayBand == SecurityBand.High,
                "All-pass posture must be High.");
        });

        Test("warn earns half weight and fail earns zero", () =>
        {
            var statuses = SecurityControlCatalog.All.ToDictionary(x => x.Id, _ => SecurityControlStatus.Pass, StringComparer.Ordinal);
            statuses["SEC-AV-DEFINITIONS"] = SecurityControlStatus.Warn; // 8 -> earns 4
            statuses["SEC-UAC"] = SecurityControlStatus.Fail;            // 4 -> earns 0
            var assessment = SecurityPostureEvaluator.Evaluate(From(statuses));
            Require(assessment.CoveragePercent == 100, "Known Warn/Fail controls must keep full coverage.");
            Require(assessment.Score == 92, $"Expected score 92, got {assessment.Score?.ToString() ?? "null"}.");
        });

        Test("unknown lowers coverage without becoming failure", () =>
        {
            var statuses = SecurityControlCatalog.All.ToDictionary(x => x.Id, _ => SecurityControlStatus.Pass, StringComparer.Ordinal);
            statuses["SEC-LOCAL-ADMINS"] = SecurityControlStatus.Unknown; // remove 8 known points
            var assessment = SecurityPostureEvaluator.Evaluate(From(statuses));
            Require(assessment.Score == 100, "Unknown telemetry must not be scored as failure.");
            Require(assessment.CoveragePercent == 92, $"Expected 92% coverage, got {assessment.CoveragePercent}%.");
            Require(assessment.DisplayBand == SecurityBand.High, "92% coverage may still present High when all known controls pass.");
        });

        Test("not-applicable is removed from both score and coverage denominator", () =>
        {
            var statuses = SecurityControlCatalog.All.ToDictionary(x => x.Id, _ => SecurityControlStatus.Pass, StringComparer.Ordinal);
            statuses["SEC-BITLOCKER-DATA"] = SecurityControlStatus.NotApplicable;
            var assessment = SecurityPostureEvaluator.Evaluate(From(statuses));
            Require(assessment.Score == 100, "N/A control must not reduce score.");
            Require(assessment.CoveragePercent == 100, "N/A control must not reduce coverage.");
        });

        Test("coverage below 80 forces assessment incomplete", () =>
        {
            var statuses = SecurityControlCatalog.All.ToDictionary(x => x.Id, _ => SecurityControlStatus.Unknown, StringComparer.Ordinal);
            foreach (var id in new[] { "SEC-AV-ACTIVE", "SEC-AV-DEFINITIONS", "SEC-FIREWALL", "SEC-BITLOCKER-OS", "SEC-LOCAL-ADMINS" })
                statuses[id] = SecurityControlStatus.Pass;
            var assessment = SecurityPostureEvaluator.Evaluate(From(statuses));
            Require(assessment.Score == 100, "Known passing controls must retain normalized score 100.");
            Require(assessment.CoveragePercent < 80, "Synthetic coverage must be below 80%.");
            Require(assessment.DisplayBand == SecurityBand.AssessmentIncomplete, "Coverage <80 must force AssessmentIncomplete.");
        });

        Test("critical Low caps override arithmetic score", () =>
        {
            foreach (var id in new[] { "SEC-AV-ACTIVE", "SEC-FIREWALL", "SEC-BITLOCKER-OS", "SEC-LOCAL-ADMINS" })
            {
                var assessment = EvaluateWithFail(id);
                Require(assessment.DisplayBand == SecurityBand.Low, $"{id} must cap band at Low.");
                Require(assessment.CriticalOverrides.Contains(id, StringComparer.Ordinal), $"{id} override must be recorded.");
            }
        });

        Test("needs-attention caps override High without becoming Low", () =>
        {
            foreach (var id in new[] { "SEC-OS-UPDATES", "SEC-UAC", "SEC-BIOS-ADMIN-PASSWORD", "SEC-BOOT-RESTRICTIONS", "SEC-BITLOCKER-DATA" })
            {
                var assessment = EvaluateWithFail(id);
                Require(assessment.DisplayBand == SecurityBand.NeedsAttention, $"{id} must cap High posture at NeedsAttention.");
                Require(assessment.CriticalOverrides.Contains(id, StringComparer.Ordinal), $"{id} override must be recorded.");
            }
        });

        Console.WriteLine($"Security posture evaluator self-test: {9 - failures}/9 passed.");
        return failures == 0 ? 0 : 1;
    }

    private static SecurityPostureSnapshot All(SecurityControlStatus status)
        => From(SecurityControlCatalog.All.ToDictionary(x => x.Id, _ => status, StringComparer.Ordinal));

    private static SecurityPostureSnapshot From(IReadOnlyDictionary<string, SecurityControlStatus> statuses)
        => new()
        {
            ControlObservations = SecurityControlCatalog.All.ToDictionary(
                x => x.Id,
                x => new SecurityControlObservation(x.Id, statuses[x.Id], [], "Synthetic"),
                StringComparer.Ordinal)
        };

    private static SecurityPostureAssessment EvaluateWithFail(string id)
    {
        var statuses = SecurityControlCatalog.All.ToDictionary(x => x.Id, _ => SecurityControlStatus.Pass, StringComparer.Ordinal);
        statuses[id] = SecurityControlStatus.Fail;
        return SecurityPostureEvaluator.Evaluate(From(statuses));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
