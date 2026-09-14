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

internal enum EffectiveFirewallProviderKind
{
    Windows,
    ThirdParty,
    None,
    Ambiguous
}

internal sealed record FirewallObservation(
    EffectiveFirewallProviderKind EffectiveProvider,
    bool? DomainEnabled,
    bool? PrivateEnabled,
    bool? PublicEnabled,
    bool PolicyEnforced,
    string Source);

internal sealed record SecureBootObservation(string FirmwareType, bool? Enabled, string Source);
internal sealed record UacObservation(bool? Enabled, string Source);
internal sealed record TpmObservation(bool? Present, bool? Ready, string? SpecVersion, string Source);
internal sealed record DeviceGuardObservation(
    int? VirtualizationBasedSecurityStatus,
    IReadOnlyList<int> SecurityServicesRunning,
    IReadOnlyList<int> SecurityServicesConfigured,
    string Source);

internal sealed record SecurityCenterProduct(
    string Name,
    string Provider,
    string ProductState,
    string SignatureState,
    string? ProductPath,
    string Source);

internal sealed record DefenderSecurityObservation(
    bool? AntivirusEnabled,
    bool? RealTimeProtectionEnabled,
    bool? BehaviorMonitorEnabled,
    bool? IoavProtectionEnabled,
    bool? TamperProtected,
    int? SignatureAgeDays,
    string? PlatformVersion,
    string Source);

internal sealed record KasperskySecurityObservation(
    bool? RealTimeProtectionEnabled,
    DateTime? DefinitionsUpdatedAt,
    string? ProductVersion,
    string Source);

internal sealed record WindowsUpdateSecurityObservation(
    DateTime? LastSuccessfulQualifyingUpdate,
    int? PendingQualifyingUpdates,
    bool? PendingReboot,
    string ServiceSource,
    string Source);
