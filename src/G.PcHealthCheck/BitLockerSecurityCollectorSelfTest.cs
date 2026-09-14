using System.Text.Json;

namespace G.PcHealthCheck;

internal static class BitLockerSecurityCollectorSelfTest
{
    public static int Run()
    {
        var failures = 0;
        void Test(string name, Action body)
        {
            try { body(); Console.WriteLine($"BitLocker security self-test: PASS — {name}"); }
            catch (Exception ex) { failures++; Console.Error.WriteLine($"BitLocker security self-test: FAIL — {name}: {ex.Message}"); }
        }

        Test("OS volume protected and fully encrypted passes", () =>
        {
            var controls = Collect(Os("On", "FullyEncrypted", 100));
            Require(controls["SEC-BITLOCKER-OS"].Status == SecurityControlStatus.Pass, "Protected OS volume must pass.");
        });

        Test("OS encrypting or suspended warns", () =>
        {
            Require(Collect(Os("On", "EncryptionInProgress", 45))["SEC-BITLOCKER-OS"].Status == SecurityControlStatus.Warn, "Encrypting OS volume must warn.");
            Require(Collect(Os("Off", "FullyEncrypted", 100))["SEC-BITLOCKER-OS"].Status == SecurityControlStatus.Warn, "Suspended OS protection must warn.");
        });

        Test("OS decrypted or unprotected fails and unavailable is unknown", () =>
        {
            Require(Collect(Os("Off", "FullyDecrypted", 0))["SEC-BITLOCKER-OS"].Status == SecurityControlStatus.Fail, "Decrypted OS volume must fail.");
            Require(Collect(Os("Unknown", "Unknown", null))["SEC-BITLOCKER-OS"].Status == SecurityControlStatus.Unknown, "Unknown OS state must remain Unknown.");
            Require(BitLockerSecurityCollector.Collect(new FakeSource([]))["SEC-BITLOCKER-OS"].Status == SecurityControlStatus.Unknown, "Missing OS volume must be Unknown.");
        });

        Test("all applicable fixed data volumes protected passes", () =>
        {
            var controls = Collect(
                Os("On", "FullyEncrypted", 100),
                Data("D:", "On", "FullyEncrypted", 100),
                Data("E:", "On", "FullyEncrypted", 100));
            Require(controls["SEC-BITLOCKER-DATA"].Status == SecurityControlStatus.Pass, "All protected data volumes must pass.");
        });

        Test("data aggregate fails, warns, not-applicable and unknown truthfully", () =>
        {
            Require(Collect(Os("On", "FullyEncrypted", 100), Data("D:", "Off", "FullyDecrypted", 0))["SEC-BITLOCKER-DATA"].Status == SecurityControlStatus.Fail,
                "One unprotected data volume must fail aggregate.");
            Require(Collect(Os("On", "FullyEncrypted", 100), Data("D:", "On", "EncryptionInProgress", 50))["SEC-BITLOCKER-DATA"].Status == SecurityControlStatus.Warn,
                "Only degraded data volume must warn.");
            Require(Collect(Os("On", "FullyEncrypted", 100))["SEC-BITLOCKER-DATA"].Status == SecurityControlStatus.NotApplicable,
                "No extra fixed data volume must be NotApplicable.");
            Require(Collect(Os("On", "FullyEncrypted", 100), Data("D:", "Unknown", "Unknown", null))["SEC-BITLOCKER-DATA"].Status == SecurityControlStatus.Unknown,
                "Incomplete applicable data state must be Unknown.");
        });

        Test("EFI recovery removable and optical volumes are excluded from data aggregate", () =>
        {
            var controls = Collect(
                Os("On", "FullyEncrypted", 100),
                Excluded("EFI", "SystemSupport"),
                Excluded("Recovery", "Recovery"),
                Excluded("USB", "Removable"),
                Excluded("DVD", "Optical"));
            Require(controls["SEC-BITLOCKER-DATA"].Status == SecurityControlStatus.NotApplicable, "Excluded support/removable volumes must not become data volumes.");
        });

        Test("evidence contains protector types only and never recovery material", () =>
        {
            const string forbidden = "111111-222222-333333-444444-555555-666666-777777-888888";
            var volume = new EncryptionVolumeObservation("vol-os", "C:", true, false, "On", "FullyEncrypted", 100, "XtsAes256", ["Tpm", "NumericalPassword"], "Synthetic");
            var controls = Collect(volume);
            var json = JsonSerializer.Serialize(controls);
            Require(json.Contains("NumericalPassword", StringComparison.Ordinal), "Protector type should be retained.");
            Require(!json.Contains(forbidden, StringComparison.Ordinal), "Recovery password material must never be serialized.");
        });

        Console.WriteLine($"BitLocker security self-test: {7 - failures}/7 passed.");
        return failures == 0 ? 0 : 1;
    }

    private static Dictionary<string, SecurityControlObservation> Collect(params EncryptionVolumeObservation[] volumes)
        => BitLockerSecurityCollector.Collect(new FakeSource(volumes));

    private static EncryptionVolumeObservation Os(string protection, string conversion, int? percent)
        => new("vol-os", "C:", true, false, protection, conversion, percent, "XtsAes256", ["Tpm", "NumericalPassword"], "Synthetic");

    private static EncryptionVolumeObservation Data(string mount, string protection, string conversion, int? percent)
        => new("vol-" + mount.TrimEnd(':', '\\'), mount, false, true, protection, conversion, percent, "XtsAes256", ["NumericalPassword"], "Synthetic");

    private static EncryptionVolumeObservation Excluded(string id, string source)
        => new("vol-" + id, "", false, false, "Off", "FullyDecrypted", 0, "None", [], source);

    private sealed class FakeSource(IReadOnlyList<EncryptionVolumeObservation> values) : IBitLockerSecuritySource
    {
        public IReadOnlyList<EncryptionVolumeObservation> ReadVolumes() => values;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
