using System.Reflection;

namespace G.PcHealthCheck;

internal static class FirmwareSecurityCollectorSelfTest
{
    public static int Run()
    {
        var failures = 0;
        void Test(string name, Action body)
        {
            try { body(); Console.WriteLine($"Firmware security self-test: PASS — {name}"); }
            catch (Exception ex) { failures++; Console.Error.WriteLine($"Firmware security self-test: FAIL — {name}: {ex.Message}"); }
        }

        Test("Lenovo PasswordState maps admin, power-on and drive password bits", () =>
        {
            var admin = new LenovoFirmwareSecurityAdapter(new FakeLenovoSource(0b010, UnknownBoot("Lenovo WMI"))).Collect();
            Require(admin.AdminPasswordSet == true, "Lenovo BIT1 must mean admin/supervisor password set.");
            Require(admin.PowerOnPasswordSet == false, "Lenovo BIT0 must remain supplemental power-on/user password evidence.");
            Require(admin.DrivePasswordSet == false, "Lenovo BIT2 must remain supplemental drive-password evidence.");

            var supplemental = new LenovoFirmwareSecurityAdapter(new FakeLenovoSource(0b101, UnknownBoot("Lenovo WMI"))).Collect();
            Require(supplemental.AdminPasswordSet == false, "Missing Lenovo BIT1 must positively report admin password absent when PasswordState is valid.");
            Require(supplemental.PowerOnPasswordSet == true, "Lenovo BIT0 supplemental evidence drifted.");
            Require(supplemental.DrivePasswordSet == true, "Lenovo BIT2 supplemental evidence drifted.");

            var missing = new LenovoFirmwareSecurityAdapter(new FakeLenovoSource(0, UnknownBoot("Lenovo WMI"))).Collect();
            Require(missing.AdminPasswordSet == false, "Lenovo PasswordState 0 must report admin password absent.");

            var malformed = new LenovoFirmwareSecurityAdapter(new FakeLenovoSource(-1, UnknownBoot("Lenovo WMI"))).Collect();
            Require(malformed.AdminPasswordSet is null, "Malformed Lenovo PasswordState must remain Unknown.");
        });

        Test("Dell and HP require an already-present supported provider", () =>
        {
            var dellPresent = new DellFirmwareSecurityAdapter(new FakeDellSource(true, true, UnknownBoot("Dell BIOS Provider"))).Collect();
            Require(dellPresent.AdminPasswordSet == true, "Present Dell provider must surface admin-password state.");
            var dellMissing = new DellFirmwareSecurityAdapter(new FakeDellSource(false, true, UnknownBoot("Dell BIOS Provider"))).Collect();
            Require(dellMissing.AdminPasswordSet is null, "Missing Dell provider must be Unknown.");

            var hpPresent = new HpFirmwareSecurityAdapter(new FakeHpSource(true, false, UnknownBoot("HP InstrumentedBIOS"))).Collect();
            Require(hpPresent.AdminPasswordSet == false, "Present HP provider must surface setup-password state.");
            var hpMissing = new HpFirmwareSecurityAdapter(new FakeHpSource(false, false, UnknownBoot("HP InstrumentedBIOS"))).Collect();
            Require(hpMissing.AdminPasswordSet is null, "Missing HP provider must be Unknown.");

            Require(!HasDeclaredInstallMethod(typeof(DellFirmwareSecurityAdapter)), "Dell adapter must not install provider/tooling.");
            Require(!HasDeclaredInstallMethod(typeof(HpFirmwareSecurityAdapter)), "HP adapter must not install provider/tooling.");
            Require(!HasDeclaredInstallMethod(typeof(LenovoFirmwareSecurityAdapter)), "Lenovo adapter must not install provider/tooling.");
        });

        Test("firmware password control uses admin password only", () =>
        {
            var pass = Collect(new FirmwareSecurityObservation(true, false, false, UnknownBoot("Synthetic"), "Synthetic"));
            Require(pass["SEC-BIOS-ADMIN-PASSWORD"].Status == SecurityControlStatus.Pass, "Confirmed admin password must pass.");

            var fail = Collect(new FirmwareSecurityObservation(false, true, true, UnknownBoot("Synthetic"), "Synthetic"));
            Require(fail["SEC-BIOS-ADMIN-PASSWORD"].Status == SecurityControlStatus.Fail, "Power-on/HDD passwords must not substitute for missing admin password.");
            Require(fail["SEC-BIOS-ADMIN-PASSWORD"].Evidence.Any(x => x.Key == "PowerOnPasswordSet" && x.Value == "True"), "Power-on password must remain supplemental evidence.");
            Require(fail["SEC-BIOS-ADMIN-PASSWORD"].Evidence.Any(x => x.Key == "DrivePasswordSet" && x.Value == "True"), "Drive password must remain supplemental evidence.");

            var unknown = Collect(new FirmwareSecurityObservation(null, null, null, UnknownBoot("Synthetic"), "Synthetic"));
            Require(unknown["SEC-BIOS-ADMIN-PASSWORD"].Status == SecurityControlStatus.Unknown, "Unavailable password state must remain Unknown.");
        });

        Test("boot restriction pass requires trusted internal-only firmware evidence", () =>
        {
            var controls = Collect(new FirmwareSecurityObservation(null, null, null,
                new FirmwareBootObservation(true, true, false, false, false, false, false, false, true, "Synthetic OEM"), "Synthetic OEM"));
            Require(controls["SEC-BOOT-RESTRICTIONS"].Status == SecurityControlStatus.Pass,
                "UEFI + effective Windows Boot Manager + all external paths disabled must pass.");
        });

        Test("internal first with USB or PXE still enabled warns", () =>
        {
            var usb = Collect(new FirmwareSecurityObservation(null, null, null,
                new FirmwareBootObservation(true, true, true, false, false, false, false, false, true, "Synthetic OEM"), "Synthetic OEM"));
            Require(usb["SEC-BOOT-RESTRICTIONS"].Status == SecurityControlStatus.Warn, "Enabled USB path after internal boot must warn.");

            var pxe = Collect(new FirmwareSecurityObservation(null, null, null,
                new FirmwareBootObservation(true, true, false, true, false, false, false, false, true, "Synthetic OEM"), "Synthetic OEM"));
            Require(pxe["SEC-BOOT-RESTRICTIONS"].Status == SecurityControlStatus.Warn, "Enabled PXE path after internal boot must warn.");
        });

        Test("external effective path or legacy boot fails", () =>
        {
            var external = Collect(new FirmwareSecurityObservation(null, null, null,
                new FirmwareBootObservation(true, true, true, false, false, false, false, true, true, "Synthetic OEM"), "Synthetic OEM"));
            Require(external["SEC-BOOT-RESTRICTIONS"].Status == SecurityControlStatus.Fail, "Effective external boot path must fail.");

            var legacy = Collect(new FirmwareSecurityObservation(null, null, null,
                new FirmwareBootObservation(false, false, null, null, null, null, null, null, false, "Synthetic OEM"), "Synthetic OEM"));
            Require(legacy["SEC-BOOT-RESTRICTIONS"].Status == SecurityControlStatus.Fail, "Legacy firmware mode must fail.");
        });

        Test("BCD-only or partial boot evidence stays Unknown", () =>
        {
            var bcdOnly = Collect(new FirmwareSecurityObservation(null, null, null,
                new FirmwareBootObservation(true, true, null, null, null, null, null, null, false, "Windows BCD"), "Windows BCD"));
            Require(bcdOnly["SEC-BOOT-RESTRICTIONS"].Status == SecurityControlStatus.Unknown,
                "BCD-only evidence must never prove external boot is disabled.");
        });

        Test("firmware adapter API cannot accept or return password secrets", () =>
        {
            foreach (var type in new[] { typeof(LenovoFirmwareSecurityAdapter), typeof(DellFirmwareSecurityAdapter), typeof(HpFirmwareSecurityAdapter) })
            {
                foreach (var ctor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    AssertNoSecretParameters(type, ctor.GetParameters());

                foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    AssertNoSecretParameters(type, method.GetParameters());
                    Require(method.ReturnType != typeof(System.Security.SecureString), $"{type.Name}.{method.Name} must not return SecureString.");
                    Require(method.ReturnType != typeof(byte[]), $"{type.Name}.{method.Name} must not return raw secret bytes.");
                    Require(method.ReturnType != typeof(char[]), $"{type.Name}.{method.Name} must not return raw secret chars.");
                }
            }
        });

        Console.WriteLine($"Firmware security self-test: {8 - failures}/8 passed.");
        return failures == 0 ? 0 : 1;
    }

    private static Dictionary<string, SecurityControlObservation> Collect(FirmwareSecurityObservation observation)
        => FirmwareSecurityCollector.Collect(observation);

    private static FirmwareBootObservation UnknownBoot(string source)
        => new(null, null, null, null, null, null, null, null, false, source);

    private static bool HasDeclaredInstallMethod(Type type)
        => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Any(x => x.Name.Contains("Install", StringComparison.OrdinalIgnoreCase));

    private static void AssertNoSecretParameters(Type type, ParameterInfo[] parameters)
    {
        foreach (var parameter in parameters)
        {
            var name = parameter.Name ?? "";
            Require(!IsSecretParameterName(name), $"{type.Name} exposes secret-like parameter '{name}'.");
            Require(parameter.ParameterType != typeof(System.Security.SecureString), $"{type.Name} must not accept SecureString.");
            Require(parameter.ParameterType != typeof(byte[]), $"{type.Name} must not accept raw secret bytes.");
            Require(parameter.ParameterType != typeof(char[]), $"{type.Name} must not accept raw secret chars.");
        }
    }

    private static bool IsSecretParameterName(string name)
        => name.Equals("password", StringComparison.OrdinalIgnoreCase)
            || name.Equals("currentPassword", StringComparison.OrdinalIgnoreCase)
            || name.Equals("newPassword", StringComparison.OrdinalIgnoreCase)
            || name.Equals("secret", StringComparison.OrdinalIgnoreCase)
            || name.Equals("credential", StringComparison.OrdinalIgnoreCase)
            || name.Equals("recoveryKey", StringComparison.OrdinalIgnoreCase);

    private sealed class FakeLenovoSource(int? passwordState, FirmwareBootObservation boot) : ILenovoFirmwareSecuritySource
    {
        public int? ReadPasswordState() => passwordState;
        public FirmwareBootObservation ReadBoot() => boot;
    }

    private sealed class FakeDellSource(bool available, bool? adminPasswordSet, FirmwareBootObservation boot) : IDellFirmwareSecuritySource
    {
        public bool IsProviderAvailable() => available;
        public bool? ReadAdminPasswordSet() => adminPasswordSet;
        public FirmwareBootObservation ReadBoot() => boot;
    }

    private sealed class FakeHpSource(bool available, bool? adminPasswordSet, FirmwareBootObservation boot) : IHpFirmwareSecuritySource
    {
        public bool IsProviderAvailable() => available;
        public bool? ReadAdminPasswordSet() => adminPasswordSet;
        public FirmwareBootObservation ReadBoot() => boot;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
