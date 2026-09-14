namespace G.PcHealthCheck;

public enum RecommendationClass
{
    Recommended,
    Optional,
    Manual
}

internal enum ActionRisk
{
    Low,
    Medium,
    High,
    Disruptive
}

internal enum ActionHost
{
    Parent,
    Worker,
    Split
}

internal sealed record ServiceDeskActionDescriptor(
    string Id,
    ActionHost Host,
    bool RequiresAdministrator,
    bool IncludeInDoEverything,
    ActionRisk Risk,
    int PhaseOrder,
    bool MayRequireReboot,
    bool MayBreakConnectivity,
    bool MayDeleteUserVisibleState,
    string TitleKey,
    string ImpactKey,
    string RiskKey,
    string VerificationKey);

internal static class ServiceDeskActionRegistry
{
    private static ServiceDeskActionDescriptor Action(
        string id,
        ActionHost host,
        bool admin,
        ActionRisk risk,
        int phaseOrder,
        bool reboot = false,
        bool connectivity = false,
        bool deletesUserState = false)
        => new(
            id,
            host,
            admin,
            IncludeInDoEverything: true,
            risk,
            phaseOrder,
            reboot,
            connectivity,
            deletesUserState,
            $"Action.{id}.Title",
            $"Action.{id}.Impact",
            $"Action.{id}.Risk",
            $"Action.{id}.Verification");

    // Execution order, not the marketing/listing order. Phase A: local machine
    // repairs; Phase B: original-user work; Phase C: network disruption;
    // Phase D: final current-process DNS cache flush.
    public static IReadOnlyList<ServiceDeskActionDescriptor> All { get; } =
    [
        Action("RestartSpooler", ActionHost.Worker, true, ActionRisk.Medium, 10),
        Action("ClearPrintQueue", ActionHost.Worker, true, ActionRisk.High, 11, deletesUserState: true),
        Action("RestartUpdateServices", ActionHost.Worker, true, ActionRisk.High, 20),
        Action("TimeResync", ActionHost.Worker, true, ActionRisk.Medium, 30),
        Action("GpUpdate", ActionHost.Split, true, ActionRisk.High, 40),
        Action("Dism", ActionHost.Worker, true, ActionRisk.Medium, 50),
        Action("Sfc", ActionHost.Worker, true, ActionRisk.Medium, 60),
        Action("CleanTemp", ActionHost.Parent, false, ActionRisk.Low, 70, deletesUserState: true),
        Action("WinsockReset", ActionHost.Worker, true, ActionRisk.High, 80, reboot: true, connectivity: true),
        Action("TcpIpReset", ActionHost.Worker, true, ActionRisk.Disruptive, 90, reboot: true, connectivity: true),
        Action("RestartNetworkAdapters", ActionHost.Worker, true, ActionRisk.Disruptive, 100, connectivity: true),
        Action("DhcpReleaseRenew", ActionHost.Worker, true, ActionRisk.Disruptive, 110, connectivity: true),
        Action("RegisterDns", ActionHost.Worker, true, ActionRisk.Medium, 120),
        Action("FlushDns", ActionHost.Parent, false, ActionRisk.Low, 130)
    ];

    private static readonly Dictionary<string, ServiceDeskActionDescriptor> ById =
        All.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

    // Task 4 is metadata/refactoring only. These are the only mutating handlers
    // that existed before 0.16.0; the remaining ten IDs stay non-executable until
    // their later TDD tasks add fixed native-operation implementations.
    public static IReadOnlySet<string> ExecutableHandlerIds { get; } =
        new HashSet<string>(["CleanTemp", "FlushDns", "Dism", "Sfc"], StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> WorkerExecutableHandlerIds { get; } =
        new HashSet<string>(["FlushDns", "Dism", "Sfc"], StringComparer.OrdinalIgnoreCase);

    public static ServiceDeskActionDescriptor? Find(string? id)
        => !string.IsNullOrWhiteSpace(id) && ById.TryGetValue(id, out var descriptor) ? descriptor : null;

    public static bool IsExecutableHandler(string? id)
        => !string.IsNullOrWhiteSpace(id) && ExecutableHandlerIds.Contains(id);

    public static bool IsWorkerExecutableHandler(string? id)
        => !string.IsNullOrWhiteSpace(id) && WorkerExecutableHandlerIds.Contains(id);

    public static IReadOnlyList<string> OrderExecutable(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return ids
            .Where(IsExecutableHandler)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => Find(id)!.PhaseOrder)
            .ToList();
    }
}
