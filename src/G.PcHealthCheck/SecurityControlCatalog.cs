namespace G.PcHealthCheck;

internal sealed record SecurityControlDescriptor(string Id, int Weight, SecurityBand? FailBandCap = null);

internal static class SecurityControlCatalog
{
    public static IReadOnlyList<SecurityControlDescriptor> All { get; } =
    [
        new("SEC-AV-ACTIVE", 9, SecurityBand.Low),
        new("SEC-AV-PLATFORM", 2),
        new("SEC-AV-DEFINITIONS", 8),
        new("SEC-AV-RTP", 8),
        new("SEC-AV-TAMPER", 3),
        new("SEC-FIREWALL", 11, SecurityBand.Low),
        new("SEC-OS-UPDATES", 10, SecurityBand.NeedsAttention),
        new("SEC-BITLOCKER-OS", 8, SecurityBand.Low),
        new("SEC-BITLOCKER-DATA", 5, SecurityBand.NeedsAttention),
        new("SEC-SECUREBOOT", 6),
        new("SEC-BIOS-ADMIN-PASSWORD", 5, SecurityBand.NeedsAttention),
        new("SEC-BOOT-RESTRICTIONS", 4, SecurityBand.NeedsAttention),
        new("SEC-UAC", 4, SecurityBand.NeedsAttention),
        new("SEC-TPM", 4),
        new("SEC-VBS-HVCI", 5),
        new("SEC-LOCAL-ADMINS", 8, SecurityBand.Low)
    ];

    public static SecurityControlDescriptor? Find(string id)
        => All.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));
}
