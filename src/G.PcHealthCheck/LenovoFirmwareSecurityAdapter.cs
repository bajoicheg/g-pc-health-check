using System.Management;

namespace G.PcHealthCheck;

internal interface ILenovoFirmwareSecuritySource
{
    int? ReadPasswordState();
    FirmwareBootObservation ReadBoot();
}

internal sealed class LenovoFirmwareSecurityAdapter : IFirmwareSecurityAdapter
{
    private readonly ILenovoFirmwareSecuritySource _source;

    public LenovoFirmwareSecurityAdapter() : this(new LenovoFirmwareSecuritySource()) { }

    internal LenovoFirmwareSecurityAdapter(ILenovoFirmwareSecuritySource source)
    {
        _source = source;
    }

    public bool Supports(string manufacturer)
        => manufacturer.Contains("Lenovo", StringComparison.OrdinalIgnoreCase);

    public FirmwareSecurityObservation Collect()
    {
        int? state;
        FirmwareBootObservation boot;
        try { state = _source.ReadPasswordState(); }
        catch { state = null; }
        try { boot = _source.ReadBoot(); }
        catch { boot = FirmwareBootObservation.Unknown("Lenovo WMI"); }

        bool? admin = null;
        bool? powerOn = null;
        bool? drive = null;
        if (state is >= 0)
        {
            admin = (state.Value & 0b010) != 0;
            powerOn = (state.Value & 0b001) != 0;
            drive = (state.Value & 0b100) != 0;
        }

        return new(admin, powerOn, drive, boot, "Lenovo WMI");
    }
}

internal sealed class LenovoFirmwareSecuritySource : ILenovoFirmwareSecuritySource
{
    public int? ReadPasswordState()
    {
        using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT PasswordState FROM Lenovo_BiosPasswordSettings");
        using var item = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
        if (item is null) return null;
        try { return Convert.ToInt32(item["PasswordState"]); }
        catch { return null; }
    }

    public FirmwareBootObservation ReadBoot()
    {
        var settings = ReadSettings();
        var uefi = ParseUefi(Find(settings, "UEFI/Legacy Boot", "Boot Mode", "BootMode"));
        var order = Find(settings, "BootOrder", "Boot Order", "StartupSequence");
        var windowsFirst = ParseWindowsBootManagerFirst(order);
        var usb = ParseEnabled(Find(settings, "USB Boot", "USBBoot"));
        var pxe = ParseEnabled(Find(settings, "Network Boot", "PXE Boot", "PXEBoot"));
        var optical = ParseEnabled(Find(settings, "CD/DVD Boot", "Optical Boot", "CDROM Boot"));
        var sd = ParseEnabled(Find(settings, "SD Card Boot", "SD Boot"));
        var oneTime = ParseEnabled(Find(settings, "Boot Device List F12 Option", "F12 Boot Menu", "Startup Device Menu Prompt"));
        var externalEffective = windowsFirst switch
        {
            true => false,
            false when !string.IsNullOrWhiteSpace(order) => true,
            _ => (bool?)null
        };
        var trusted = uefi is not null
            && windowsFirst is not null
            && usb is not null
            && pxe is not null
            && optical is not null
            && sd is not null
            && oneTime is not null
            && externalEffective is not null;
        return new(uefi, windowsFirst, usb, pxe, optical, sd, oneTime, externalEffective, trusted, "Lenovo WMI");
    }

    private static Dictionary<string, string> ReadSettings()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT CurrentSetting FROM Lenovo_BiosSetting");
        using var collection = searcher.Get();
        foreach (ManagementObject item in collection)
        {
            using (item)
            {
                var raw = Convert.ToString(item["CurrentSetting"]);
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var comma = raw.IndexOf(',');
                if (comma <= 0) continue;
                var name = raw[..comma].Trim();
                var value = raw[(comma + 1)..].Trim();
                if (name.Length > 0) result[name] = value;
            }
        }
        return result;
    }

    private static string? Find(IReadOnlyDictionary<string, string> settings, params string[] names)
    {
        foreach (var name in names)
            if (settings.TryGetValue(name, out var value)) return value;
        return null;
    }

    private static bool? ParseUefi(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Contains("UEFI Only", StringComparison.OrdinalIgnoreCase)) return true;
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
}
