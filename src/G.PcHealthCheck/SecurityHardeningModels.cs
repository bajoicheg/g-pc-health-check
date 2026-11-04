namespace G.PcHealthCheck;

internal enum SecurityPrimaryProvider
{
    Unknown,
    Defender,
    Kaspersky,
    Other
}

internal enum SecurityHardeningActionState
{
    Ready,
    NeedsUac,
    NotNeeded,
    Unavailable,
    BlockedByPolicy
}

internal sealed record SecurityHardeningActionDescriptor(
    string Id,
    bool RequiresAdministrator,
    ActionRisk Risk,
    string ScopeCode);

internal sealed record SecurityHardeningRecommendation(
    string Id,
    SecurityHardeningActionState State,
    string ReasonCode,
    SecurityPrimaryProvider Provider,
    bool RequiresAdministrator,
    string Scope)
{
    public bool CanRun => State is SecurityHardeningActionState.Ready or SecurityHardeningActionState.NeedsUac;
}

internal sealed class SecurityHardeningPlan
{
    public List<SecurityHardeningRecommendation> Actions { get; init; } = [];
    public bool HasRunnableActions => Actions.Any(x => x.CanRun);
    public string NoRunnableReasonCode { get; init; } = "";
}
