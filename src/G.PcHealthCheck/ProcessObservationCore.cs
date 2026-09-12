namespace G.PcHealthCheck;

internal static class ProcessObservationCore
{
    public static ProcessObservationTarget FromEntry(ProcessReviewEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Pid == 0 || entry.CreatedAt is not DateTimeOffset created || !ValidCreatedAt(created))
            throw new ArgumentException("Для наблюдения нужны PID и время создания выбранного процесса. Повторите сбор сведений о процессах.", nameof(entry));
        return new(entry.Pid, created, entry.Name);
    }
    internal static bool ValidCreatedAt(DateTimeOffset value)
    {
        try { return value.UtcDateTime.ToFileTimeUtc() > 0; }
        catch (ArgumentOutOfRangeException) { return false; }
    }
    public static bool Matches(ProcessObservationTarget target, ProcessCounters counters)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(counters);
        if (target.Pid == 0 || target.Pid != counters.Pid || counters.CreatedFileTime == 0 || counters.CreatedFileTime > long.MaxValue || !ValidCreatedAt(target.CreatedAt)) return false;
        // WMI CreationDate has microsecond precision; FILETIME has 100ns units.
        // After this initial comparison the Windows source retains one OS handle.
        return (ulong)target.CreatedAt.UtcDateTime.ToFileTimeUtc() / 10 == counters.CreatedFileTime / 10;
    }
    public static ProcessObservationReading Calculate(ProcessObservationTarget target, ProcessCounterSample? before, ProcessCounterSample after, int intervalSeconds)
    {
        ArgumentNullException.ThrowIfNull(after);
        if (intervalSeconds is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
        var a = after.Counters; var warnings = a.Warnings.ToList();
        if (a.State != "Live" || !Matches(target, a))
        {
            warnings.Add("Измерения процесса недоступны: " + StateText(a.State == "Live" ? "IdentityChanged" : a.State));
            return new(null, null, null, null, null, null, warnings.Distinct().ToArray());
        }
        double? memory = a.WorkingSetBytes is ulong ws ? ws / 1048576d : null;
        double? commit = a.PrivateBytes is ulong pv ? pv / 1048576d : null;
        if (memory is null) warnings.Add("Рабочая память процесса не получена.");
        if (commit is null) warnings.Add("Частная выделенная память процесса не получена.");
        long? elapsed = null;
        if (before is not null && before.OffsetMs >= 0 && after.OffsetMs > before.OffsetMs && after.OffsetMs - before.OffsetMs <= intervalSeconds * 1750L
            && before.Counters.State == "Live" && Matches(target, before.Counters) && before.Counters.CreatedFileTime == a.CreatedFileTime)
            elapsed = after.OffsetMs - before.OffsetMs;
        if (elapsed is not long milliseconds)
        {
            warnings.Add("CPU и скорости I/O: нужны два последовательных замера того же экземпляра без пропуска.");
            return new(null, memory, commit, null, null, null, warnings.Distinct().ToArray());
        }
        var b = before!.Counters;
        static double? Delta(ulong? oldValue, ulong? newValue) => oldValue is ulong old && newValue is ulong current && current >= old ? (double)(current - old) : null;
        var kernel = Delta(b.Kernel100ns, a.Kernel100ns); var user = Delta(b.User100ns, a.User100ns);
        double? cpu = null;
        if (kernel is double k && user is double u && a.LogicalProcessors > 0 && a.LogicalProcessors == b.LogicalProcessors)
        {
            var value = (k + u) / (milliseconds * 10000d * a.LogicalProcessors) * 100;
            if (double.IsFinite(value) && value is >= 0 and <= 100) cpu = value;
        }
        if (cpu is null) warnings.Add("CPU процесса: счётчик/число процессоров недоступен, изменился или дал некорректную дельту.");
        double? Rate(ulong? oldValue, ulong? newValue, string name)
        {
            var delta = Delta(oldValue, newValue);
            if (delta is double bytes)
            {
                var rate = bytes / 1048576d * 1000 / milliseconds;
                if (double.IsFinite(rate)) return rate;
            }
            warnings.Add(name + ": счётчик недоступен или уменьшился."); return null;
        }
        var read = Rate(b.ReadBytes, a.ReadBytes, "Чтение I/O"); var write = Rate(b.WriteBytes, a.WriteBytes, "Запись I/O");
        return new(cpu, memory, commit, read, write, milliseconds, warnings.Distinct().ToArray());
    }
    public static double? Value(ProcessObservationReading reading, ProcessMetric metric)
    {
        var value = metric switch
        {
            ProcessMetric.Cpu => reading.CpuPercent, ProcessMetric.WorkingSet => reading.WorkingSetMiB,
            ProcessMetric.PrivateCommit => reading.PrivateCommitMiB, ProcessMetric.ReadRate => reading.ReadMiBps,
            ProcessMetric.WriteRate => reading.WriteMiBps, _ => throw new ArgumentOutOfRangeException(nameof(metric))
        };
        return value is double v && double.IsFinite(v) && v >= 0 && (metric != ProcessMetric.Cpu || v <= 100) ? value : null;
    }
    public static List<List<PerformancePoint>> Segments(ProcessObservationSnapshot snapshot, ProcessMetric metric)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var segments = new List<List<PerformancePoint>>(); List<PerformancePoint>? segment = null; long? previous = null;
        foreach (var sample in snapshot.Samples)
        {
            var value = Value(sample.Reading, metric); var offset = sample.Process.OffsetMs;
            if (value is null || offset < 0) { segment = null; previous = null; continue; }
            if (previous is long last && (offset <= last || offset - last > snapshot.System.Options.IntervalSeconds * 1750L)) segment = null;
            if (segment is null) { segment = []; segments.Add(segment); }
            segment.Add(new(offset, value.Value)); previous = offset;
        }
        return segments;
    }
    public static string StateText(string value) => value switch
    {
        "Live" => "Тот же процесс", "Exited" => "Процесс завершён", "IdentityChanged" => "Другой экземпляр — наблюдение не переносится",
        "AccessDenied" => "Доступ к процессу запрещён", _ => "Данные процесса недоступны"
    };
}
