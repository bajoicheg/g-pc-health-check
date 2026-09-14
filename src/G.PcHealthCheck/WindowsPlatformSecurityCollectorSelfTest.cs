namespace G.PcHealthCheck;

internal static class WindowsPlatformSecurityCollectorSelfTest
{
    public static int Run()
    {
        var failures = 0;
        void Test(string name, Action body)
        {
            try { body(); Console.WriteLine($"Windows platform security self-test: PASS — {name}"); }
            catch (Exception ex) { failures++; Console.Error.WriteLine($"Windows platform security self-test: FAIL — {name}: {ex.Message}"); }
        }

        Test("firewall ownership is evaluated without false Defender failure", () =>
        {
            Require(Status(new FakeSource { Firewall = new(EffectiveFirewallProviderKind.ThirdParty, false, false, false, false, "ThirdParty") }, "SEC-FIREWALL") == SecurityControlStatus.Pass,
                "Active third-party firewall must pass even if Defender profiles are off.");
            Require(Status(new FakeSource { Firewall = new(EffectiveFirewallProviderKind.Windows, true, true, true, false, "Windows") }, "SEC-FIREWALL") == SecurityControlStatus.Pass,
                "Windows firewall with all profiles enabled must pass.");
            Require(Status(new FakeSource { Firewall = new(EffectiveFirewallProviderKind.Windows, true, false, true, false, "Windows") }, "SEC-FIREWALL") == SecurityControlStatus.Fail,
                "Disabled Windows firewall profile must fail when Windows owns firewall role.");
            Require(Status(new FakeSource { Firewall = new(EffectiveFirewallProviderKind.None, null, null, null, false, "None") }, "SEC-FIREWALL") == SecurityControlStatus.Fail,
                "No effective firewall must fail.");
            Require(Status(new FakeSource { Firewall = new(EffectiveFirewallProviderKind.Ambiguous, null, null, null, false, "Ambiguous") }, "SEC-FIREWALL") == SecurityControlStatus.Unknown,
                "Ambiguous firewall ownership must be unknown.");
        });

        Test("secure boot distinguishes enabled disabled legacy and unknown", () =>
        {
            Require(Status(new FakeSource { SecureBoot = new("Uefi", true, "Synthetic") }, "SEC-SECUREBOOT") == SecurityControlStatus.Pass, "Enabled Secure Boot must pass.");
            Require(Status(new FakeSource { SecureBoot = new("Uefi", false, "Synthetic") }, "SEC-SECUREBOOT") == SecurityControlStatus.Fail, "Disabled Secure Boot must fail.");
            Require(Status(new FakeSource { SecureBoot = new("Legacy", false, "Synthetic") }, "SEC-SECUREBOOT") == SecurityControlStatus.Fail, "Legacy firmware must fail Windows 11 baseline.");
            Require(Status(new FakeSource { SecureBoot = new("Unknown", null, "Synthetic") }, "SEC-SECUREBOOT") == SecurityControlStatus.Unknown, "Unreadable Secure Boot must be unknown.");
        });

        Test("uac EnableLUA semantics are exact", () =>
        {
            Require(Status(new FakeSource { Uac = new(true, "Synthetic") }, "SEC-UAC") == SecurityControlStatus.Pass, "Enabled UAC must pass.");
            Require(Status(new FakeSource { Uac = new(false, "Synthetic") }, "SEC-UAC") == SecurityControlStatus.Fail, "Disabled UAC must fail.");
            Require(Status(new FakeSource { Uac = new(null, "Synthetic") }, "SEC-UAC") == SecurityControlStatus.Unknown, "Unreadable UAC must be unknown.");
        });

        Test("tpm requires present ready and 2.x", () =>
        {
            Require(Status(new FakeSource { Tpm = new(true, true, "2.0", "Synthetic") }, "SEC-TPM") == SecurityControlStatus.Pass, "Ready TPM 2.x must pass.");
            Require(Status(new FakeSource { Tpm = new(true, false, "2.0", "Synthetic") }, "SEC-TPM") == SecurityControlStatus.Warn, "Present TPM not ready must warn.");
            Require(Status(new FakeSource { Tpm = new(false, false, null, "Synthetic") }, "SEC-TPM") == SecurityControlStatus.Fail, "Missing TPM must fail.");
            Require(Status(new FakeSource { Tpm = new(true, true, "1.2", "Synthetic") }, "SEC-TPM") == SecurityControlStatus.Fail, "TPM below 2.x must fail.");
            Require(Status(new FakeSource { Tpm = new(null, null, null, "Synthetic") }, "SEC-TPM") == SecurityControlStatus.Unknown, "Unresolved TPM must be unknown.");
        });

        Test("vbs hvci uses runtime state", () =>
        {
            Require(Status(new FakeSource { DeviceGuard = new(2, [2], [2], "Synthetic") }, "SEC-VBS-HVCI") == SecurityControlStatus.Pass, "Running VBS+HVCI must pass.");
            Require(Status(new FakeSource { DeviceGuard = new(2, [], [2], "Synthetic") }, "SEC-VBS-HVCI") == SecurityControlStatus.Warn, "VBS running without HVCI must warn.");
            Require(Status(new FakeSource { DeviceGuard = new(1, [], [2], "Synthetic") }, "SEC-VBS-HVCI") == SecurityControlStatus.Warn, "Configured but not running HVCI must warn.");
            Require(Status(new FakeSource { DeviceGuard = new(0, [], [], "Synthetic") }, "SEC-VBS-HVCI") == SecurityControlStatus.Fail, "Confirmed disabled VBS/HVCI must fail.");
            Require(Status(new FakeSource { DeviceGuard = new(null, [], [], "Synthetic") }, "SEC-VBS-HVCI") == SecurityControlStatus.Unknown, "Unavailable Device Guard must be unknown.");
        });

        Test("individual source failures degrade only that control to unknown", () =>
        {
            var controls = WindowsPlatformSecurityCollector.Collect(new ThrowingFirewallSource());
            Require(controls["SEC-FIREWALL"].Status == SecurityControlStatus.Unknown, "Firewall exception must become Unknown.");
            Require(controls["SEC-UAC"].Status == SecurityControlStatus.Pass, "Unrelated UAC evidence must survive firewall failure.");
        });

        Console.WriteLine($"Windows platform security self-test: {6 - failures}/6 passed.");
        return failures == 0 ? 0 : 1;
    }

    private static SecurityControlStatus Status(IWindowsPlatformSecuritySource source, string id)
        => WindowsPlatformSecurityCollector.Collect(source)[id].Status;

    private sealed class FakeSource : IWindowsPlatformSecuritySource
    {
        public FirewallObservation Firewall { get; init; } = new(EffectiveFirewallProviderKind.Windows, true, true, true, false, "Synthetic");
        public SecureBootObservation SecureBoot { get; init; } = new("Uefi", true, "Synthetic");
        public UacObservation Uac { get; init; } = new(true, "Synthetic");
        public TpmObservation Tpm { get; init; } = new(true, true, "2.0", "Synthetic");
        public DeviceGuardObservation DeviceGuard { get; init; } = new(2, [2], [2], "Synthetic");
        public FirewallObservation ReadFirewall() => Firewall;
        public SecureBootObservation ReadSecureBoot() => SecureBoot;
        public UacObservation ReadUac() => Uac;
        public TpmObservation ReadTpm() => Tpm;
        public DeviceGuardObservation ReadDeviceGuard() => DeviceGuard;
    }

    private sealed class ThrowingFirewallSource : IWindowsPlatformSecuritySource
    {
        public FirewallObservation ReadFirewall() => throw new InvalidOperationException("synthetic firewall failure");
        public SecureBootObservation ReadSecureBoot() => new("Uefi", true, "Synthetic");
        public UacObservation ReadUac() => new(true, "Synthetic");
        public TpmObservation ReadTpm() => new(true, true, "2.0", "Synthetic");
        public DeviceGuardObservation ReadDeviceGuard() => new(2, [2], [2], "Synthetic");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
