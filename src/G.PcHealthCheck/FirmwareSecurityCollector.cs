namespace G.PcHealthCheck;

internal interface IFirmwareSecurityAdapter
{
    bool Supports(string manufacturer);
    FirmwareSecurityObservation Collect();
}

internal static class FirmwareSecurityCollector
{
    public static Dictionary<string, SecurityControlObservation> Collect(FirmwareSecurityObservation observation)
    {
        return new(StringComparer.Ordinal)
        {
            ["SEC-BIOS-ADMIN-PASSWORD"] = EvaluateAdminPassword(observation),
            ["SEC-BOOT-RESTRICTIONS"] = EvaluateBoot(observation.Boot)
        };
    }

    public static Dictionary<string, SecurityControlObservation> Collect(string manufacturer, IEnumerable<IFirmwareSecurityAdapter>? adapters = null)
    {
        adapters ??=
        [
            new LenovoFirmwareSecurityAdapter(),
            new DellFirmwareSecurityAdapter(),
            new HpFirmwareSecurityAdapter()
        ];

        var adapter = adapters.FirstOrDefault(x => x.Supports(manufacturer));
        if (adapter is null)
            return UnknownPair("UnsupportedManufacturer", string.IsNullOrWhiteSpace(manufacturer) ? "Unknown" : manufacturer, "Firmware");

        try { return Collect(adapter.Collect()); }
        catch (Exception ex) { return UnknownPair("CollectionError", ex.GetType().Name, "Firmware"); }
    }

    private static SecurityControlObservation EvaluateAdminPassword(FirmwareSecurityObservation value)
    {
        var status = value.AdminPasswordSet switch
        {
            true => SecurityControlStatus.Pass,
            false => SecurityControlStatus.Fail,
            null => SecurityControlStatus.Unknown
        };

        return new(
            "SEC-BIOS-ADMIN-PASSWORD",
            status,
            [
                new("AdminPasswordSet", Flag(value.AdminPasswordSet), value.Source),
                new("PowerOnPasswordSet", Flag(value.PowerOnPasswordSet), value.Source),
                new("DrivePasswordSet", Flag(value.DrivePasswordSet), value.Source)
            ],
            "SEC-BIOS-ADMIN-PASSWORD");
    }

    private static SecurityControlObservation EvaluateBoot(FirmwareBootObservation value)
    {
        SecurityControlStatus status;
        if (value.UefiMode == false)
            status = SecurityControlStatus.Fail;
        else if (!value.TrustedFirmwareEvidence)
            status = SecurityControlStatus.Unknown;
        else if (value.WindowsBootManagerEffective == false || value.ExternalBootEffective == true)
            status = SecurityControlStatus.Fail;
        else if (value.UefiMode != true || value.WindowsBootManagerEffective != true || value.ExternalBootEffective is null)
            status = SecurityControlStatus.Unknown;
        else
        {
            var external = new bool?[]
            {
                value.UsbBootEnabled,
                value.PxeBootEnabled,
                value.OpticalBootEnabled,
                value.SdBootEnabled,
                value.OneTimeExternalBootEnabled
            };
            status = external.Any(x => x == true)
                ? SecurityControlStatus.Warn
                : external.All(x => x == false)
                    ? SecurityControlStatus.Pass
                    : SecurityControlStatus.Unknown;
        }

        return new(
            "SEC-BOOT-RESTRICTIONS",
            status,
            [
                new("UefiMode", Flag(value.UefiMode), value.Source),
                new("WindowsBootManagerEffective", Flag(value.WindowsBootManagerEffective), value.Source),
                new("UsbBootEnabled", Flag(value.UsbBootEnabled), value.Source),
                new("PxeBootEnabled", Flag(value.PxeBootEnabled), value.Source),
                new("OpticalBootEnabled", Flag(value.OpticalBootEnabled), value.Source),
                new("SdBootEnabled", Flag(value.SdBootEnabled), value.Source),
                new("OneTimeExternalBootEnabled", Flag(value.OneTimeExternalBootEnabled), value.Source),
                new("ExternalBootEffective", Flag(value.ExternalBootEffective), value.Source),
                new("TrustedFirmwareEvidence", value.TrustedFirmwareEvidence ? "True" : "False", value.Source)
            ],
            "SEC-BOOT-RESTRICTIONS");
    }

    private static Dictionary<string, SecurityControlObservation> UnknownPair(string key, string value, string source)
    {
        IReadOnlyList<SecurityEvidence> evidence = [new SecurityEvidence(key, value, source)];
        return new(StringComparer.Ordinal)
        {
            ["SEC-BIOS-ADMIN-PASSWORD"] = new("SEC-BIOS-ADMIN-PASSWORD", SecurityControlStatus.Unknown, evidence, "SEC-BIOS-ADMIN-PASSWORD"),
            ["SEC-BOOT-RESTRICTIONS"] = new("SEC-BOOT-RESTRICTIONS", SecurityControlStatus.Unknown, evidence, "SEC-BOOT-RESTRICTIONS")
        };
    }

    private static string Flag(bool? value) => value switch
    {
        true => "True",
        false => "False",
        null => "Unknown"
    };
}
