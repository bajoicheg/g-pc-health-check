using Microsoft.Win32;
using System.Management;
using System.Runtime.InteropServices;

namespace G.PcHealthCheck;

internal interface IWindowsPlatformSecuritySource
{
    FirewallObservation ReadFirewall();
    SecureBootObservation ReadSecureBoot();
    UacObservation ReadUac();
    TpmObservation ReadTpm();
    DeviceGuardObservation ReadDeviceGuard();
}

internal static class WindowsPlatformSecurityCollector
{
    public static Dictionary<string, SecurityControlObservation> Collect(IWindowsPlatformSecuritySource? source = null)
    {
        source ??= new WindowsPlatformSecuritySource();
        var result = new Dictionary<string, SecurityControlObservation>(StringComparer.Ordinal);
        result["SEC-FIREWALL"] = Safe("SEC-FIREWALL", source.ReadFirewall, EvaluateFirewall);
        result["SEC-SECUREBOOT"] = Safe("SEC-SECUREBOOT", source.ReadSecureBoot, EvaluateSecureBoot);
        result["SEC-UAC"] = Safe("SEC-UAC", source.ReadUac, EvaluateUac);
        result["SEC-TPM"] = Safe("SEC-TPM", source.ReadTpm, EvaluateTpm);
        result["SEC-VBS-HVCI"] = Safe("SEC-VBS-HVCI", source.ReadDeviceGuard, EvaluateDeviceGuard);
        return result;
    }

    private static SecurityControlObservation Safe<T>(
        string id,
        Func<T> read,
        Func<T, SecurityControlObservation> evaluate)
    {
        try { return evaluate(read()); }
        catch (Exception ex)
        {
            return Observation(id, SecurityControlStatus.Unknown, "CollectionError", ex.GetType().Name, "Collector");
        }
    }

    private static SecurityControlObservation EvaluateFirewall(FirewallObservation value)
    {
        var status = value.EffectiveProvider switch
        {
            EffectiveFirewallProviderKind.ThirdParty => SecurityControlStatus.Pass,
            EffectiveFirewallProviderKind.None => SecurityControlStatus.Fail,
            EffectiveFirewallProviderKind.Ambiguous => SecurityControlStatus.Unknown,
            EffectiveFirewallProviderKind.Windows when value.DomainEnabled == false || value.PrivateEnabled == false || value.PublicEnabled == false
                => SecurityControlStatus.Fail,
            EffectiveFirewallProviderKind.Windows when value.DomainEnabled == true && value.PrivateEnabled == true && value.PublicEnabled == true
                => SecurityControlStatus.Pass,
            _ => SecurityControlStatus.Unknown
        };
        return new SecurityControlObservation(
            "SEC-FIREWALL", status,
            [
                new("EffectiveProvider", value.EffectiveProvider.ToString(), value.Source),
                new("DomainEnabled", Flag(value.DomainEnabled), value.Source),
                new("PrivateEnabled", Flag(value.PrivateEnabled), value.Source),
                new("PublicEnabled", Flag(value.PublicEnabled), value.Source),
                new("PolicyEnforced", value.PolicyEnforced ? "true" : "false", value.Source)
            ],
            "SEC-FIREWALL");
    }

    private static SecurityControlObservation EvaluateSecureBoot(SecureBootObservation value)
    {
        var status = value.FirmwareType.Equals("Legacy", StringComparison.OrdinalIgnoreCase)
            ? SecurityControlStatus.Fail
            : value.FirmwareType.Equals("Uefi", StringComparison.OrdinalIgnoreCase)
                ? value.Enabled switch
                {
                    true => SecurityControlStatus.Pass,
                    false => SecurityControlStatus.Fail,
                    null => SecurityControlStatus.Unknown
                }
                : SecurityControlStatus.Unknown;
        return new SecurityControlObservation(
            "SEC-SECUREBOOT", status,
            [new("FirmwareType", value.FirmwareType, value.Source), new("SecureBootEnabled", Flag(value.Enabled), value.Source)],
            "SEC-SECUREBOOT");
    }

    private static SecurityControlObservation EvaluateUac(UacObservation value)
    {
        var status = value.Enabled switch
        {
            true => SecurityControlStatus.Pass,
            false => SecurityControlStatus.Fail,
            null => SecurityControlStatus.Unknown
        };
        return Observation("SEC-UAC", status, "EnableLUA", Flag(value.Enabled), value.Source);
    }

    private static SecurityControlObservation EvaluateTpm(TpmObservation value)
    {
        SecurityControlStatus status;
        if (value.Present is null) status = SecurityControlStatus.Unknown;
        else if (value.Present == false) status = SecurityControlStatus.Fail;
        else if (value.Ready == false) status = SecurityControlStatus.Warn;
        else if (TryTpmMajor(value.SpecVersion, out var major) && major < 2) status = SecurityControlStatus.Fail;
        else if (value.Ready == true && major >= 2) status = SecurityControlStatus.Pass;
        else if (value.Present == true && TryTpmMajor(value.SpecVersion, out major) && major >= 2) status = SecurityControlStatus.Warn;
        else status = SecurityControlStatus.Unknown;

        return new SecurityControlObservation(
            "SEC-TPM", status,
            [
                new("Present", Flag(value.Present), value.Source),
                new("Ready", Flag(value.Ready), value.Source),
                new("SpecVersion", value.SpecVersion ?? "Unknown", value.Source)
            ],
            "SEC-TPM");
    }

    private static SecurityControlObservation EvaluateDeviceGuard(DeviceGuardObservation value)
    {
        var status = value.VirtualizationBasedSecurityStatus switch
        {
            null => SecurityControlStatus.Unknown,
            2 when value.SecurityServicesRunning.Contains(2) => SecurityControlStatus.Pass,
            2 => SecurityControlStatus.Warn,
            1 => SecurityControlStatus.Warn,
            0 => SecurityControlStatus.Fail,
            _ => SecurityControlStatus.Unknown
        };
        return new SecurityControlObservation(
            "SEC-VBS-HVCI", status,
            [
                new("VbsStatus", value.VirtualizationBasedSecurityStatus?.ToString() ?? "Unknown", value.Source),
                new("ServicesRunning", string.Join(",", value.SecurityServicesRunning), value.Source),
                new("ServicesConfigured", string.Join(",", value.SecurityServicesConfigured), value.Source)
            ],
            "SEC-VBS-HVCI");
    }

    private static SecurityControlObservation Observation(
        string id, SecurityControlStatus status, string key, string value, string source)
        => new(id, status, [new SecurityEvidence(key, value, source)], id);

    private static string Flag(bool? value) => value switch { true => "true", false => "false", null => "Unknown" };

    private static bool TryTpmMajor(string? raw, out int major)
    {
        major = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        foreach (var token in raw.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dot = token.IndexOf('.');
            var leading = dot >= 0 ? token[..dot] : token;
            if (int.TryParse(leading, out major)) return true;
        }
        return false;
    }
}

internal sealed class WindowsPlatformSecuritySource : IWindowsPlatformSecuritySource
{
    private const int WscSecurityProviderFirewall = 0x1;
    private const int WscHealthGood = 0;
    private const int WscHealthPoor = 2;

    public FirewallObservation ReadFirewall()
    {
        var domain = ReadFirewallProfile("DomainProfile", "DomainProfile");
        var privateProfile = ReadFirewallProfile("PrivateProfile", "StandardProfile");
        var publicProfile = ReadFirewallProfile("PublicProfile", "PublicProfile");
        var policy = domain.Policy || privateProfile.Policy || publicProfile.Policy;
        var wsc = ReadWscFirewallHealth();

        EffectiveFirewallProviderKind provider;
        if (wsc == WscHealthGood)
        {
            provider = domain.Enabled == true && privateProfile.Enabled == true && publicProfile.Enabled == true
                ? EffectiveFirewallProviderKind.Windows
                : EffectiveFirewallProviderKind.ThirdParty;
        }
        else if (wsc == WscHealthPoor && domain.Enabled == false && privateProfile.Enabled == false && publicProfile.Enabled == false)
            provider = EffectiveFirewallProviderKind.None;
        else if (wsc is null && domain.Enabled == true && privateProfile.Enabled == true && publicProfile.Enabled == true)
            provider = EffectiveFirewallProviderKind.Windows;
        else
            provider = EffectiveFirewallProviderKind.Ambiguous;

        return new FirewallObservation(provider, domain.Enabled, privateProfile.Enabled, publicProfile.Enabled, policy, "WindowsFirewall/WSC");
    }

    public SecureBootObservation ReadSecureBoot()
    {
        if (!GetFirmwareType(out var firmwareType)) return new("Unknown", null, "GetFirmwareType");
        if (firmwareType == FirmwareTypeBios) return new("Legacy", false, "GetFirmwareType");
        if (firmwareType != FirmwareTypeUefi) return new("Unknown", null, "GetFirmwareType");
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
        var raw = key?.GetValue("UEFISecureBootEnabled");
        var enabled = raw is int i ? i != 0 : raw is uint u ? u != 0 : (bool?)null;
        return new("Uefi", enabled, "Windows SecureBoot State");
    }

    public UacObservation ReadUac()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
        var raw = key?.GetValue("EnableLUA");
        return new(raw is int i ? i != 0 : raw is uint u ? u != 0 : null, "Windows Registry");
    }

    public TpmObservation ReadTpm()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\CIMV2\Security\MicrosoftTpm", "SELECT IsEnabled_InitialValue,IsActivated_InitialValue,SpecVersion FROM Win32_Tpm");
            using var item = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
            if (item is null) return new(false, false, null, "Win32_Tpm");
            var enabled = NullableBool(item["IsEnabled_InitialValue"]);
            var activated = NullableBool(item["IsActivated_InitialValue"]);
            bool? ready = enabled is null || activated is null ? null : enabled.Value && activated.Value;
            return new(true, ready, Convert.ToString(item["SpecVersion"]), "Win32_Tpm");
        }
        catch (ManagementException) { return ReadTpmViaTbs(); }
        catch (UnauthorizedAccessException) { return ReadTpmViaTbs(); }
        catch (COMException) { return ReadTpmViaTbs(); }
    }

    private static TpmObservation ReadTpmViaTbs()
    {
        try
        {
            var info = new TpmDeviceInfo { StructVersion = TpmVersion20 };
            var result = Tbsi_GetDeviceInfo((uint)Marshal.SizeOf<TpmDeviceInfo>(), ref info);
            if (result == TbsSuccess)
            {
                var version = info.TpmVersion switch
                {
                    TpmVersion12 => "1.2",
                    TpmVersion20 => "2.0",
                    _ => null
                };
                return new(true, null, version, "TBS Tbsi_GetDeviceInfo");
            }
            if (result == TbsTpmNotFound) return new(false, false, null, "TBS Tbsi_GetDeviceInfo");
            return new(null, null, null, $"TBS error 0x{result:X8}");
        }
        catch (DllNotFoundException) { return new(null, null, null, "TBS unavailable"); }
        catch (EntryPointNotFoundException) { return new(null, null, null, "TBS unavailable"); }
    }

    public DeviceGuardObservation ReadDeviceGuard()
    {
        using var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\DeviceGuard", "SELECT VirtualizationBasedSecurityStatus,SecurityServicesRunning,SecurityServicesConfigured FROM Win32_DeviceGuard");
        using var item = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
        if (item is null) return new(null, [], [], "Win32_DeviceGuard");
        return new(
            NullableInt(item["VirtualizationBasedSecurityStatus"]),
            IntArray(item["SecurityServicesRunning"]),
            IntArray(item["SecurityServicesConfigured"]),
            "Win32_DeviceGuard");
    }

    private static (bool? Enabled, bool Policy) ReadFirewallProfile(string policyProfile, string localProfile)
    {
        var policyPath = @"SOFTWARE\Policies\Microsoft\WindowsFirewall\" + policyProfile;
        using (var policyKey = Registry.LocalMachine.OpenSubKey(policyPath))
        {
            var policyValue = ToBool(policyKey?.GetValue("EnableFirewall"));
            if (policyValue is not null) return (policyValue, true);
        }

        var localPath = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\" + localProfile;
        using var localKey = Registry.LocalMachine.OpenSubKey(localPath);
        return (ToBool(localKey?.GetValue("EnableFirewall")), false);
    }

    private static int? ReadWscFirewallHealth()
    {
        try
        {
            var hr = WscGetSecurityProviderHealth(WscSecurityProviderFirewall, out var health);
            return hr >= 0 ? health : null;
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
    }

    private static bool? ToBool(object? value) => value switch
    {
        int i => i != 0,
        uint u => u != 0,
        bool b => b,
        _ => null
    };

    private static bool? NullableBool(object? value)
    {
        if (value is null) return null;
        try { return Convert.ToBoolean(value); } catch { return null; }
    }

    private static int? NullableInt(object? value)
    {
        if (value is null) return null;
        try { return Convert.ToInt32(value); } catch { return null; }
    }

    private static IReadOnlyList<int> IntArray(object? value)
    {
        if (value is not Array array) return [];
        var result = new List<int>();
        foreach (var item in array)
        {
            try { result.Add(Convert.ToInt32(item)); } catch { }
        }
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TpmDeviceInfo
    {
        public uint StructVersion;
        public uint TpmVersion;
        public uint TpmInterfaceType;
        public uint TpmImpRevision;
    }

    private const uint TbsSuccess = 0;
    private const uint TbsTpmNotFound = 0x8028400F;
    private const uint TpmVersion12 = 1;
    private const uint TpmVersion20 = 2;
    private const uint FirmwareTypeBios = 1;
    private const uint FirmwareTypeUefi = 2;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFirmwareType(out uint firmwareType);

    [DllImport("wscapi.dll")]
    private static extern int WscGetSecurityProviderHealth(int providers, out int health);

    [DllImport("tbs.dll")]
    private static extern uint Tbsi_GetDeviceInfo(uint size, ref TpmDeviceInfo info);
}
