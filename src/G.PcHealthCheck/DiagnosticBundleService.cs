using System.Diagnostics;

namespace G.PcHealthCheck;

internal sealed record DiagnosticBundleProgress(
    DiagnosticBundleCategory Category,
    string Phase,
    string Message,
    DateTimeOffset Timestamp)
{
    public long MonotonicTimestamp { get; init; } = Stopwatch.GetTimestamp();
}

internal interface IDiagnosticBundleCollector
{
    Task<DiagnosticBundleHealthPayload> CollectHealthAsync(IProgress<string>? progress, CancellationToken ct);
    ProcessReviewSnapshot CollectProcesses(CancellationToken ct);
    EndpointSnapshot CollectEndpoints(ExecutionContextInfo? context, IProgress<string>? progress, CancellationToken ct);
    IncidentSnapshot CollectEvents(IncidentWindow window, CancellationToken ct);
    DiskDetailsSnapshot CollectStorage(CancellationToken ct);
    Task<PerformanceSessionSnapshot> CollectPerformanceAsync(PerformanceSessionOptions options,
        IProgress<PerformanceSample>? progress, CancellationToken ct);
}

/// <summary>
/// Composes existing read-only Windows collectors only. It does not invoke Temp cleanup/preview,
/// remediation workers, active resource probes, file-use actions or process observation.
/// </summary>
internal sealed class WindowsDiagnosticBundleCollector : IDiagnosticBundleCollector
{
    public async Task<DiagnosticBundleHealthPayload> CollectHealthAsync(IProgress<string>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var data = await new DiagnosticsService().CollectAsync(progress, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var assessment = new AssessmentService().Assess(data);
        ct.ThrowIfCancellationRequested();
        return new DiagnosticBundleHealthPayload(data, assessment);
    }

    public ProcessReviewSnapshot CollectProcesses(CancellationToken ct)
        => new ProcessReview(new WindowsProcessReviewSource()).Collect(1024, ct);

    public EndpointSnapshot CollectEndpoints(ExecutionContextInfo? context, IProgress<string>? progress, CancellationToken ct)
        => EndpointReviewService.Collect(new EndpointWindowsSource(), context, ct, progress);

    public IncidentSnapshot CollectEvents(IncidentWindow window, CancellationToken ct)
        => new IncidentEvents(new WindowsIncidentEventSource()).Collect(window, ct);

    public DiskDetailsSnapshot CollectStorage(CancellationToken ct)
        => DiskDetailsService.Collect(new WindowsDiskDetailsSource(), ct);

    public async Task<PerformanceSessionSnapshot> CollectPerformanceAsync(PerformanceSessionOptions options,
        IProgress<PerformanceSample>? progress, CancellationToken ct)
    {
        using var source = new WindowsPerformanceSessionSource();
        return await new PerformanceSessionService().RunAsync(options, source,
            new MonotonicPerformanceClock(), progress, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Deterministic sequential orchestration for the Service Desk bundle. The caller should run the
/// coordinator away from the WinForms UI thread because several existing providers are synchronous.
/// Source failures are isolated; cancellation stops before the next source while preserving evidence.
/// </summary>
internal sealed class DiagnosticBundleService
{
    public async Task<DiagnosticBundleSnapshot> CollectAsync(DiagnosticBundleOptions options,
        IDiagnosticBundleCollector collector, ExecutionContextInfo? context,
        IProgress<DiagnosticBundleProgress>? progress, IProgress<PerformanceMarker>? markers,
        CancellationToken ct)
    {
        DiagnosticBundleCore.Validate(options);
        ArgumentNullException.ThrowIfNull(collector);

        // Freeze a fresh copy for the running session even when the caller already supplied a frozen set.
        var stableOptions = new DiagnosticBundleOptions(options.Mode, options.Categories.ToHashSet(),
            options.PerformanceSeconds, options.PerformanceIntervalSeconds);
        var snapshot = new DiagnosticBundleSnapshot
        {
            Options = stableOptions,
            ExecutionContext = context,
            StartedAt = DateTimeOffset.Now,
            ComputerName = Environment.MachineName,
            Outcome = "Running"
        };
        Prepare(snapshot);

        // 0.15.0 does not invent symptom markers. Task 4 will connect explicit operator notes to the
        // performance snapshot; keeping this parameter in the approved interface avoids a UI dependency.
        _ = markers;

        if (ct.IsCancellationRequested)
            return Finish(snapshot, ct);

        await RunSourceAsync(snapshot.Health,
            () => collector.CollectHealthAsync(Details(DiagnosticBundleCategory.Health, progress), ct),
            HealthState,
            payload => HealthWarnings(payload),
            (_, _) => true,
            progress, ct).ConfigureAwait(false);
        if (ct.IsCancellationRequested) return Finish(snapshot, ct);

        await RunSourceAsync(snapshot.Processes,
            () => Task.FromResult(collector.CollectProcesses(ct)),
            payload => StandardState(payload.State, payload.Processes.Count > 0),
            payload => payload.Warnings,
            (payload, state) => UsefulByState(state, payload.Processes.Count > 0),
            progress, ct).ConfigureAwait(false);
        if (ct.IsCancellationRequested) return Finish(snapshot, ct);

        await RunSourceAsync(snapshot.Endpoints,
            () => Task.FromResult(collector.CollectEndpoints(context, Details(DiagnosticBundleCategory.Endpoints, progress), ct)),
            payload => StandardState(payload.State, payload.Tables.Any(table => table.Rows.Count > 0)),
            payload => payload.Warnings,
            (payload, state) => UsefulByState(state, payload.Tables.Any(table => table.Rows.Count > 0)),
            progress, ct).ConfigureAwait(false);
        if (ct.IsCancellationRequested) return Finish(snapshot, ct);

        var incidentTo = DateTimeOffset.Now;
        await RunSourceAsync(snapshot.Events,
            () => Task.FromResult(collector.CollectEvents(new IncidentWindow(incidentTo.AddHours(-1), incidentTo, 1000), ct)),
            payload => StandardState(payload.State, payload.Logs.Any(log => log.Events.Count > 0)),
            EventWarnings,
            (payload, state) => UsefulByState(state, payload.Logs.Any(log => log.Events.Count > 0)),
            progress, ct).ConfigureAwait(false);
        if (ct.IsCancellationRequested) return Finish(snapshot, ct);

        await RunSourceAsync(snapshot.Storage,
            () => Task.FromResult(collector.CollectStorage(ct)),
            StorageState,
            payload => payload.Warnings,
            (payload, state) => UsefulByState(state, payload.Disks.Count > 0),
            progress, ct).ConfigureAwait(false);
        if (ct.IsCancellationRequested) return Finish(snapshot, ct);

        if (snapshot.Performance.Requested)
        {
            var performanceOptions = new PerformanceSessionOptions(stableOptions.PerformanceSeconds,
                stableOptions.PerformanceIntervalSeconds);
            await RunSourceAsync(snapshot.Performance,
                () => collector.CollectPerformanceAsync(performanceOptions, null, ct),
                PerformanceState,
                PerformanceWarnings,
                (payload, state) => UsefulByState(state, payload.Samples.Count > 0),
                progress, ct).ConfigureAwait(false);
        }

        return Finish(snapshot, ct);
    }

    private static async Task RunSourceAsync<T>(BundleSourceResult<T> target,
        Func<Task<T>> collect, Func<T, string> mapState, Func<T, IEnumerable<string>> warnings,
        Func<T, string, bool> useful, IProgress<DiagnosticBundleProgress>? progress, CancellationToken ct)
        where T : class
    {
        if (!target.Requested) return;
        if (ct.IsCancellationRequested)
        {
            CancelBeforeStart(target);
            return;
        }

        target.StartedAt = DateTimeOffset.Now;
        target.State = "Running";
        Report(progress, target.Category, "Starting", AppLocalization.T("Bundle.Service.Starting", SourceName(target.Category)));
        try
        {
            var payload = await collect().ConfigureAwait(false)
                ?? throw new InvalidDataException(AppLocalization.T("Bundle.Service.SourceUnavailable"));
            var state = mapState(payload);
            target.State = state;
            target.Warnings.AddRange(warnings(payload).Where(text => !string.IsNullOrWhiteSpace(text))
                .Distinct(StringComparer.Ordinal));
            // Keep native failure/cancellation envelopes only when they contain useful completed evidence.
            // This prevents an empty Unavailable object from being counted as a useful payload by outcome logic.
            if (useful(payload, state)) target.Payload = payload;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            target.State = "Cancelled";
            target.Warnings.Add(AppLocalization.T("Bundle.Service.CancelWarning"));
        }
        catch (Exception ex)
        {
            target.State = ct.IsCancellationRequested ? "Cancelled" : "Unavailable";
            target.Warnings.Add(SourceError(ex));
        }
        finally
        {
            target.FinishedAt = DateTimeOffset.Now;
            Report(progress, target.Category, "Finished", AppLocalization.T("Bundle.Service.Finished", SourceName(target.Category), StateText(target.State)));
        }
    }

    private static void Prepare(DiagnosticBundleSnapshot snapshot)
    {
        foreach (var source in snapshot.Sources)
        {
            var requested = snapshot.Options.Categories.Contains(source.Category);
            switch (source)
            {
                case BundleSourceResult<DiagnosticBundleHealthPayload> value: Prepare(value, requested); break;
                case BundleSourceResult<ProcessReviewSnapshot> value: Prepare(value, requested); break;
                case BundleSourceResult<EndpointSnapshot> value: Prepare(value, requested); break;
                case BundleSourceResult<IncidentSnapshot> value: Prepare(value, requested); break;
                case BundleSourceResult<DiskDetailsSnapshot> value: Prepare(value, requested); break;
                case BundleSourceResult<PerformanceSessionSnapshot> value: Prepare(value, requested); break;
                default: throw new InvalidOperationException(AppLocalization.T("Bundle.Core.UnknownCategory"));
            }
        }
    }

    private static void Prepare<T>(BundleSourceResult<T> source, bool requested) where T : class
    {
        source.Requested = requested;
        source.State = requested ? "Pending" : "NotRequested";
        source.StartedAt = default;
        source.FinishedAt = default;
        source.Payload = null;
        source.Warnings.Clear();
    }

    private static DiagnosticBundleSnapshot Finish(DiagnosticBundleSnapshot snapshot, CancellationToken ct)
    {
        snapshot.CancellationRequested |= ct.IsCancellationRequested;
        if (snapshot.CancellationRequested)
        {
            foreach (var source in snapshot.Sources.Where(source => source.Requested && source.State == "Pending"))
            {
                switch (source)
                {
                    case BundleSourceResult<DiagnosticBundleHealthPayload> value: CancelBeforeStart(value); break;
                    case BundleSourceResult<ProcessReviewSnapshot> value: CancelBeforeStart(value); break;
                    case BundleSourceResult<EndpointSnapshot> value: CancelBeforeStart(value); break;
                    case BundleSourceResult<IncidentSnapshot> value: CancelBeforeStart(value); break;
                    case BundleSourceResult<DiskDetailsSnapshot> value: CancelBeforeStart(value); break;
                    case BundleSourceResult<PerformanceSessionSnapshot> value: CancelBeforeStart(value); break;
                }
            }
        }
        else
        {
            // A requested source must not silently remain pending in a final snapshot.
            foreach (var source in snapshot.Sources.Where(source => source.Requested && source.State == "Pending"))
            {
                switch (source)
                {
                    case BundleSourceResult<DiagnosticBundleHealthPayload> value: UnavailableWithoutStart(value); break;
                    case BundleSourceResult<ProcessReviewSnapshot> value: UnavailableWithoutStart(value); break;
                    case BundleSourceResult<EndpointSnapshot> value: UnavailableWithoutStart(value); break;
                    case BundleSourceResult<IncidentSnapshot> value: UnavailableWithoutStart(value); break;
                    case BundleSourceResult<DiskDetailsSnapshot> value: UnavailableWithoutStart(value); break;
                    case BundleSourceResult<PerformanceSessionSnapshot> value: UnavailableWithoutStart(value); break;
                }
            }
        }
        snapshot.FinishedAt = DateTimeOffset.Now;
        snapshot.Outcome = DiagnosticBundleCore.CalculateOutcome(snapshot);
        return snapshot;
    }

    private static void CancelBeforeStart<T>(BundleSourceResult<T> source) where T : class
    {
        source.State = "Cancelled";
        source.Warnings.Add(AppLocalization.T("Bundle.Service.CancelBefore"));
    }

    private static void UnavailableWithoutStart<T>(BundleSourceResult<T> source) where T : class
    {
        source.State = "Unavailable";
        source.Warnings.Add(AppLocalization.T("Bundle.Service.SourceUnavailable"));
    }

    private static string HealthState(DiagnosticBundleHealthPayload payload)
        => payload.Data.CollectionWarnings.Count == 0 && payload.Assessment.Assessment.MissingSignals.Count == 0
            ? "Complete" : "Partial";

    private static IEnumerable<string> HealthWarnings(DiagnosticBundleHealthPayload payload)
        => payload.Data.CollectionWarnings.Concat(payload.Assessment.Assessment.MissingSignals
            .Select(signal => AppLocalization.T("Bundle.Service.HealthMissingSignals", signal)));

    private static IEnumerable<string> EventWarnings(IncidentSnapshot payload)
        => payload.Logs.SelectMany(log => log.Warnings.Select(warning => log.Log + ": " + warning));

    private static IEnumerable<string> PerformanceWarnings(PerformanceSessionSnapshot payload)
    {
        foreach (var warning in payload.Warnings) yield return warning;
        if (payload.Samples.Any(sample => sample.Reading.Warnings.Count > 0))
            yield return AppLocalization.T("Bundle.Service.PerformancePartial", PerformanceStatistics.Completeness(payload));
    }

    private static string StandardState(string native, bool evidence)
        => native switch
        {
            "Complete" => "Complete",
            "Partial" => "Partial",
            "Unavailable" => "Unavailable",
            "Cancelled" => "Cancelled",
            _ => evidence ? "Partial" : "Unavailable"
        };

    private static string StorageState(DiskDetailsSnapshot payload)
        => payload.Outcome switch
        {
            "Completed" => "Complete",
            "Partial" => "Partial",
            "Unavailable" => "Unavailable",
            "Stopped" => "Cancelled",
            "Failed" => payload.Disks.Count > 0 ? "Partial" : "Unavailable",
            _ => payload.Disks.Count > 0 ? "Partial" : "Unavailable"
        };

    private static string PerformanceState(PerformanceSessionSnapshot payload)
    {
        if (payload.Outcome == "Stopped") return "Cancelled";
        if (payload.Outcome == "Failed") return payload.Samples.Count > 0 ? "Partial" : "Unavailable";
        if (payload.Outcome != "Completed") return payload.Samples.Count > 0 ? "Partial" : "Unavailable";
        if (payload.Samples.Count == 0) return "Unavailable";

        var expected = payload.Options.DurationSeconds / payload.Options.IntervalSeconds;
        var complete = payload.MissedSlots == 0
            && payload.Samples.Count == expected
            && Enum.GetValues<SessionMetric>().All(metric => PerformanceStatistics.For(payload, metric).Valid == payload.Samples.Count);
        return complete ? "Complete" : "Partial";
    }

    private static bool UsefulByState(string state, bool evidence)
        => state is "Complete" or "Partial" || evidence;

    private static IProgress<string>? Details(DiagnosticBundleCategory category,
        IProgress<DiagnosticBundleProgress>? progress)
        => progress is null ? null : new InlineProgress<string>(message =>
            Report(progress, category, "Collecting", message));

    private static void Report(IProgress<DiagnosticBundleProgress>? progress, DiagnosticBundleCategory category,
        string phase, string message)
        => progress?.Report(new DiagnosticBundleProgress(category, phase, message, DateTimeOffset.Now));

    private static string SourceError(Exception ex)
    {
        var prefix = ex is UnauthorizedAccessException ? AppLocalization.T("Bundle.Service.AccessDenied") + " " : string.Empty;
        return prefix + AppLocalization.T("Bundle.Service.SourceError", ex.GetType().Name, ex.HResult.ToString("X8"));
    }

    private static string SourceName(DiagnosticBundleCategory category) => AppLocalization.T(category switch
    {
        DiagnosticBundleCategory.Health => "Bundle.Source.Health",
        DiagnosticBundleCategory.Processes => "Bundle.Source.Processes",
        DiagnosticBundleCategory.Endpoints => "Bundle.Source.Endpoints",
        DiagnosticBundleCategory.Events => "Bundle.Source.Events",
        DiagnosticBundleCategory.Storage => "Bundle.Source.Storage",
        DiagnosticBundleCategory.Performance => "Bundle.Source.Performance",
        _ => "Bundle.Source.Health"
    });

    private static string StateText(string state) => AppLocalization.T(state switch
    {
        "Complete" => "Bundle.State.Complete",
        "Partial" => "Bundle.State.Partial",
        "Unavailable" => "Bundle.State.Unavailable",
        "Cancelled" => "Bundle.State.Cancelled",
        "NotRequested" => "Bundle.State.NotRequested",
        _ => "Bundle.State.Collecting"
    });

    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }
}
