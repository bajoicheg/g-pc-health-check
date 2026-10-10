using System.Reflection;

namespace G.PcHealthCheck;

internal static class SecurityPostureIntegrationSelfTest
{
    private static readonly string[] AntivirusIds =
    [
        "SEC-AV-ACTIVE", "SEC-AV-PLATFORM", "SEC-AV-DEFINITIONS", "SEC-AV-RTP", "SEC-AV-TAMPER"
    ];

    public static int Run()
    {
        var failures = 0;
        void Test(string name, Action body)
        {
            try { body(); Console.WriteLine($"Security posture integration self-test: PASS — {name}"); }
            catch (Exception ex) { failures++; Console.Error.WriteLine($"Security posture integration self-test: FAIL — {name}: {ex.InnerException?.Message ?? ex.Message}"); }
        }

        Test("one provider failure becomes Unknown while other controls still evaluate", () =>
        {
            var backend = new FakeBackend { ThrowAntivirus = true };
            var collected = SecurityPostureCollector.CollectAsync(
                SystemInfo(), CancellationToken.None, null, backend).GetAwaiter().GetResult();

            foreach (var id in AntivirusIds)
                Require(collected.Assessment.Controls.Single(x => x.Id == id).Status == SecurityControlStatus.Unknown,
                    $"{id} must be Unknown after antivirus provider failure.");
            Require(collected.Assessment.Controls.Single(x => x.Id == "SEC-FIREWALL").Status == SecurityControlStatus.Pass,
                "Independent platform controls must still evaluate.");
            Require(collected.Assessment.CollectionWarnings.Any(x => x.Contains("Antivirus", StringComparison.Ordinal)),
                "Provider failure must be retained as a bounded collection warning.");
            Require(collected.Assessment.CoveragePercent < 100, "Unknown provider controls must lower security coverage.");
        });

        Test("adding Security never changes the technical Health assessment", () =>
        {
            var data = TechnicalData();
            var assessmentService = new AssessmentService();
            var baseline = assessmentService.Assess(data);
            var enriched = assessmentService.Assess(data);
            var security = SecurityPostureCollector.CollectAsync(
                data.System, CancellationToken.None, null, new FakeBackend()).GetAwaiter().GetResult();

            enriched.Security = security.Assessment;
            enriched.SecuritySnapshot = security.Snapshot;

            Require(baseline.Assessment.Score == enriched.Assessment.Score, "Security integration changed technical score.");
            Require(baseline.Assessment.Status == enriched.Assessment.Status, "Security integration changed technical status.");
            Require(baseline.Assessment.CoveragePercent == enriched.Assessment.CoveragePercent, "Security integration changed technical coverage.");
            Require(enriched.Security?.Score == 100 && enriched.Security.CoveragePercent == 100, "Synthetic all-pass security posture must remain separate and complete.");
            Require(enriched.SecuritySnapshot is not null, "Internal security snapshot must be retained for UI/report details.");
        });

        Test("pre-cancelled collection does not enter native providers", () =>
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var backend = new FakeBackend();
            var cancelled = false;
            try
            {
                _ = SecurityPostureCollector.CollectAsync(SystemInfo(), cancellation.Token, null, backend).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { cancelled = true; }
            Require(cancelled, "Pre-cancelled security collection must cancel.");
            Require(backend.CallCount == 0, "Pre-cancelled collection must not enter providers.");
        });

        Test("security progress remains guarded by the main scan progress owner", () =>
        {
            using var form = new MainForm();
            var type = typeof(MainForm);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var ownerField = type.GetField("_scanProgressOwner", flags);
            var statusField = type.GetField("_status", flags);
            var apply = type.GetMethod("ApplyScanProgress", flags);
            Require(ownerField is not null && statusField is not null && apply is not null, "Main scan progress ownership boundary missing.");

            var status = (Label)statusField!.GetValue(form)!;
            var oldOwner = new object();
            var newOwner = new object();
            ownerField!.SetValue(form, newOwner);
            status.Text = "new scan active";
            apply!.Invoke(form, [oldOwner, "ИБ: late old security progress"]);
            Require(status.Text == "new scan active", "Stale security progress overwrote newer scan status.");
        });

        Console.WriteLine($"Security posture integration self-test: {4 - failures}/4 passed.");
        return failures == 0 ? 0 : 1;
    }

    private static SystemInfo SystemInfo()
        => new() { ComputerName = "PC01", Manufacturer = "Synthetic OEM", CollectedAt = new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Local) };

    private static DiagnosticData TechnicalData()
        => new()
        {
            System = new SystemInfo { ComputerName = "PC01", Manufacturer = "Synthetic OEM", UptimeDays = 2 },
            Performance = new PerformanceSnapshot { CpuPercent = 20, MemoryAvailablePercent = 50, DiskBusyPercent = 10, DiskQueueLength = 0.1 },
            LogicalDisks = [new LogicalDiskInfo { Drive = "C:", SizeGB = 500, FreeGB = 250, FreePercent = 50 }],
            PhysicalDisks = [new PhysicalDiskInfo { Name = "Disk0", HealthStatus = "Healthy" }],
            Events = new EventSummary { Hours = 24, CriticalCount = 0, ErrorCount = 0 },
            PendingReboot = new PendingRebootInfo { Pending = false },
            Updates = new UpdateInfo { Wuauserv = "Running", Bits = "Running", RecentHotfixes = ["KB000000"] }
        };

    private static SecurityControlObservation Obs(string id)
        => new(id, SecurityControlStatus.Pass, [new SecurityEvidence("Synthetic", "Pass", "Synthetic")], id);

    private sealed class FakeBackend : ISecurityPostureCollectionBackend
    {
        public bool ThrowAntivirus { get; init; }
        public int CallCount { get; private set; }

        public Dictionary<string, SecurityControlObservation> CollectWindowsPlatform()
        {
            CallCount++;
            return new(StringComparer.Ordinal)
            {
                ["SEC-FIREWALL"] = Obs("SEC-FIREWALL"),
                ["SEC-SECUREBOOT"] = Obs("SEC-SECUREBOOT"),
                ["SEC-UAC"] = Obs("SEC-UAC"),
                ["SEC-TPM"] = Obs("SEC-TPM"),
                ["SEC-VBS-HVCI"] = Obs("SEC-VBS-HVCI")
            };
        }

        public Dictionary<string, SecurityControlObservation> CollectAntivirus()
        {
            CallCount++;
            if (ThrowAntivirus) throw new InvalidOperationException("synthetic antivirus failure");
            return AntivirusIds.ToDictionary(x => x, Obs, StringComparer.Ordinal);
        }

        public SecurityControlObservation CollectWindowsUpdate()
        {
            CallCount++;
            return Obs("SEC-OS-UPDATES");
        }

        public Dictionary<string, SecurityControlObservation> CollectBitLocker()
        {
            CallCount++;
            return new(StringComparer.Ordinal)
            {
                ["SEC-BITLOCKER-OS"] = Obs("SEC-BITLOCKER-OS"),
                ["SEC-BITLOCKER-DATA"] = Obs("SEC-BITLOCKER-DATA")
            };
        }

        public Dictionary<string, SecurityControlObservation> CollectFirmware(SystemInfo system)
        {
            CallCount++;
            return new(StringComparer.Ordinal)
            {
                ["SEC-BIOS-ADMIN-PASSWORD"] = Obs("SEC-BIOS-ADMIN-PASSWORD"),
                ["SEC-BOOT-RESTRICTIONS"] = Obs("SEC-BOOT-RESTRICTIONS")
            };
        }

        public LocalAdministratorsCollectionResult CollectLocalAdministrators(SystemInfo system)
        {
            CallCount++;
            var policy = new LocalAdminPolicy(true, true, ["LOCAL\\admin"], "Synthetic");
            var member = new LocalAdminMember("LOCAL\\admin", "S-1-5-21-1-2-3-1000", "User", "Local", "Allowed", "LOCAL\\admin", "NameRuleMatched");
            return new(policy, [member], Obs("SEC-LOCAL-ADMINS"));
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
