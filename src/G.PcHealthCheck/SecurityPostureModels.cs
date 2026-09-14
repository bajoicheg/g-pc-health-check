namespace G.PcHealthCheck;

public enum SecurityControlStatus
{
    Pass,
    Warn,
    Fail,
    Unknown,
    NotApplicable
}

public enum SecurityBand
{
    High,
    Good,
    NeedsAttention,
    Low,
    AssessmentIncomplete
}

public sealed record SecurityEvidence(string Key, string Value, string Source);

public sealed record SecurityControlResult(
    string Id,
    SecurityControlStatus Status,
    int Weight,
    double EarnedFraction,
    string GuidanceCode,
    IReadOnlyList<SecurityEvidence> Evidence);

public sealed class SecurityPostureAssessment
{
    public string ModelVersion { get; init; } = "EndpointSecurityPosture-v1";
    public int? Score { get; init; }
    public int CoveragePercent { get; init; }
    public SecurityBand RawBand { get; init; }
    public SecurityBand DisplayBand { get; init; }
    public List<string> CriticalOverrides { get; init; } = [];
    public List<SecurityControlResult> Controls { get; init; } = [];
    public List<SecurityControlResult> Supplemental { get; init; } = [];
    public List<string> CollectionWarnings { get; init; } = [];
}

internal sealed record SecurityControlObservation(
    string Id,
    SecurityControlStatus Status,
    IReadOnlyList<SecurityEvidence> Evidence,
    string GuidanceCode);

internal sealed class SecurityPostureSnapshot
{
    public Dictionary<string, SecurityControlObservation> ControlObservations { get; init; } = new(StringComparer.Ordinal);
    public List<SecurityControlResult> Supplemental { get; init; } = [];
    public List<string> CollectionWarnings { get; init; } = [];
}
