using System.Collections.Frozen;

namespace G.PcHealthCheck;

internal enum DiagnosticBundleMode
{
    Quick,
    Extended
}

internal enum DiagnosticBundleCategory
{
    Health,
    Processes,
    Endpoints,
    Events,
    Storage,
    Performance
}

internal sealed record DiagnosticBundleOptions
{
    public DiagnosticBundleMode Mode { get; init; }
    public IReadOnlySet<DiagnosticBundleCategory> Categories { get; }
    public int PerformanceSeconds { get; init; }
    public int PerformanceIntervalSeconds { get; init; }

    public DiagnosticBundleOptions(
        DiagnosticBundleMode mode,
        IReadOnlySet<DiagnosticBundleCategory> categories,
        int PerformanceSeconds = 60,
        int PerformanceIntervalSeconds = 2)
    {
        ArgumentNullException.ThrowIfNull(categories);
        Mode = mode;
        Categories = categories.ToFrozenSet();
        this.PerformanceSeconds = PerformanceSeconds;
        this.PerformanceIntervalSeconds = PerformanceIntervalSeconds;
    }
}

internal interface IBundleSourceResult
{
    DiagnosticBundleCategory Category { get; }
    string State { get; }
    DateTimeOffset StartedAt { get; }
    DateTimeOffset FinishedAt { get; }
    bool Requested { get; }
    bool PayloadAvailable { get; }
    IReadOnlyList<string> Warnings { get; }
}

internal sealed class BundleSourceResult<T> : IBundleSourceResult where T : class
{
    public DiagnosticBundleCategory Category { get; init; }
    public string State { get; set; } = "NotRequested";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public bool Requested { get; set; }
    public T? Payload { get; set; }
    public List<string> Warnings { get; set; } = [];
    public bool PayloadAvailable => Payload is not null;
    IReadOnlyList<string> IBundleSourceResult.Warnings => Warnings;
}

internal sealed record DiagnosticBundleHealthPayload(DiagnosticData Data, ScanResult Assessment);

internal sealed class DiagnosticBundleSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public string ApplicationVersion { get; set; } = typeof(DiagnosticBundleSnapshot).Assembly.GetName().Version?.ToString(3) ?? "";
    public string ComputerName { get; set; } = Environment.MachineName;
    public DiagnosticBundleOptions Options { get; set; } = DiagnosticBundleCore.DefaultOptions(DiagnosticBundleMode.Quick);
    public ExecutionContextInfo? ExecutionContext { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset FinishedAt { get; set; }
    public string Outcome { get; set; } = "NotStarted";
    public bool CancellationRequested { get; set; }

    public BundleSourceResult<DiagnosticBundleHealthPayload> Health { get; set; } = New<DiagnosticBundleHealthPayload>(DiagnosticBundleCategory.Health);
    public BundleSourceResult<ProcessReviewSnapshot> Processes { get; set; } = New<ProcessReviewSnapshot>(DiagnosticBundleCategory.Processes);
    public BundleSourceResult<EndpointSnapshot> Endpoints { get; set; } = New<EndpointSnapshot>(DiagnosticBundleCategory.Endpoints);
    public BundleSourceResult<IncidentSnapshot> Events { get; set; } = New<IncidentSnapshot>(DiagnosticBundleCategory.Events);
    public BundleSourceResult<DiskDetailsSnapshot> Storage { get; set; } = New<DiskDetailsSnapshot>(DiagnosticBundleCategory.Storage);
    public BundleSourceResult<PerformanceSessionSnapshot> Performance { get; set; } = New<PerformanceSessionSnapshot>(DiagnosticBundleCategory.Performance);

    public IReadOnlyList<IBundleSourceResult> Sources => [Health, Processes, Endpoints, Events, Storage, Performance];

    private static BundleSourceResult<T> New<T>(DiagnosticBundleCategory category) where T : class
        => new() { Category = category };
}
