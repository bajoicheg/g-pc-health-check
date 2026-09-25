using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace G.PcHealthCheck;

internal interface IHuaweiFirmwareSecuritySource
{
    (bool? AdminPasswordSet, bool? PowerOnPasswordSet) ReadPasswordStatus();
    FirmwareBootObservation ReadBoot();
}

internal sealed class HuaweiFirmwareSecurityAdapter : IFirmwareSecurityAdapter
{
    private readonly IHuaweiFirmwareSecuritySource _source;

    public HuaweiFirmwareSecurityAdapter() : this(new HuaweiFirmwareSecuritySource()) { }

    internal HuaweiFirmwareSecurityAdapter(IHuaweiFirmwareSecuritySource source)
    {
        _source = source;
    }

    public bool Supports(string manufacturer)
        => manufacturer.Contains("Huawei", StringComparison.OrdinalIgnoreCase);

    public FirmwareSecurityObservation Collect()
    {
        (bool? AdminPasswordSet, bool? PowerOnPasswordSet) passwords;
        FirmwareBootObservation boot;
        try { passwords = _source.ReadPasswordStatus(); }
        catch { passwords = (null, null); }
        try { boot = _source.ReadBoot(); }
        catch { boot = FirmwareBootObservation.Unknown("Huawei SMBIOS/UEFI"); }

        return new(
            passwords.AdminPasswordSet,
            passwords.PowerOnPasswordSet,
            null,
            boot,
            "Huawei SMBIOS/UEFI");
    }
}

internal sealed class HuaweiFirmwareSecuritySource : IHuaweiFirmwareSecuritySource
{
    public (bool? AdminPasswordSet, bool? PowerOnPasswordSet) ReadPasswordStatus()
        => HuaweiSmbiosHardwareSecurityReader.ReadPasswordStatus();

    public FirmwareBootObservation ReadBoot()
        => HuaweiFirmwareBootReader.Read();
}

internal static class HuaweiSmbiosHardwareSecurityReader
{
    private const uint RawSmbiosProvider = 0x52534D42; // 'RSMB'
    private const byte HardwareSecurityType = 24;
    private const int RawSmbiosHeaderLength = 8;

    public static (bool? AdminPasswordSet, bool? PowerOnPasswordSet) ReadPasswordStatus()
    {
        var required = GetSystemFirmwareTable(RawSmbiosProvider, 0, IntPtr.Zero, 0);
        if (required < RawSmbiosHeaderLength)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Raw SMBIOS table is unavailable.");

        var buffer = Marshal.AllocHGlobal(checked((int)required));
        try
        {
            var written = GetSystemFirmwareTable(RawSmbiosProvider, 0, buffer, required);
            if (written != required)
                throw new InvalidDataException("Raw SMBIOS table length changed during collection.");

            var raw = new byte[written];
            Marshal.Copy(buffer, raw, 0, checked((int)written));
            return ParseRawSmbios(raw);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static (bool? AdminPasswordSet, bool? PowerOnPasswordSet) ParseHardwareSecurityByte(byte settings)
        => (
            DecodeStatus((settings >> 2) & 0b11),
            DecodeStatus((settings >> 6) & 0b11));

    internal static (bool? AdminPasswordSet, bool? PowerOnPasswordSet) ParseRawSmbios(byte[] raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Length < RawSmbiosHeaderLength)
            return (null, null);

        var tableLength = BitConverter.ToUInt32(raw, 4);
        var available = raw.Length - RawSmbiosHeaderLength;
        if (tableLength > available)
            return (null, null);

        var offset = RawSmbiosHeaderLength;
        var end = checked(RawSmbiosHeaderLength + (int)tableLength);
        while (offset + 4 <= end)
        {
            var type = raw[offset];
            var length = raw[offset + 1];
            if (length < 4 || offset + length > end)
                return (null, null);

            if (type == HardwareSecurityType && length >= 5)
                return ParseHardwareSecurityByte(raw[offset + 4]);

            if (type == 127)
                break;

            var next = offset + length;
            while (next + 1 < end && (raw[next] != 0 || raw[next + 1] != 0))
                next++;
            if (next + 1 >= end)
                break;
            offset = next + 2;
        }

        return (null, null);
    }

    private static bool? DecodeStatus(int value) => value switch
    {
        0 => false,
        1 => true,
        _ => null
    };

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetSystemFirmwareTable(
        uint firmwareTableProviderSignature,
        uint firmwareTableId,
        IntPtr firmwareTableBuffer,
        uint bufferSize);
}

internal static class HuaweiFirmwareBootReader
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(3);
    private static readonly Regex BlockSeparator = new(@"\r?\n\s*\r?\n", RegexOptions.Compiled);
    private static readonly Regex Identifier = new(
        @"\{(?:fwbootmgr|bootmgr|[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static FirmwareBootObservation Read()
    {
        var uefi = ReadUefiMode();
        if (uefi == false)
            return new(false, null, null, null, null, null, null, null, false, "GetFirmwareType");

        var output = ReadFirmwareEnumeration();
        return string.IsNullOrWhiteSpace(output)
            ? new(uefi, null, null, null, null, null, null, null, false, "Windows firmware boot manager")
            : ParseFirmwareEnumeration(output, uefi);
    }

    internal static FirmwareBootObservation ParseFirmwareEnumeration(string output, bool? uefiMode)
    {
        if (string.IsNullOrWhiteSpace(output))
            return new(uefiMode, null, null, null, null, null, null, null, false, "Windows firmware boot manager");

        var blocks = BlockSeparator.Split(output.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        var manager = blocks.FirstOrDefault(x => ContainsId(x, "{fwbootmgr}"));
        if (manager is null)
            return new(uefiMode, null, null, null, null, null, null, null, false, "Windows firmware boot manager");

        var ordered = ExtractIds(manager)
            .Where(x => !x.Equals("{fwbootmgr}", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var objects = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in blocks)
        {
            var ids = ExtractIds(block);
            if (ids.Count == 0) continue;
            var objectId = ids[0];
            if (objectId.Equals("{fwbootmgr}", StringComparison.OrdinalIgnoreCase)) continue;
            objects.TryAdd(objectId, block);
        }

        string? ObjectText(string id)
            => objects.TryGetValue(id, out var value) ? value : null;

        bool IsWindows(string id)
        {
            if (id.Equals("{bootmgr}", StringComparison.OrdinalIgnoreCase)) return true;
            var text = ObjectText(id);
            if (string.IsNullOrWhiteSpace(text)) return false;
            return text.Contains(@"\EFI\Microsoft\Boot\bootmgfw.efi", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Windows Boot Manager", StringComparison.OrdinalIgnoreCase);
        }

        bool IsUsb(string text)
            => text.Contains("USB", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Removable", StringComparison.OrdinalIgnoreCase);
        bool IsPxe(string text)
            => text.Contains("PXE", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Network", StringComparison.OrdinalIgnoreCase)
               || text.Contains("IPv4", StringComparison.OrdinalIgnoreCase)
               || text.Contains("IPv6", StringComparison.OrdinalIgnoreCase);
        bool IsOptical(string text)
            => text.Contains("DVD", StringComparison.OrdinalIgnoreCase)
               || text.Contains("CDROM", StringComparison.OrdinalIgnoreCase)
               || text.Contains("CD-ROM", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Optical", StringComparison.OrdinalIgnoreCase);
        bool IsSd(string text)
            => text.Contains("SD Card", StringComparison.OrdinalIgnoreCase)
               || text.Contains("SDCard", StringComparison.OrdinalIgnoreCase);

        bool IsExternal(string id)
        {
            var text = ObjectText(id);
            return text is not null && (IsUsb(text) || IsPxe(text) || IsOptical(text) || IsSd(text));
        }

        var objectTexts = objects.Values.ToList();
        var usb = objectTexts.Any(IsUsb) ? true : (bool?)null;
        var pxe = objectTexts.Any(IsPxe) ? true : (bool?)null;
        var optical = objectTexts.Any(IsOptical) ? true : (bool?)null;
        var sd = objectTexts.Any(IsSd) ? true : (bool?)null;
        var anyExternal = usb == true || pxe == true || optical == true || sd == true;

        bool? windowsFirst = null;
        bool? externalEffective = null;
        if (ordered.Count > 0)
        {
            var first = ordered[0];
            if (IsWindows(first))
            {
                windowsFirst = true;
                if (anyExternal) externalEffective = false;
            }
            else if (IsExternal(first))
            {
                windowsFirst = false;
                externalEffective = true;
            }
        }

        var trustedPositiveRiskEvidence = uefiMode == true
            && anyExternal
            && windowsFirst is not null
            && externalEffective is not null;

        return new(
            uefiMode,
            windowsFirst,
            usb,
            pxe,
            optical,
            sd,
            null,
            externalEffective,
            trustedPositiveRiskEvidence,
            "Windows firmware boot manager (Huawei)");
    }

    private static bool? ReadUefiMode()
    {
        if (!GetFirmwareType(out var firmwareType)) return null;
        return firmwareType switch
        {
            FirmwareTypeBios => false,
            FirmwareTypeUefi => true,
            _ => null
        };
    }

    private static string? ReadFirmwareEnumeration()
    {
        var path = Path.Combine(Environment.SystemDirectory, "bcdedit.exe");
        if (!File.Exists(path)) return null;

        var psi = new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("/enum");
        psi.ArgumentList.Add("FIRMWARE");
        psi.ArgumentList.Add("/v");

        using var process = Process.Start(psi);
        if (process is null) return null;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit((int)QueryTimeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            try { process.WaitForExit(500); } catch { }
            return null;
        }

        var output = stdout.GetAwaiter().GetResult();
        _ = stderr.GetAwaiter().GetResult();
        return process.ExitCode == 0 ? output : null;
    }

    private static bool ContainsId(string block, string id)
        => ExtractIds(block).Any(x => x.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static List<string> ExtractIds(string block)
        => Identifier.Matches(block)
            .Select(x => x.Value.ToLowerInvariant())
            .ToList();

    private const uint FirmwareTypeBios = 1;
    private const uint FirmwareTypeUefi = 2;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFirmwareType(out uint firmwareType);
}
