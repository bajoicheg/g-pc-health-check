using System.Diagnostics;

namespace G.PcHealthCheck;

internal sealed class DiagnosticBundleMarkerClock
{
    private long? _startedAt;

    public bool IsRunning => _startedAt.HasValue;

    public void Start(long monotonicTimestamp) => _startedAt = monotonicTimestamp;

    public void Reset() => _startedAt = null;

    public long ElapsedMs(long monotonicTimestamp)
    {
        if (_startedAt is not long started || monotonicTimestamp <= started) return 0;
        var milliseconds = Stopwatch.GetElapsedTime(started, monotonicTimestamp).TotalMilliseconds;
        return milliseconds >= long.MaxValue ? long.MaxValue : (long)milliseconds;
    }
}
