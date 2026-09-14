namespace G.PcHealthCheck;

internal sealed class ServiceDeskPhasePlan
{
    public IReadOnlyList<string> WorkerBeforeNetwork { get; init; } = [];
    public IReadOnlyList<string> ParentBeforeNetwork { get; init; } = [];
    public IReadOnlyList<string> WorkerNetwork { get; init; } = [];
    public IReadOnlyList<string> ParentAfterWorker { get; init; } = [];
}

internal static class ServiceDeskBatchExecutor
{
    private static readonly HashSet<string> WorkerBeforeNetworkIds = new(
        ["RestartSpooler", "ClearPrintQueue", "RestartUpdateServices", "TimeResync", "GpUpdate", "Dism", "Sfc"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> WorkerNetworkIds = new(
        ["WinsockReset", "TcpIpReset", "RestartNetworkAdapters", "DhcpReleaseRenew", "RegisterDns"],
        StringComparer.OrdinalIgnoreCase);

    internal static ServiceDeskPhasePlan BuildPhasePlan(
        IReadOnlyCollection<string> actionIds,
        ExecutionContextInfo parentContext)
    {
        ArgumentNullException.ThrowIfNull(actionIds);
        ArgumentNullException.ThrowIfNull(parentContext);

        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in actionIds)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException("Пустой ID действия недопустим в пакетном плане.");
            var descriptor = ServiceDeskActionRegistry.Find(raw)
                ?? throw new InvalidOperationException($"Действие {raw} отсутствует в фиксированном Service Desk allow-list.");
            selected.Add(descriptor.Id);
        }

        if (selected.Count == 0)
            throw new InvalidOperationException("Пакетный план не содержит действий.");

        if (selected.Contains("GpUpdate") && !ExecutionPolicy.SameUser(parentContext))
            throw new InvalidOperationException("Для пользовательской фазы Group Policy не подтверждено совпадение исходного процесса и пользователя интерактивного сеанса.");

        // ClearPrintQueue already performs a controlled Spooler stop/start, so a
        // separate restart in the same batch would only repeat the mutation.
        var supersedeRestartSpooler = selected.Contains("ClearPrintQueue");

        var ordered = selected
            .Select(id => ServiceDeskActionRegistry.Find(id)!)
            .OrderBy(x => x.PhaseOrder)
            .Select(x => x.Id)
            .ToList();

        var workerBefore = ordered
            .Where(id => WorkerBeforeNetworkIds.Contains(id))
            .Where(id => !(supersedeRestartSpooler && id.Equals("RestartSpooler", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var parentBefore = new List<string>();
        if (selected.Contains("GpUpdate")) parentBefore.Add("GpUpdate");
        if (selected.Contains("CleanTemp")) parentBefore.Add("CleanTemp");

        var workerNetwork = ordered
            .Where(id => WorkerNetworkIds.Contains(id))
            .ToList();

        var parentAfter = selected.Contains("FlushDns")
            ? new List<string> { "FlushDns" }
            : new List<string>();

        return new ServiceDeskPhasePlan
        {
            WorkerBeforeNetwork = workerBefore,
            ParentBeforeNetwork = parentBefore,
            WorkerNetwork = workerNetwork,
            ParentAfterWorker = parentAfter
        };
    }
}
