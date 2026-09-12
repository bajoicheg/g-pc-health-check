namespace G.PcHealthCheck;

internal static class ProcessObservationService
{
    public static async Task<ProcessObservationSnapshot> RunAsync(ProcessObservationTarget target, PerformanceSessionOptions options,
        IProcessObservationSource process, IPerformanceSessionSource system, IPerformanceSessionClock clock,
        ExecutionContextInfo? context, IProgress<ProcessObservationSample>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(system); ArgumentNullException.ThrowIfNull(clock);
        PerformanceValues.Validate(options);
        if (target.Pid == 0 || !ProcessObservationCore.ValidCreatedAt(target.CreatedAt)) throw new ArgumentException("Не определён экземпляр процесса.", nameof(target));
        var result = new ProcessObservationSnapshot { Target = target, ExecutionContext = context };
        using var combined = new CombinedSource(target, options.IntervalSeconds, process, system, clock);
        // Synchronous sink: the scheduler commits a pair only after both reads and
        // its cancellation check. Async Progress<T> here could reorder/miss pairs.
        var sink = new CommitSink(combined, result.Samples, progress);
        result.System = await new PerformanceSessionService().RunAsync(options, combined, clock, sink, ct).ConfigureAwait(false);
        return result;
    }
    private sealed class CommitSink(CombinedSource source, List<ProcessObservationSample> samples, IProgress<ProcessObservationSample>? progress) : IProgress<PerformanceSample>
    {
        public void Report(PerformanceSample host)
        {
            if (source.Pending is not { } pending) throw new InvalidOperationException("Нет измерения процесса для завершённой пары.");
            var pair = new ProcessObservationSample(pending.Raw, pending.Reading, host);
            samples.Add(pair); source.Pending = null; progress?.Report(pair);
        }
    }
    private sealed class CombinedSource(ProcessObservationTarget target, int interval, IProcessObservationSource process,
        IPerformanceSessionSource system, IPerformanceSessionClock clock) : IPerformanceSessionSource
    {
        private ProcessCounterSample? _previous;
        private ProcessCounters? _terminal;
        public (ProcessCounterSample Raw, ProcessObservationReading Reading)? Pending { get; set; }
        public PerformanceReading Read(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Pending = null;
            ProcessCounters counters;
            try { counters = _terminal ?? process.Read(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { counters = new() { Pid = target.Pid, State = "Unavailable", Warnings = [$"Процесс: {ex.GetType().Name}, 0x{ex.HResult:X8}."] }; }
            if (counters.State == "Live" && !ProcessObservationCore.Matches(target, counters))
                counters = new() { Pid = counters.Pid, CreatedFileTime = counters.CreatedFileTime, State = "IdentityChanged", Warnings = ["PID/время создания не соответствуют выбранной строке."] };
            if (counters.State is "Exited" or "IdentityChanged") _terminal = counters;
            var raw = new ProcessCounterSample(clock.ElapsedMs, clock.Now, counters);
            var reading = ProcessObservationCore.Calculate(target, _previous, raw, interval);
            _previous = raw;
            ct.ThrowIfCancellationRequested();
            PerformanceReading host;
            try { host = system.Read(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { host = new(null, null, null, null, [$"Компьютер: {ex.GetType().Name}, 0x{ex.HResult:X8}."]); }
            ct.ThrowIfCancellationRequested(); Pending = (raw, reading);
            return host;
        }
        // The caller owns both supplied providers, as with PerformanceSessionService.
        public void Dispose() { }
    }
}
