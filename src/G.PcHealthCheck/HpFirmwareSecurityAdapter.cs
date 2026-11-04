using System.Management;

namespace G.PcHealthCheck;

internal interface IHpFirmwareSecuritySource
{
    bool IsProviderAvailable();
    bool? ReadAdminPasswordSet();
    FirmwareBootObservation ReadBoot();
}

internal sealed class HpFirmwareSecurityAdapter : IFirmwareSecurityAdapter
{
    private readonly IHpFirmwareSecuritySource _source;

    public HpFirmwareSecurityAdapter() : this(new HpFirmwareSecuritySource()) { }

    internal HpFirmwareSecurityAdapter(IHpFirmwareSecuritySource source)
    {
        _source = source;
    }

    public bool Supports(string manufacturer)
        => manufacturer.Contains("Hewlett", StringComparison.OrdinalIgnoreCase)
            || manufacturer.Equals("HP", StringComparison.OrdinalIgnoreCase)
            || manufacturer.StartsWith("HP ", StringComparison.OrdinalIgnoreCase);

    public FirmwareSecurityObservation Collect()
    {
        bool available;
        try { available = _source.IsProviderAvailable(); }
        catch { available = false; }
        if (!available)
            return new(null, null, null, FirmwareBootObservation.Unknown("HP InstrumentedBIOS"), "HP InstrumentedBIOS");

        bool? admin;
        FirmwareBootObservation boot;
        try { admin = _source.ReadAdminPasswordSet(); }
        catch { admin = null; }
        try { boot = _source.ReadBoot(); }
        catch { boot = FirmwareBootObservation.Unknown("HP InstrumentedBIOS"); }
        return new(admin, null, null, boot, "HP InstrumentedBIOS");
    }
}

internal sealed class HpFirmwareSecuritySource : IHpFirmwareSecuritySource
{
    private const string NamespacePath = @"root\HP\InstrumentedBIOS";

    public bool IsProviderAvailable()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(NamespacePath, "SELECT * FROM HP_BIOSPassword");
            using var collection = searcher.Get();
            return collection.Cast<ManagementObject>().Any();
        }
        catch { return false; }
    }

    public bool? ReadAdminPasswordSet()
    {
        using var searcher = new ManagementObjectSearcher(NamespacePath, "SELECT * FROM HP_BIOSPassword");
        using var collection = searcher.Get();
        foreach (ManagementObject item in collection)
        {
            using (item)
            {
                var name = Property(item, "Name") ?? Property(item, "InstanceName") ?? "";
                if (!IsSetupPasswordName(name)) continue;
                return ToBool(item.Properties["IsSet"]?.Value)
                    ?? ToBool(item.Properties["Set"]?.Value);
            }
        }
        return null;
    }

    public FirmwareBootObservation ReadBoot()
    {
        var settings = ReadEnumerationSettings();
        var mode = FindByName(settings, "UEFI Boot Mode", "Legacy Support", "Boot Mode");
        var sequence = FindContaining(settings, "UEFI", "Boot Order")
            ?? FindContaining(settings, "Boot Order");
        var uefi = ParseUefi(mode);
        var windowsFirst = ParseWindowsBootManagerFirst(sequence);
        var usb = ParseEnabled(FindContaining(settings, "USB", "Boot"));
        var pxe = ParseEnabled(FindContaining(settings, "Network", "Boot"))
            ?? ParseEnabled(FindContaining(settings, "PXE"));
        var optical = ParseEnabled(FindContaining(settings, "CD", "Boot"))
            ?? ParseEnabled(FindContaining(settings, "DVD", "Boot"))
            ?? ParseEnabled(FindContaining(settings, "Optical", "Boot"));
        var sd = ParseEnabled(FindContaining(settings, "SD", "Boot"));
        var oneTime = ParseEnabled(FindContaining(settings, "One-Time", "Boot"))
            ?? ParseEnabled(FindContaining(settings, "F9", "Boot"));
        var externalEffective = windowsFirst switch
        {
            true => false,
            false when !string.IsNullOrWhiteSpace(sequence) => true,
            _ => (bool?)null
        };
        var trusted = uefi is not null && windowsFirst is not null
            && usb is not null && pxe is not null && optical is not null
            && sd is not null && oneTime is not null && externalEffective is not null;
        return new(uefi, windowsFirst, usb, pxe, optical, sd, oneTime, externalEffective, trusted, "HP InstrumentedBIOS");
    }

    private static Dictionary<string, string> ReadEnumerationSettings()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var searcher = new ManagementObjectSearcher(NamespacePath, "SELECT * FROM HP_BIOSEnumeration");
        using var collection = searcher.Get();
        foreach (ManagementObject item in collection)
        {
            using (item)
            {
                var name = Property(item, "Name");
                var value = Property(item, "CurrentValue");
                if (!string.IsNullOrWhiteSpace(name) && value is not null) result[name] = value;
            }
        }
        return result;
    }

    private static bool IsSetupPasswordName(string value)
        => value.Contains("Setup", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Administrator", StringComparison.OrdinalIgnoreCase);

    private static string? Property(ManagementObject item, string name)
        => Convert.ToString(item.Properties[name]?.Value);

    private static string? FindByName(IReadOnlyDictionary<string, string> settings, params string[] names)
    {
        foreach (var name in names)
            if (settings.TryGetValue(name, out var value)) return value;
        return null;
    }

    private static string? FindContaining(IReadOnlyDictionary<string, string> settings, params string[] fragments)
        => settings.FirstOrDefault(x => fragments.All(f => x.Key.Contains(f, StringComparison.OrdinalIgnoreCase))).Value;

    private static bool? ParseUefi(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Contains("UEFI", StringComparison.OrdinalIgnoreCase) && !value.Contains("Legacy", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Contains("Legacy", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    private static bool? ParseWindowsBootManagerFirst(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var tokens = value.Split([':', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return null;
        return tokens[0].Contains("Windows Boot Manager", StringComparison.OrdinalIgnoreCase)
            || tokens[0].Contains("WindowsBootManager", StringComparison.OrdinalIgnoreCase);
    }

    private static bool? ParseEnabled(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Contains("Disable", StringComparison.OrdinalIgnoreCase) || value.Equals("Off", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.Contains("Enable", StringComparison.OrdinalIgnoreCase) || value.Equals("On", StringComparison.OrdinalIgnoreCase)) return true;
        return null;
    }

    private static bool? ToBool(object? value) => value switch
    {
        bool b => b,
        int i => i != 0,
        uint u => u != 0,
        string s when bool.TryParse(s, out var b) => b,
        string s when int.TryParse(s, out var i) => i != 0,
        _ => null
    };
}
