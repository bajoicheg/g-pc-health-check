namespace G.PcHealthCheck;

internal enum BatchMode
{
    SelectedStrict,
    RecommendedBestEffort,
    AllBestEffort
}

internal enum PlannedActionState
{
    Run,
    Skipped,
    Superseded
}

internal sealed record ActionPreflight(
    ServiceDeskActionDescriptor Descriptor,
    ActionRecommendation? Recommendation,
    ActionAvailability Availability,
    PlannedActionState State,
    string Reason);

internal sealed record BatchPreflight(
    BatchMode Mode,
    IReadOnlyList<ActionPreflight> Actions,
    ActionRisk HighestRisk,
    bool RequiresUac,
    bool MayBreakConnectivity,
    bool MayDeleteUserVisibleState,
    bool MayRequireReboot);
