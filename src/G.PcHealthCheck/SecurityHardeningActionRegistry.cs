namespace G.PcHealthCheck;

internal static class SecurityHardeningActionRegistry
{
    public static IReadOnlyList<SecurityHardeningActionDescriptor> All { get; } =
    [
        new("SecurityUpdateAvDefinitions", true, ActionRisk.Low, "PrimaryAvDefinitions"),
        new("SecurityEnablePrimaryRtp", true, ActionRisk.Medium, "DefenderRealtimeProtection"),
        new("SecurityEnableWindowsFirewall", true, ActionRisk.Medium, "WindowsFirewallProfiles")
    ];

    private static readonly Dictionary<string, SecurityHardeningActionDescriptor> ById =
        All.ToDictionary(x => x.Id, StringComparer.Ordinal);

    public static SecurityHardeningActionDescriptor? Find(string? id)
        => !string.IsNullOrWhiteSpace(id) && ById.TryGetValue(id, out var descriptor)
            ? descriptor
            : null;
}
