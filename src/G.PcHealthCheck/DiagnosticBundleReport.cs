using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace G.PcHealthCheck;

internal sealed record DiagnosticBundleSaveResult(string Folder, string? Zip);

internal static class DiagnosticBundleReport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Summary(DiagnosticBundleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var builder = new StringBuilder("G PC Health Check — диагностический пакет Service Desk\n");
        builder.AppendLine($"Версия приложения: {Value(snapshot.ApplicationVersion)}");
        builder.AppendLine($"Компьютер: {Value(snapshot.ComputerName)}");
        builder.AppendLine($"Режим: {ModeText(snapshot.Options.Mode)}");
        builder.AppendLine($"Начало: {snapshot.StartedAt:O}; завершение: {Time(snapshot.FinishedAt)}");
        builder.AppendLine($"Полнота пакета: {OutcomeText(snapshot.Outcome)} ({snapshot.Outcome}). Это полнота сбора, а не оценка здоровья компьютера.");
        if (snapshot.CancellationRequested) builder.AppendLine("Отмена была запрошена; завершённые источники сохранены, если успели вернуть полезные данные.");
        builder.AppendLine();
        builder.AppendLine("Контекст выполнения:");
        builder.AppendLine(ExecutionPolicy.Describe(snapshot.ExecutionContext));
        builder.AppendLine();
        builder.AppendLine("Источники:");
        foreach (var source in snapshot.Sources)
        {
            builder.Append("- ").Append(SourceName(source.Category)).Append(": ").Append(StateText(source.State));
            if (!source.Requested) builder.Append("; исключён оператором");
            else if (source.StartedAt != default) builder.Append($"; {source.StartedAt:O} — {Time(source.FinishedAt)}");
            builder.AppendLine(".");
            foreach (var warning in source.Warnings) builder.AppendLine("  ! " + warning);
        }

        builder.AppendLine();
        builder.AppendLine("Главные выводы:");
        if (snapshot.Health.Payload is { } health)
        {
            builder.AppendLine($"Health Score: {health.Assessment.Assessment.Score}; coverage: {health.Assessment.Assessment.CoveragePercent}% ({health.Assessment.Assessment.CoverageStatus}).");
            var findings = health.Assessment.Assessment.Findings
                .Where(item => item.Severity is "CRIT" or "WARN")
                .OrderBy(item => item.Severity == "CRIT" ? 0 : 1)
                .ThenByDescending(item => item.Penalty)
                .ThenBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToArray();
            if (findings.Length == 0) builder.AppendLine("В существующей оценке нет CRIT/WARN; это не отменяет предупреждения о неполных источниках пакета.");
            foreach (var finding in findings)
                builder.AppendLine($"{finding.Severity}: {finding.Category} — {finding.Title}; {finding.Value}. {finding.Recommendation}".TrimEnd());
        }
        else
        {
            builder.AppendLine("Health Check не дал полезного payload; отсутствие оценки не считается здоровым состоянием.");
        }
        builder.AppendLine("Проверьте пакет перед передачей: он может содержать аккаунты/SID, пути и команды процессов, IP/порты, тексты событий, идентификаторы устройств и заметки о симптомах.");
        return builder.ToString().TrimEnd();
    }

    public static string ManifestJson(DiagnosticBundleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var manifest = new
        {
            snapshot.SchemaVersion,
            snapshot.ApplicationVersion,
            snapshot.ComputerName,
            Mode = snapshot.Options.Mode.ToString(),
            RequestedCategories = snapshot.Options.Categories.OrderBy(item => item).Select(item => item.ToString()).ToArray(),
            snapshot.StartedAt,
            snapshot.FinishedAt,
            snapshot.Outcome,
            snapshot.CancellationRequested,
            snapshot.ExecutionContext,
            Sources = snapshot.Sources.Select(source => new
            {
                Category = source.Category.ToString(),
                source.Requested,
                source.State,
                source.StartedAt,
                source.FinishedAt,
                source.PayloadAvailable,
                source.Warnings,
                File = source.PayloadAvailable ? PayloadFile(source.Category) : null
            }).ToArray()
        };
        return JsonSerializer.Serialize(manifest, JsonOptions);
    }

    public static string Html(DiagnosticBundleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var builder = new StringBuilder("<!doctype html><html lang='ru'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>");
        builder.Append("<title>G PC Health Check — пакет Service Desk</title><style>");
        builder.Append("body{margin:0;background:#f4f7fa;color:#172432;font:14px/1.5 'Segoe UI',Arial,sans-serif}main{max-width:1350px;margin:auto;padding:24px}");
        builder.Append("section{background:#fff;border:1px solid #dce5ed;border-radius:12px;padding:20px;margin:0 0 18px}h1,h2{color:#15344f}pre{white-space:pre-wrap;overflow-wrap:anywhere;font:inherit}");
        builder.Append("table{border-collapse:collapse;width:100%;font-size:13px}th,td{padding:8px;border-bottom:1px solid #dce5ed;text-align:left;vertical-align:top;overflow-wrap:anywhere}th{background:#f0f5f8}.table{overflow-x:auto}.muted{color:#607285}.warn{font-weight:600}.files a{display:inline-block;margin-right:12px}@media(max-width:650px){main{padding:8px}}");
        builder.Append("</style></head><body><main><h1>G PC Health Check</h1><h2>Диагностический пакет для Service Desk</h2>");
        builder.Append("<section><pre>").Append(H(Summary(snapshot))).Append("</pre></section>");
        AppendSourceMatrix(builder, snapshot);
        AppendFindings(builder, snapshot);
        AppendEvents(builder, snapshot);
        AppendProcesses(builder, snapshot);
        AppendEndpoints(builder, snapshot);
        AppendStorage(builder, snapshot);
        AppendPerformance(builder, snapshot);
        AppendFiles(builder, snapshot);
        builder.Append("<p class='muted'>Перед внешней передачей проверьте содержимое. Поиск и фильтры не являются редактированием или обезличиванием. Автоматической отправки нет.</p></main></body></html>");
        return builder.ToString();
    }

    public static DiagnosticBundleSaveResult Save(DiagnosticBundleSnapshot snapshot, string parent, bool createZip)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(parent) || !Path.IsPathFullyQualified(parent))
            throw new ArgumentException("Нужен полный путь к папке для диагностического пакета.", nameof(parent));

        Directory.CreateDirectory(parent);
        var name = $"G-PC-DiagnosticBundle_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}";
        var folder = Path.Combine(parent, name);
        Directory.CreateDirectory(folder);

        WriteNew(Path.Combine(folder, "summary.html"), Html(snapshot));
        WriteNew(Path.Combine(folder, "manifest.json"), ManifestJson(snapshot));
        WritePayloads(snapshot, folder);

        string? zip = null;
        if (createZip)
        {
            zip = Path.Combine(parent, name + ".zip");
            try
            {
                ZipFile.CreateFromDirectory(folder, zip, CompressionLevel.Optimal, includeBaseDirectory: false);
            }
            catch
            {
                try { if (File.Exists(zip)) File.Delete(zip); } catch { }
                throw;
            }
        }
        return new DiagnosticBundleSaveResult(folder, zip);
    }

    private static void AppendSourceMatrix(StringBuilder builder, DiagnosticBundleSnapshot snapshot)
    {
        builder.Append("<section><h2>Источники и полнота</h2><p class='muted'>Состояние источника описывает сбор evidence и не является вердиктом о здоровье.</p><div class='table'><table><tr><th>Источник</th><th>Запрошен</th><th>Состояние</th><th>Начало</th><th>Окончание</th><th>Предупреждения</th></tr>");
        foreach (var source in snapshot.Sources)
        {
            builder.Append("<tr><td>").Append(H(SourceName(source.Category))).Append("</td><td>").Append(source.Requested ? "Да" : "Нет")
                .Append("</td><td>").Append(H(StateText(source.State))).Append("</td><td>").Append(H(Time(source.StartedAt)))
                .Append("</td><td>").Append(H(Time(source.FinishedAt))).Append("</td><td>").Append(H(string.Join("; ", source.Warnings))).Append("</td></tr>");
        }
        builder.Append("</table></div></section>");
    }

    private static void AppendFindings(StringBuilder builder, DiagnosticBundleSnapshot snapshot)
    {
        builder.Append("<section><h2>Главные выводы Health Check</h2>");
        if (snapshot.Health.Payload is not { } health)
        {
            builder.Append("<p class='warn'>").Append(H("Health Check: " + StateText(snapshot.Health.State) + ". Отсутствующий источник не считается здоровым.")).Append("</p></section>");
            return;
        }

        builder.Append("<p>Health Score: ").Append(health.Assessment.Assessment.Score).Append("; coverage: ")
            .Append(health.Assessment.Assessment.CoveragePercent).Append("% (").Append(H(health.Assessment.Assessment.CoverageStatus)).Append(").</p>");
        builder.Append("<div class='table'><table><tr><th>Уровень</th><th>Категория</th><th>Вывод</th><th>Evidence</th><th>Следующий шаг</th></tr>");
        foreach (var finding in health.Assessment.Assessment.Findings
            .Where(item => item.Severity is "CRIT" or "WARN")
            .OrderBy(item => item.Severity == "CRIT" ? 0 : 1)
            .ThenByDescending(item => item.Penalty)
            .ThenBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(12))
        {
            Row(builder, finding.Severity, finding.Category, finding.Title, finding.Value, finding.Recommendation);
        }
        builder.Append("</table></div></section>");
    }

    private static void AppendEvents(StringBuilder builder, DiagnosticBundleSnapshot snapshot)
    {
        if (!snapshot.Events.Requested) return;
        builder.Append("<section><h2>События</h2>");
        if (snapshot.Events.Payload is not { } events)
        {
            builder.Append("<p class='warn'>События: ").Append(H(StateText(snapshot.Events.State))).Append(". Отсутствующие события не означают отсутствие проблем.</p></section>");
            return;
        }
        builder.Append("<p class='muted'>Показаны только группы уже собранных Critical/Error/Warning. Совпадение по времени не доказывает причину сбоя.</p>");
        var highlights = DiagnosticBundleCore.EventHighlights(events);
        if (highlights.Count == 0) builder.Append("<p>В собранной части нет событий уровней Critical/Error/Warning. Это относится только к фактически прочитанному интервалу и журналам.</p>");
        else
        {
            builder.Append("<div class='table'><table><tr><th>Уровень</th><th>Журнал</th><th>Provider</th><th>Event ID</th><th>Количество</th><th>Последнее</th></tr>");
            foreach (var item in highlights) Row(builder, EventLevelText(item.Level), item.Log, item.Provider, item.EventId, item.Count, item.Latest?.ToString("O") ?? "—");
            builder.Append("</table></div>");
        }
        builder.Append("</section>");
    }

    private static void AppendProcesses(StringBuilder builder, DiagnosticBundleSnapshot snapshot)
    {
        if (!snapshot.Processes.Requested) return;
        builder.Append("<section><h2>Крупнейшие рабочие наборы</h2>");
        if (snapshot.Processes.Payload is not { } processes)
        {
            builder.Append("<p class='warn'>Процессы: ").Append(H(StateText(snapshot.Processes.State))).Append(".</p></section>");
            return;
        }
        builder.Append("<p class='muted'>Рейтинг использует только доступный WorkingSet текущего снимка. Большой рабочий набор сам по себе не делает процесс плохим и не устанавливает причину сбоя.</p>");
        builder.Append("<div class='table'><table><tr><th>Процесс</th><th>PID</th><th>Working set</th><th>Путь</th><th>Команда</th></tr>");
        foreach (var process in DiagnosticBundleCore.ProcessWorkingSetHighlights(processes))
            Row(builder, process.Name, process.Pid, Bytes(process.WorkingSetBytes), process.Executable, process.CommandLine);
        builder.Append("</table></div></section>");
    }

    private static void AppendEndpoints(StringBuilder builder, DiagnosticBundleSnapshot snapshot)
    {
        if (!snapshot.Endpoints.Requested) return;
        builder.Append("<section><h2>Локальные TCP/UDP endpoints</h2>");
        if (snapshot.Endpoints.Payload is not { } endpoints)
        {
            builder.Append("<p class='warn'>Endpoints: ").Append(H(StateText(snapshot.Endpoints.State))).Append(".</p></section>");
            return;
        }
        var counts = DiagnosticBundleCore.EndpointCounts(endpoints);
        builder.Append("<p>Всего строк: ").Append(counts.Total).Append("; TCP LISTEN: ").Append(counts.TcpListeners)
            .Append("; TCP ESTABLISHED: ").Append(counts.TcpEstablished).Append("; UDP bindings: ").Append(counts.UdpBindings).Append(".</p>");
        builder.Append("<p class='muted'>LISTEN/BOUND не доказывают доступность извне: действуют брандмауэр, маршрутизация и политики. ESTABLISHED не доказывает здоровье или доверие приложения. UDP — локальные привязки, не список удалённых разговоров.</p></section>");
    }

    private static void AppendStorage(StringBuilder builder, DiagnosticBundleSnapshot snapshot)
    {
        if (!snapshot.Storage.Requested) return;
        builder.Append("<section><h2>Накопители</h2>");
        if (snapshot.Storage.Payload is not { } storage)
        {
            builder.Append("<p class='warn'>Накопители: ").Append(H(StateText(snapshot.Storage.State))).Append(". Неизвестные показатели не считаются исправными.</p></section>");
            return;
        }
        var highlights = DiagnosticBundleCore.StorageHighlights(storage);
        if (highlights.Count == 0) builder.Append("<p>Явных предупреждений в доступной Windows Storage evidence не выделено; это не полный SMART и не гарантия исправности.</p>");
        else
        {
            builder.Append("<ul>");
            foreach (var value in highlights) builder.Append("<li>").Append(H(value)).Append("</li>");
            builder.Append("</ul>");
        }
        builder.Append("<p class='muted'>Данные зависят от Windows Storage, драйвера, типа подключения и прав. Отсутствующее поле остаётся неизвестным.</p></section>");
    }

    private static void AppendPerformance(StringBuilder builder, DiagnosticBundleSnapshot snapshot)
    {
        if (!snapshot.Performance.Requested) return;
        builder.Append("<section><h2>Сеанс производительности</h2>");
        if (snapshot.Performance.Payload is not { } performance)
        {
            builder.Append("<p class='warn'>Производительность: ").Append(H(StateText(snapshot.Performance.State))).Append(".</p></section>");
            return;
        }
        builder.Append("<pre>").Append(H(PerformanceSessionReport.Summary(performance))).Append("</pre>");
        builder.Append("<p><a href='performance.html'>Графики и все измерения</a> · <a href='performance.json'>JSON сеанса</a></p>");
        builder.Append("<p class='muted'>Это наблюдение, а не стресс-тест. Порог или совпадение с отметкой не доказывает причинность.</p></section>");
    }

    private static void AppendFiles(StringBuilder builder, DiagnosticBundleSnapshot snapshot)
    {
        builder.Append("<section class='files'><h2>Файлы пакета</h2><a href='manifest.json'>manifest.json</a>");
        foreach (var source in snapshot.Sources.Where(source => source.PayloadAvailable))
            builder.Append("<a href='").Append(PayloadFile(source.Category)).Append("'>").Append(PayloadFile(source.Category)).Append("</a>");
        if (snapshot.Performance.PayloadAvailable) builder.Append("<a href='performance.html'>performance.html</a>");
        builder.Append("</section>");
    }

    private static void WritePayloads(DiagnosticBundleSnapshot snapshot, string folder)
    {
        if (snapshot.Health.Payload is { } health) WriteNew(Path.Combine(folder, "health.json"), SourceJson("Health", health));
        if (snapshot.Processes.Payload is { } processes) WriteNew(Path.Combine(folder, "processes.json"), SourceJson("Processes", processes));
        if (snapshot.Endpoints.Payload is { } endpoints) WriteNew(Path.Combine(folder, "endpoints.json"), SourceJson("Endpoints", endpoints));
        if (snapshot.Events.Payload is { } events) WriteNew(Path.Combine(folder, "events.json"), SourceJson("Events", events));
        if (snapshot.Storage.Payload is { } storage) WriteNew(Path.Combine(folder, "storage.json"), SourceJson("Storage", storage));
        if (snapshot.Performance.Payload is { } performance)
        {
            WriteNew(Path.Combine(folder, "performance.json"), PerformanceSessionReport.Json(performance));
            WriteNew(Path.Combine(folder, "performance.html"), PerformanceSessionReport.Html(performance));
        }
    }

    private static string SourceJson<T>(string category, T snapshot) where T : class
        => JsonSerializer.Serialize(new { SchemaVersion = 1, Category = category, Snapshot = snapshot }, JsonOptions);

    private static void WriteNew(string path, string content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false, true));
        writer.Write(content);
    }

    private static void Row(StringBuilder builder, params object?[] values)
    {
        builder.Append("<tr>");
        foreach (var value in values)
            builder.Append("<td>").Append(H(value is null ? "—" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "—")).Append("</td>");
        builder.Append("</tr>");
    }

    private static string PayloadFile(DiagnosticBundleCategory category) => category switch
    {
        DiagnosticBundleCategory.Health => "health.json",
        DiagnosticBundleCategory.Processes => "processes.json",
        DiagnosticBundleCategory.Endpoints => "endpoints.json",
        DiagnosticBundleCategory.Events => "events.json",
        DiagnosticBundleCategory.Storage => "storage.json",
        DiagnosticBundleCategory.Performance => "performance.json",
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };

    private static string SourceName(DiagnosticBundleCategory category) => category switch
    {
        DiagnosticBundleCategory.Health => "Health Check",
        DiagnosticBundleCategory.Processes => "Процессы",
        DiagnosticBundleCategory.Endpoints => "TCP/UDP endpoints",
        DiagnosticBundleCategory.Events => "События",
        DiagnosticBundleCategory.Storage => "Накопители",
        DiagnosticBundleCategory.Performance => "Производительность",
        _ => category.ToString()
    };

    private static string StateText(string state) => state switch
    {
        "Complete" => "Полностью собрано",
        "Partial" => "Частично",
        "Unavailable" => "Недоступно",
        "Cancelled" => "Отменено",
        "NotRequested" => "Не запрошено",
        "Pending" => "Ожидает",
        "Running" => "Выполняется",
        _ => "Неизвестно (" + state + ")"
    };

    private static string OutcomeText(string outcome) => outcome switch
    {
        "Complete" => "Полный по запрошенным категориям",
        "Partial" => "Частичный",
        "Unavailable" => "Полезные диагностические payload недоступны",
        "Cancelled" => "Отменён до получения полезного evidence",
        "Running" => "Сбор выполняется",
        _ => "Не определена"
    };

    private static string ModeText(DiagnosticBundleMode mode) => mode == DiagnosticBundleMode.Extended ? "Расширенный" : "Быстрый";
    private static string EventLevelText(int? level) => level switch { 1 => "Critical", 2 => "Error", 3 => "Warning", _ => "—" };
    private static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? "не определена" : value;
    private static string Time(DateTimeOffset value) => value == default ? "—" : value.ToString("O");
    private static string Bytes(ulong? value) => value is ulong bytes ? $"{bytes / 1048576d:0.##} MiB ({bytes:N0} байт)" : "—";
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? "");
}
