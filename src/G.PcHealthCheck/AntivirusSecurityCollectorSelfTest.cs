namespace G.PcHealthCheck;

internal static class AntivirusSecurityCollectorSelfTest
{
    public static int Run()
    {
        var failures = 0;
        void Test(string name, Action body)
        {
            try { body(); Console.WriteLine($"Antivirus security self-test: PASS — {name}"); }
            catch (Exception ex) { failures++; Console.Error.WriteLine($"Antivirus security self-test: FAIL — {name}: {ex.Message}"); }
        }

        var now = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Local);

        Test("active Kaspersky plus passive Defender selects Kaspersky", () =>
        {
            var source = new FakeSource
            {
                Products =
                [
                    new("Kaspersky Endpoint Security", "Antivirus", "On", "UpToDate", @"C:\Program Files\Kaspersky Lab\KES\avp.exe", "WSC"),
                    new("Microsoft Defender Antivirus", "Antivirus", "Off", "UpToDate", null, "WSC")
                ],
                Kaspersky = new(true, now.AddHours(-6), "12.12.0", "KESCLI"),
                Defender = new(false, false, false, false, true, 1, "4.18.0", "Defender")
            };
            var result = AntivirusSecurityCollector.Collect(source, now);
            Require(result["SEC-AV-ACTIVE"].Status == SecurityControlStatus.Pass, "Active Kaspersky must satisfy AV active.");
            Require(result["SEC-AV-RTP"].Status == SecurityControlStatus.Pass, "Kaspersky RTP must drive RTP result.");
            Require(result["SEC-AV-DEFINITIONS"].Status == SecurityControlStatus.Pass, "Kaspersky definitions must drive definition result.");
        });

        Test("Defender-only posture uses Defender enrichment", () =>
        {
            var source = DefenderSource(now, signatureAgeDays: 1, rtp: true, behavior: true, ioav: true, tamper: true);
            var result = AntivirusSecurityCollector.Collect(source, now);
            Require(result["SEC-AV-ACTIVE"].Status == SecurityControlStatus.Pass, "Active Defender must pass.");
            Require(result["SEC-AV-DEFINITIONS"].Status == SecurityControlStatus.Pass, "Fresh Defender signatures must pass.");
            Require(result["SEC-AV-RTP"].Status == SecurityControlStatus.Pass, "Defender protection components must pass.");
            Require(result["SEC-AV-TAMPER"].Status == SecurityControlStatus.Pass, "Defender tamper protection must pass.");
        });

        Test("multiple active providers make primary-dependent controls unknown", () =>
        {
            var source = DefenderSource(now, 1, true, true, true, true);
            source.Products =
            [
                new("Microsoft Defender Antivirus", "Antivirus", "On", "UpToDate", null, "WSC"),
                new("Kaspersky Endpoint Security", "Antivirus", "On", "UpToDate", null, "WSC")
            ];
            source.Kaspersky = new(true, now.AddHours(-2), "12.12.0", "KESCLI");
            var result = AntivirusSecurityCollector.Collect(source, now);
            Require(result["SEC-AV-ACTIVE"].Status == SecurityControlStatus.Pass, "Known active protection must not become a false failure.");
            Require(result["SEC-AV-DEFINITIONS"].Status == SecurityControlStatus.Unknown, "Ambiguous primary definitions must be Unknown.");
            Require(result["SEC-AV-RTP"].Status == SecurityControlStatus.Unknown, "Ambiguous primary RTP must be Unknown.");
            Require(result["SEC-AV-TAMPER"].Status == SecurityControlStatus.Unknown, "Ambiguous primary tamper state must be Unknown.");
        });

        Test("missing or explicitly inactive antivirus fails active control", () =>
        {
            Require(Status(new FakeSource { Products = [] }, now, "SEC-AV-ACTIVE") == SecurityControlStatus.Fail, "No registered AV must fail.");
            foreach (var state in new[] { "Off", "Snoozed", "Expired" })
            {
                var source = new FakeSource { Products = [new("Microsoft Defender Antivirus", "Antivirus", state, "UpToDate", null, "WSC")] };
                Require(Status(source, now, "SEC-AV-ACTIVE") == SecurityControlStatus.Fail, $"{state} AV must fail active control.");
            }
        });

        Test("definition status and age are deterministic", () =>
        {
            var staleExplicit = DefenderSource(now, 1, true, true, true, true);
            staleExplicit.Products = [new("Microsoft Defender Antivirus", "Antivirus", "On", "OutOfDate", null, "WSC")];
            Require(Status(staleExplicit, now, "SEC-AV-DEFINITIONS") == SecurityControlStatus.Fail, "Explicit out-of-date signatures must fail.");
            Require(Status(DefenderSource(now, 4, true, true, true, true), now, "SEC-AV-DEFINITIONS") == SecurityControlStatus.Warn, "3-7 day signatures must warn.");
            Require(Status(DefenderSource(now, 8, true, true, true, true), now, "SEC-AV-DEFINITIONS") == SecurityControlStatus.Fail, ">7 day signatures must fail.");
        });

        Test("Defender core RTP and optional components are separated", () =>
        {
            Require(Status(DefenderSource(now, 1, false, true, true, true), now, "SEC-AV-RTP") == SecurityControlStatus.Fail, "Core Defender RTP off must fail.");
            Require(Status(DefenderSource(now, 1, true, false, true, true), now, "SEC-AV-RTP") == SecurityControlStatus.Warn, "Behavior monitor degraded with core RTP on must warn.");
            Require(Status(DefenderSource(now, 1, true, true, false, true), now, "SEC-AV-RTP") == SecurityControlStatus.Warn, "IOAV degraded with core RTP on must warn.");
        });

        Test("malformed provider response becomes unknown without throwing", () =>
        {
            var source = new FakeSource { Products = [new("Unknown AV", "Antivirus", "Unexpected", "Unexpected", null, "WSC")] };
            var result = AntivirusSecurityCollector.Collect(source, now);
            Require(result["SEC-AV-ACTIVE"].Status == SecurityControlStatus.Unknown, "Unknown product state must remain Unknown.");
        });

        Console.WriteLine($"Antivirus security self-test: {7 - failures}/7 passed.");
        return failures == 0 ? 0 : 1;
    }

    private static FakeSource DefenderSource(DateTime now, int signatureAgeDays, bool rtp, bool behavior, bool ioav, bool tamper)
        => new()
        {
            Products = [new("Microsoft Defender Antivirus", "Antivirus", "On", "UpToDate", null, "WSC")],
            Defender = new(true, rtp, behavior, ioav, tamper, signatureAgeDays, "4.18.0", "Defender")
        };

    private static SecurityControlStatus Status(IAntivirusSecuritySource source, DateTime now, string id)
        => AntivirusSecurityCollector.Collect(source, now)[id].Status;

    private sealed class FakeSource : IAntivirusSecuritySource
    {
        public IReadOnlyList<SecurityCenterProduct> Products { get; set; } = [];
        public DefenderSecurityObservation Defender { get; set; } = new(null, null, null, null, null, null, null, "Synthetic");
        public KasperskySecurityObservation Kaspersky { get; set; } = new(null, null, null, "Synthetic");
        public IReadOnlyList<SecurityCenterProduct> ReadSecurityCenterProducts() => Products;
        public DefenderSecurityObservation ReadDefender() => Defender;
        public KasperskySecurityObservation ReadKaspersky() => Kaspersky;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
