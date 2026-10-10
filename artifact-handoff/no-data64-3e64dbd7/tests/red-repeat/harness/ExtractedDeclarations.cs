using System.Text.Json.Serialization;
namespace G.PcHealthCheck;
internal sealed class SecurityActionResult
{
    public string Id { get; init; } = "";
    public bool Success { get; init; }
    public bool BlockedByPolicy { get; init; }
    public int? ExitCode { get; init; }
    public string Message { get; init; } = "";
    [JsonIgnore]
    public string Output { get; init; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public ExecutionContextInfo? ExecutionContext { get; set; }
    public string TargetScope { get; set; } = "";

    public static SecurityActionResult Succeeded(string id, string message)
        => new() { Id = id, Success = true, Message = message };
}
internal sealed class SecurityHardeningBatchResult
{
    public string SessionId { get; init; } = "";
    public DateTime StartedAt { get; init; }
    public DateTime FinishedAt { get; set; }
    public bool Elevated { get; init; }
    public List<SecurityActionResult> Actions { get; init; } = [];
}
internal sealed record SecurityHardeningVerification(
    SecurityPostureCollectionResult Before,
    SecurityPostureCollectionResult After,
    SecurityHardeningBatchResult Batch);
internal sealed record SecurityPostureCollectionResult(
    SecurityPostureSnapshot Snapshot,
    SecurityPostureAssessment Assessment);
