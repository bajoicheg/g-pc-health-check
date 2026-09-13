namespace G.PcHealthCheck;

internal sealed record DiagnosticEventHighlight(
    string Log,
    string Provider,
    int EventId,
    int? Level,
    int Count,
    DateTimeOffset? Latest);

internal sealed record DiagnosticEndpointCounts(int Total, int TcpListeners, int TcpEstablished, int UdpBindings);

internal static class DiagnosticBundleCore
{
    private static readonly DiagnosticBundleCategory[] PointInTimeCategories =
    [
        DiagnosticBundleCategory.Health,
        DiagnosticBundleCategory.Processes,
        DiagnosticBundleCategory.Endpoints,
        DiagnosticBundleCategory.Events,
        DiagnosticBundleCategory.Storage
    ];

    public static DiagnosticBundleOptions DefaultOptions(DiagnosticBundleMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var categories = new HashSet<DiagnosticBundleCategory>(PointInTimeCategories);
        if (mode == DiagnosticBundleMode.Extended) categories.Add(DiagnosticBundleCategory.Performance);
        return new DiagnosticBundleOptions(mode, categories, PerformanceSeconds: 60, PerformanceIntervalSeconds: 2);
    }

    public static void Validate(DiagnosticBundleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(options.Mode)) throw new ArgumentOutOfRangeException(nameof(options.Mode));
        if (options.Categories.Count == 0) throw new ArgumentException("Нужно выбрать хотя бы одну категорию диагностики.", nameof(options));
        if (options.Categories.Any(category => !Enum.IsDefined(category)))
            throw new ArgumentException("Выбрана неизвестная категория диагностики.", nameof(options));
        if (options.Mode == DiagnosticBundleMode.Quick && options.Categories.Contains(DiagnosticBundleCategory.Performance))
            throw new ArgumentException("Быстрый режим не запускает сеанс производительности.", nameof(options));
        if (options.Mode == DiagnosticBundleMode.Extended)
        {
            if (options.PerformanceSeconds is not (30 or 60))
                throw new ArgumentOutOfRangeException(nameof(options.PerformanceSeconds), "Расширенный режим допускает 30 или 60 секунд наблюдения.");
            if (options.PerformanceIntervalSeconds is not (1 or 2 or 5))
                throw new ArgumentOutOfRangeException(nameof(options.PerformanceIntervalSeconds), "Интервал наблюдения должен быть 1, 2 или 5 секунд.");
        }
    }

    public static string CalculateOutcome(DiagnosticBundleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var requested = snapshot.Sources.Where(source => source.Requested).ToList();
        if (requested.Count == 0) return snapshot.CancellationRequested ? "Cancelled" : "Unavailable";

        var useful = requested.Any(source => source.PayloadAvailable);
        if (snapshot.CancellationRequested && !useful) return "Cancelled";
        if (!useful && requested.All(source => source.State is "Unavailable" or "Cancelled")) return "Unavailable";
        if (requested.All(source => source.State == "Complete")) return "Complete";
        return useful ? "Partial" : snapshot.CancellationRequested ? "Cancelled" : "Unavailable";
    }

    public static int AttachPerformanceMarkers(PerformanceSessionSnapshot performance, IEnumerable<PerformanceMarker> pending)
    {
        ArgumentNullException.ThrowIfNull(performance);
        ArgumentNullException.ThrowIfNull(pending);

        var excluded = 0;
        foreach (var marker in pending)
        {
            if (performance.Markers.Count >= 100 || marker.OffsetMs < 0 || marker.OffsetMs > performance.ElapsedMs)
            {
                excluded++;
                continue;
            }
            performance.Markers.Add(marker);
        }
        if (excluded > 0)
            performance.Warnings.Add($"{excluded} отметок симптомов получены вне фактической временной шкалы производительности или сверх лимита и не включены.");
        return excluded;
    }

    public static IReadOnlyList<DiagnosticEventHighlight> EventHighlights(IncidentSnapshot snapshot, int maximum = 10)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (maximum is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maximum));

        return snapshot.Logs
            .SelectMany(log => log.Events)
            .Where(item => item.Level is >= 1 and <= 3)
            .GroupBy(item => new { item.Log, item.Provider, item.EventId, item.Level })
            .Select(group => new DiagnosticEventHighlight(
                group.Key.Log,
                group.Key.Provider,
                group.Key.EventId,
                group.Key.Level,
                group.Count(),
                group.Where(item => item.Timestamp.HasValue).Select(item => item.Timestamp).Max()))
            .OrderBy(item => EventLevelRank(item.Level))
            .ThenByDescending(item => item.Count)
            .ThenByDescending(item => item.Latest)
            .ThenBy(item => item.Log, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Provider, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.EventId)
            .Take(maximum)
            .ToArray();
    }

    public static IReadOnlyList<ProcessReviewEntry> ProcessWorkingSetHighlights(ProcessReviewSnapshot snapshot, int maximum = 5)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (maximum is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maximum));

        return snapshot.Processes
            .Where(process => process.WorkingSetBytes.HasValue)
            .OrderByDescending(process => process.WorkingSetBytes)
            .ThenBy(process => process.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(process => process.Pid)
            .Take(maximum)
            .ToArray();
    }

    public static DiagnosticEndpointCounts EndpointCounts(EndpointSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var rows = snapshot.Tables.SelectMany(table => table.Rows).ToArray();
        return new DiagnosticEndpointCounts(
            rows.Length,
            rows.Count(row => string.Equals(row.Protocol, "TCP", StringComparison.OrdinalIgnoreCase)
                && string.Equals(row.State, "LISTEN", StringComparison.OrdinalIgnoreCase)),
            rows.Count(row => string.Equals(row.Protocol, "TCP", StringComparison.OrdinalIgnoreCase)
                && string.Equals(row.State, "ESTABLISHED", StringComparison.OrdinalIgnoreCase)),
            rows.Count(row => string.Equals(row.Protocol, "UDP", StringComparison.OrdinalIgnoreCase)));
    }

    public static IReadOnlyList<string> StorageHighlights(DiskDetailsSnapshot snapshot, int maximum = 10)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (maximum is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maximum));

        var result = new List<string>();
        result.AddRange(snapshot.Warnings.Where(value => !string.IsNullOrWhiteSpace(value)));
        foreach (var disk in snapshot.Disks)
        {
            var attention = DiskDetailsService.Attention(disk);
            if (attention is "CRIT" or "WARN" or "UNKNOWN")
            {
                var name = string.IsNullOrWhiteSpace(disk.Name) ? "Накопитель" : disk.Name;
                result.Add($"{attention}: {name}; DeviceId={disk.DeviceId}; {DiskDetailsService.HealthText(disk.Health)}.");
            }
            result.AddRange(disk.Warnings.Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => $"{disk.Name}: {value}"));
        }
        return result.Distinct(StringComparer.Ordinal).Take(maximum).ToArray();
    }

    private static int EventLevelRank(int? level) => level switch { 1 => 0, 2 => 1, 3 => 2, _ => 3 };
}
