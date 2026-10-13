using System.Reflection;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class SecurityReportSelfTest
{
    private const string RecoverySecret = "111111-222222-333333-444444-555555-666666-777777-888888";
    private const string BiosSecret = "Synthetic-BIOS-Password!";
    private const string TokenSecret = "synthetic-token-secret-123";

    public static int Run()
    {
        var failures = 0;
        void Test(string name, Action body)
        {
            try { body(); Console.WriteLine($"Security report self-test: PASS — {name}"); }
            catch (Exception ex) { failures++; Console.Error.WriteLine($"Security report self-test: FAIL — {name}: {ex.InnerException?.Message ?? ex.Message}"); }
        }

        Test("localized Security HTML contains score coverage model IDs and distinct states", () =>
        {
            var scan = SyntheticScan();
            var ru = SecurityReportSection.BuildHtml(scan, "ru");
            var en = SecurityReportSection.BuildHtml(scan, "en");
            foreach (var html in new[] { ru, en })
            {
                Require(html.Contains("EndpointSecurityPosture-v1", StringComparison.Ordinal), "Security model version missing from HTML.");
                Require(html.Contains("71", StringComparison.Ordinal) && html.Contains("80%", StringComparison.Ordinal), "Security score/coverage missing from HTML.");
                Require(html.Contains("SEC-LOCAL-ADMINS", StringComparison.Ordinal), "Stable control ID missing from HTML.");
                Require(html.Contains("SEC-BIOS-ADMIN-PASSWORD", StringComparison.Ordinal), "Unknown control ID missing from HTML.");
                Require(html.Contains("S-1-5-21-100-200-300-1300", StringComparison.Ordinal), "Unauthorized principal SID missing from HTML.");
                Require(html.Contains("D:", StringComparison.Ordinal), "Applicable data volume evidence missing from HTML.");
            }
            Require(ru.Contains(AppLocalization.TextForCulture("ru", "Security.Status.Fail"), StringComparison.Ordinal), "RU Fail framing missing.");
            Require(ru.Contains(AppLocalization.TextForCulture("ru", "Security.Status.Unknown"), StringComparison.Ordinal), "RU Unknown framing missing.");
            Require(en.Contains(AppLocalization.TextForCulture("en", "Security.Status.Fail"), StringComparison.Ordinal), "EN Fail framing missing.");
            Require(en.Contains(AppLocalization.TextForCulture("en", "Security.Status.Unknown"), StringComparison.Ordinal), "EN Unknown framing missing.");
        });

        Test("ordinary HTML report embeds Security section", () =>
        {
            var method = typeof(ReportService).GetMethod("BuildScanHtml", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("BuildScanHtml test hook missing.");
            var html = (string)method.Invoke(null, [SyntheticScan()])!;
            Require(html.Contains("EndpointSecurityPosture-v1", StringComparison.Ordinal), "Main HTML report omitted Security section.");
            Require(html.Contains("SEC-LOCAL-ADMINS", StringComparison.Ordinal), "Main HTML report omitted Security controls.");
        });

        Test("public JSON contains bounded Security assessment but excludes raw snapshot", () =>
        {
            var scan = SyntheticScan();
            var json = JsonSerializer.Serialize(scan);
            Require(json.Contains("EndpointSecurityPosture-v1", StringComparison.Ordinal), "Security model missing from JSON.");
            Require(json.Contains("SEC-LOCAL-ADMINS", StringComparison.Ordinal), "Control IDs missing from JSON.");
            Require(json.Contains("S-1-5-21-100-200-300-1300", StringComparison.Ordinal), "Bounded SID evidence missing from JSON.");
            Require(json.Contains("D:", StringComparison.Ordinal), "Bounded volume evidence missing from JSON.");
            Require(!json.Contains("SecuritySnapshot", StringComparison.Ordinal), "Internal SecuritySnapshot must stay JsonIgnore.");

            using var document = JsonDocument.Parse(json);
            var controls = document.RootElement.GetProperty("Security").GetProperty("Controls");
            var fail = controls.EnumerateArray().Single(x => x.GetProperty("Id").GetString() == "SEC-LOCAL-ADMINS").GetProperty("Status").GetInt32();
            var unknown = controls.EnumerateArray().Single(x => x.GetProperty("Id").GetString() == "SEC-BIOS-ADMIN-PASSWORD").GetProperty("Status").GetInt32();
            Require(fail != unknown, "Fail and Unknown must remain distinct in JSON.");
        });

        Test("clipboard summary carries concise separate Security posture", () =>
        {
            var summary = SupportSummary.Build(SyntheticScan());
            Require(summary.Contains("Security", StringComparison.OrdinalIgnoreCase), "Clipboard summary omitted Security posture.");
            Require(summary.Contains("71", StringComparison.Ordinal) && summary.Contains("80%", StringComparison.Ordinal), "Clipboard summary omitted Security score/coverage.");
            Require(summary.Contains("SEC-LOCAL-ADMINS", StringComparison.Ordinal), "Clipboard summary omitted actionable Security control ID.");
        });

        Test("hardening before-after and action evidence persist in HTML and public JSON without raw output", () =>
        {
            var scan = SyntheticScan();
            scan.SecurityHardening = new SecurityHardeningEvidence
            {
                Before = new SecurityPostureAssessment
                {
                    Score = 54,
                    CoveragePercent = 82,
                    RawBand = SecurityBand.NeedsAttention,
                    DisplayBand = SecurityBand.NeedsAttention,
                    Controls = [new("SEC-AV-DEFINITIONS", SecurityControlStatus.Fail, 8, 0, "SEC-AV-DEFINITIONS", [])]
                },
                After = scan.Security!,
                StartedAt = new DateTime(2026, 9, 16, 12, 30, 0),
                FinishedAt = new DateTime(2026, 9, 16, 12, 30, 10),
                Elevated = true,
                Actions =
                [
                    new SecurityHardeningActionEvidence
                    {
                        Id = "SecurityUpdateAvDefinitions",
                        Success = true,
                        BlockedByPolicy = false,
                        ExitCode = 0,
                        Message = "Definitions update completed",
                        TargetScope = "PrimaryAvDefinitions",
                        StartedAt = new DateTime(2026, 9, 16, 12, 30, 1),
                        FinishedAt = new DateTime(2026, 9, 16, 12, 30, 5)
                    }
                ]
            };

            var html = SecurityReportSection.BuildHtml(scan, "en");
            var json = JsonSerializer.Serialize(scan);
            foreach (var output in new[] { html, json })
            {
                Require(output.Contains("SecurityUpdateAvDefinitions", StringComparison.Ordinal), "Hardening action ID missing from persisted evidence.");
                Require(output.Contains("54", StringComparison.Ordinal) && output.Contains("71", StringComparison.Ordinal), "Hardening before/after scores missing from persisted evidence.");
                Require(output.Contains("PrimaryAvDefinitions", StringComparison.Ordinal), "Hardening action scope missing from persisted evidence.");
                Require(!output.Contains(RecoverySecret, StringComparison.Ordinal), "Recovery secret leaked through hardening evidence.");
                Require(!output.Contains(BiosSecret, StringComparison.Ordinal), "BIOS secret leaked through hardening evidence.");
                Require(!output.Contains(TokenSecret, StringComparison.Ordinal), "Token secret leaked through hardening evidence.");
            }

            using var document = JsonDocument.Parse(json);
            var hardening = document.RootElement.GetProperty("SecurityHardening");
            Require(hardening.GetProperty("Before").GetProperty("Score").GetInt32() == 54, "JSON before score drifted.");
            Require(hardening.GetProperty("After").GetProperty("Score").GetInt32() == 71, "JSON after score drifted.");
            Require(hardening.GetProperty("Actions")[0].GetProperty("Id").GetString() == "SecurityUpdateAvDefinitions", "JSON hardening action evidence drifted.");
            Require(!json.Contains("Output", StringComparison.Ordinal), "Raw native hardening Output property leaked into public JSON.");
        });

        Test("raw snapshot secrets never leak to HTML JSON or clipboard", () =>
        {
            var scan = SyntheticScan();
            var outputs = new[]
            {
                SecurityReportSection.BuildHtml(scan, "ru"),
                SecurityReportSection.BuildHtml(scan, "en"),
                JsonSerializer.Serialize(scan),
                SupportSummary.Build(scan)
            };
            foreach (var output in outputs)
            {
                Require(!output.Contains(RecoverySecret, StringComparison.Ordinal), "Recovery secret leaked.");
                Require(!output.Contains(BiosSecret, StringComparison.Ordinal), "BIOS secret leaked.");
                Require(!output.Contains(TokenSecret, StringComparison.Ordinal), "Token secret leaked.");
            }
        });

        Console.WriteLine($"Security report self-test: {6 - failures}/6 passed.");
        return failures == 0 ? 0 : 1;
    }

    private static ScanResult SyntheticScan()
    {
        var passVolume = new SecurityControlResult(
            "SEC-BITLOCKER-DATA", SecurityControlStatus.Pass, 5, 1, "SEC-BITLOCKER-DATA",
            [
                new("Volume", "D:", "BitLocker"),
                new("ProtectionStatus", "On", "BitLocker"),
                new("ConversionStatus", "FullyEncrypted", "BitLocker")
            ]);
        var failAdmin = new SecurityControlResult(
            "SEC-LOCAL-ADMINS", SecurityControlStatus.Fail, 8, 0, "SEC-LOCAL-ADMINS",
            [
                new("Member", "DOMAIN\\ordinary-user|User|Unauthorized", "NetAPI32"),
                new("MemberSid", "S-1-5-21-100-200-300-1300", "NetAPI32")
            ]);
        var unknownFirmware = new SecurityControlResult(
            "SEC-BIOS-ADMIN-PASSWORD", SecurityControlStatus.Unknown, 5, 0, "SEC-BIOS-ADMIN-PASSWORD",
            [new("Provider", "Unavailable", "Firmware")]);
        var passFirewall = new SecurityControlResult(
            "SEC-FIREWALL", SecurityControlStatus.Pass, 11, 1, "SEC-FIREWALL",
            [new("EffectiveProvider", "Windows", "WindowsFirewall/WSC")]);

        var snapshot = new SecurityPostureSnapshot
        {
            CollectionWarnings = [RecoverySecret, BiosSecret, TokenSecret],
            ControlObservations = new(StringComparer.Ordinal)
            {
                ["RAW-SECRET"] = new(
                    "RAW-SECRET",
                    SecurityControlStatus.Unknown,
                    [
                        new("RecoveryPassword", RecoverySecret, "SyntheticRaw"),
                        new("BiosPassword", BiosSecret, "SyntheticRaw"),
                        new("Token", TokenSecret, "SyntheticRaw")
                    ],
                    "RAW-SECRET")
            }
        };

        return new ScanResult
        {
            Data = new DiagnosticData
            {
                System = new SystemInfo { ComputerName = "PC01", UserName = "user", Model = "Synthetic", CollectedAt = new DateTime(2026, 9, 16, 12, 0, 0) }
            },
            Assessment = new Assessment { Score = 90, Status = "OK", CoveragePercent = 100, CoverageStatus = "HIGH" },
            Security = new SecurityPostureAssessment
            {
                Score = 71,
                CoveragePercent = 80,
                RawBand = SecurityBand.Good,
                DisplayBand = SecurityBand.Low,
                CriticalOverrides = ["SEC-LOCAL-ADMINS"],
                Controls = [passVolume, failAdmin, unknownFirmware, passFirewall]
            },
            SecuritySnapshot = snapshot
        };
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
