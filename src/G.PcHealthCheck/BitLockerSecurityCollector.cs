using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
namespace G.PcHealthCheck;

internal interface IBitLockerSecuritySource
{
    IReadOnlyList<EncryptionVolumeObservation> ReadVolumes();
}

internal static class BitLockerSecurityCollector
{
    public static Dictionary<string, SecurityControlObservation> Collect(IBitLockerSecuritySource? source = null)
    {
        source ??= new BitLockerSecuritySource();
        IReadOnlyList<EncryptionVolumeObservation> volumes;
        try { volumes = source.ReadVolumes(); }
        catch (Exception ex)
        {
            var evidence = new[] { new SecurityEvidence("CollectionError", ex.GetType().Name, "BitLocker") };
            return new(StringComparer.Ordinal)
            {
                ["SEC-BITLOCKER-OS"] = new("SEC-BITLOCKER-OS", SecurityControlStatus.Unknown, evidence, "SEC-BITLOCKER-OS"),
                ["SEC-BITLOCKER-DATA"] = new("SEC-BITLOCKER-DATA", SecurityControlStatus.Unknown, evidence, "SEC-BITLOCKER-DATA")
            };
        }

        var os = volumes.FirstOrDefault(x => x.IsOsVolume);
        var osStatus = os is null ? SecurityControlStatus.Unknown : EvaluateVolume(os);
        IReadOnlyList<SecurityEvidence> osEvidence = os is null
            ? new[] { new SecurityEvidence("OsVolume", "NotObserved", "BitLocker") }
            : EvidenceFor(os);

        var data = volumes.Where(x => x.IsApplicableFixedData).ToList();
        SecurityControlStatus dataStatus;
        if (data.Count == 0) dataStatus = SecurityControlStatus.NotApplicable;
        else
        {
            var statuses = data.Select(EvaluateVolume).ToList();
            dataStatus = statuses.Contains(SecurityControlStatus.Fail) ? SecurityControlStatus.Fail
                : statuses.Contains(SecurityControlStatus.Unknown) ? SecurityControlStatus.Unknown
                : statuses.Contains(SecurityControlStatus.Warn) ? SecurityControlStatus.Warn
                : SecurityControlStatus.Pass;
        }
        IReadOnlyList<SecurityEvidence> dataEvidence = data.Count == 0
            ? new[] { new SecurityEvidence("FixedDataVolumes", "0", "BitLocker") }
            : data.SelectMany(EvidenceFor).ToList();

        return new(StringComparer.Ordinal)
        {
            ["SEC-BITLOCKER-OS"] = new("SEC-BITLOCKER-OS", osStatus, osEvidence, "SEC-BITLOCKER-OS"),
            ["SEC-BITLOCKER-DATA"] = new("SEC-BITLOCKER-DATA", dataStatus, dataEvidence, "SEC-BITLOCKER-DATA")
        };
    }

    internal static SecurityControlStatus EvaluateVolume(EncryptionVolumeObservation volume)
    {
        if (volume.ProtectionStatus.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            return SecurityControlStatus.Unknown;

        if (volume.ConversionStatus.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            return volume.ProtectionStatus.Equals("On", StringComparison.OrdinalIgnoreCase)
                || volume.ProtectionStatus.Equals("Off", StringComparison.OrdinalIgnoreCase)
                ? SecurityControlStatus.Warn
                : SecurityControlStatus.Unknown;

        if (volume.ConversionStatus is "EncryptionInProgress" or "EncryptionPaused" or "DecryptionInProgress" or "DecryptionPaused")
            return SecurityControlStatus.Warn;

        if (volume.ConversionStatus.Equals("FullyDecrypted", StringComparison.OrdinalIgnoreCase))
            return SecurityControlStatus.Fail;

        if (volume.ConversionStatus.Equals("FullyEncrypted", StringComparison.OrdinalIgnoreCase))
        {
            if (volume.ProtectionStatus.Equals("On", StringComparison.OrdinalIgnoreCase)) return SecurityControlStatus.Pass;
            if (volume.ProtectionStatus.Equals("Off", StringComparison.OrdinalIgnoreCase)) return SecurityControlStatus.Warn;
        }
        return SecurityControlStatus.Unknown;
    }

    private static IReadOnlyList<SecurityEvidence> EvidenceFor(EncryptionVolumeObservation volume)
    {
        var label = string.IsNullOrWhiteSpace(volume.MountPoint) ? volume.VolumeId : volume.MountPoint;
        return
        [
            new("Volume", label, volume.Source),
            new("ProtectionStatus", volume.ProtectionStatus, volume.Source),
            new("ConversionStatus", volume.ConversionStatus, volume.Source),
            new("EncryptionPercent", volume.EncryptionPercent?.ToString() ?? "Unknown", volume.Source),
            new("EncryptionMethod", volume.EncryptionMethod, volume.Source),
            new("ProtectorTypes", string.Join(",", volume.ProtectorTypes), volume.Source)
        ];
    }
}

internal sealed class BitLockerSecuritySource : IBitLockerSecuritySource
{
    public IReadOnlyList<EncryptionVolumeObservation> ReadVolumes()
    {
        try { return ReadVolumesViaWmi(); }
        catch (ManagementException) { return ReadVolumesViaManageBde(); }
        catch (UnauthorizedAccessException) { return ReadVolumesViaManageBde(); }
        catch (COMException) { return ReadVolumesViaManageBde(); }
    }

    private static IReadOnlyList<EncryptionVolumeObservation> ReadVolumesViaWmi()
    {
        var volumeMetadata = ReadVolumeMetadata();
        var systemDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))?.TrimEnd('\\') ?? "";
        var result = new List<EncryptionVolumeObservation>();
        using var searcher = new ManagementObjectSearcher(@"root\CIMV2\Security\MicrosoftVolumeEncryption",
            "SELECT DeviceID,DriveLetter,VolumeType,ProtectionStatus,ConversionStatus,EncryptionPercentage,EncryptionMethod FROM Win32_EncryptableVolume");
        using var collection = searcher.Get();
        foreach (ManagementObject item in collection)
        {
            using (item)
            {
                var id = Convert.ToString(item["DeviceID"]) ?? "";
                var mount = (Convert.ToString(item["DriveLetter"]) ?? "").TrimEnd('\\');
                var volumeType = ToInt(item["VolumeType"]);
                volumeMetadata.TryGetValue(id, out var metadata);
                var isOs = volumeType == 0
                    || (!string.IsNullOrWhiteSpace(mount) && mount.Equals(systemDrive, StringComparison.OrdinalIgnoreCase))
                    || metadata?.BootVolume == true;
                var isFixedData = !isOs && volumeType == 1 && IsApplicableDataVolume(mount, metadata);
                result.Add(new EncryptionVolumeObservation(
                    id,
                    mount,
                    isOs,
                    isFixedData,
                    MapProtection(ToInt(item["ProtectionStatus"])),
                    MapConversion(ToInt(item["ConversionStatus"])),
                    NullableInt(item["EncryptionPercentage"]),
                    MapEncryptionMethod(ToInt(item["EncryptionMethod"])),
                    ReadProtectorTypes(item),
                    "Win32_EncryptableVolume"));
            }
        }
        return result;
    }


    private static IReadOnlyList<EncryptionVolumeObservation> ReadVolumesViaManageBde()
    {
        var windowsDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))?.TrimEnd('\\') ?? "";
        var result = new List<EncryptionVolumeObservation>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                var mount = drive.Name.TrimEnd('\\');
                var isOs = mount.Equals(windowsDrive, StringComparison.OrdinalIgnoreCase);
                var protection = ReadManageBdeProtection(mount);
                result.Add(new EncryptionVolumeObservation(
                    mount,
                    mount,
                    isOs,
                    !isOs,
                    protection,
                    "Unknown",
                    null,
                    "Unknown",
                    [],
                    "manage-bde protection status"));
            }
            catch { }
        }
        return result;
    }

    private static string ReadManageBdeProtection(string mount)
    {
        var path = Path.Combine(Environment.SystemDirectory, "manage-bde.exe");
        if (!File.Exists(path)) return "Unknown";
        var psi = new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-status");
        psi.ArgumentList.Add(mount);
        psi.ArgumentList.Add("-protectionaserrorlevel");
        try
        {
            using var process = Process.Start(psi);
            if (process is null) return "Unknown";
            if (!process.WaitForExit(3000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return "Unknown";
            }
            return process.ExitCode switch { 0 => "On", 1 => "Off", _ => "Unknown" };
        }
        catch { return "Unknown"; }
    }

    private sealed record VolumeMetadata(int DriveType, bool BootVolume, bool SystemVolume, string Label);

    private static Dictionary<string, VolumeMetadata> ReadVolumeMetadata()
    {
        var result = new Dictionary<string, VolumeMetadata>(StringComparer.OrdinalIgnoreCase);
        using var searcher = new ManagementObjectSearcher("root\\CIMV2",
            "SELECT DeviceID,DriveType,BootVolume,SystemVolume,Label FROM Win32_Volume");
        using var collection = searcher.Get();
        foreach (ManagementObject item in collection)
        {
            using (item)
            {
                var id = Convert.ToString(item["DeviceID"]) ?? "";
                if (string.IsNullOrWhiteSpace(id)) continue;
                result[id] = new VolumeMetadata(
                    ToInt(item["DriveType"]),
                    ToBool(item["BootVolume"]),
                    ToBool(item["SystemVolume"]),
                    Convert.ToString(item["Label"]) ?? "");
            }
        }
        return result;
    }

    private static bool IsApplicableDataVolume(string mount, VolumeMetadata? metadata)
    {
        if (metadata is not null)
        {
            if (metadata.DriveType != 3 || metadata.BootVolume || metadata.SystemVolume) return false;
            if (IsSupportLabel(metadata.Label)) return false;
            return true;
        }
        return !string.IsNullOrWhiteSpace(mount);
    }

    private static bool IsSupportLabel(string label)
        => label.Contains("Recovery", StringComparison.OrdinalIgnoreCase)
            || label.Contains("OEM", StringComparison.OrdinalIgnoreCase)
            || label.Contains("EFI", StringComparison.OrdinalIgnoreCase)
            || label.Contains("System Reserved", StringComparison.OrdinalIgnoreCase)
            || label.Contains("Зарезервировано системой", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ReadProtectorTypes(ManagementObject volume)
    {
        var result = new List<string>();
        try
        {
            using var input = volume.GetMethodParameters("GetKeyProtectors");
            input["KeyProtectorType"] = 0u;
            using var output = volume.InvokeMethod("GetKeyProtectors", input, null);
            if (output?["VolumeKeyProtectorID"] is not string[] ids) return result;
            foreach (var id in ids)
            {
                try
                {
                    using var typeInput = volume.GetMethodParameters("GetKeyProtectorType");
                    typeInput["VolumeKeyProtectorID"] = id;
                    using var typeOutput = volume.InvokeMethod("GetKeyProtectorType", typeInput, null);
                    result.Add(MapProtectorType(ToInt(typeOutput?["KeyProtectorType"])));
                }
                catch { result.Add("Unknown"); }
            }
        }
        catch { }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string MapProtection(int value) => value switch { 0 => "Off", 1 => "On", _ => "Unknown" };
    private static string MapConversion(int value) => value switch
    {
        0 => "FullyDecrypted",
        1 => "FullyEncrypted",
        2 => "EncryptionInProgress",
        3 => "DecryptionInProgress",
        4 => "EncryptionPaused",
        5 => "DecryptionPaused",
        _ => "Unknown"
    };
    private static string MapEncryptionMethod(int value) => value switch
    {
        0 => "None",
        1 => "Aes128Diffuser",
        2 => "Aes256Diffuser",
        3 => "Aes128",
        4 => "Aes256",
        6 => "XtsAes128",
        7 => "XtsAes256",
        _ => "Unknown"
    };
    private static string MapProtectorType(int value) => value switch
    {
        1 => "Tpm",
        2 => "ExternalKey",
        3 => "NumericalPassword",
        4 => "TpmAndPin",
        5 => "TpmAndStartupKey",
        6 => "TpmAndPinAndStartupKey",
        7 => "PublicKey",
        8 => "Passphrase",
        9 => "TpmCertificate",
        10 => "SecurityIdentifier",
        _ => "Unknown"
    };

    private static int ToInt(object? value)
    {
        try { return Convert.ToInt32(value); } catch { return -1; }
    }
    private static int? NullableInt(object? value)
    {
        if (value is null) return null;
        try { return Convert.ToInt32(value); } catch { return null; }
    }
    private static bool ToBool(object? value)
    {
        try { return Convert.ToBoolean(value); } catch { return false; }
    }
}
