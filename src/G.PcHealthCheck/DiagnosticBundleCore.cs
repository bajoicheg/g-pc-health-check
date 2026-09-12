namespace G.PcHealthCheck;

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
}
