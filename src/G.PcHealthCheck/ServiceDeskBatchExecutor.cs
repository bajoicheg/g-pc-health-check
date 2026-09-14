namespace G.PcHealthCheck;

internal sealed class ServiceDeskPhasePlan
{
    public IReadOnlyList<string> WorkerBeforeNetwork { get; init; } = [];
    public IReadOnlyList<string> ParentBeforeNetwork { get; init; } = [];
    public IReadOnlyList<string> WorkerNetwork { get; init; } = [];
    public IReadOnlyList<string> ParentAfterWorker { get; init; } = [];
}

internal interface IServiceDeskWorkerSession : IDisposable
{
    WorkerMessage Receive();
    void Send(WorkerMessage message);
    void WaitForExit();
}

internal interface IServiceDeskBatchRuntime
{
    ExecutionContextInfo CaptureParentContext();
    IServiceDeskWorkerSession StartWorker(
        ServiceDeskPhasePlan plan,
        string sessionId,
        string nonce,
        bool requestElevation);
    RemediationActionResult ExecuteParentAction(
        string actionId,
        int tempDays,
        ExecutionContextInfo parentContext);
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

    internal static RemediationBatchResult ExecuteCore(
        IReadOnlyCollection<string> actionIds,
        int tempDays,
        IServiceDeskBatchRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(actionIds);
        ArgumentNullException.ThrowIfNull(runtime);
        tempDays = Math.Clamp(tempDays, 1, 30);

        // Authorization and user-binding decisions always use a fresh native capture
        // supplied by the runtime immediately before this batch starts.
        var parentContext = runtime.CaptureParentContext()
            ?? throw new InvalidOperationException("Не удалось получить текущий контекст родительского процесса.");
        var plan = BuildPhasePlan(actionIds, parentContext);
        var sessionId = Guid.NewGuid().ToString("D");
        var nonce = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var batch = new RemediationBatchResult
        {
            SessionId = sessionId,
            StartedAt = DateTime.Now
        };

        var workerIds = plan.WorkerBeforeNetwork
            .Concat(plan.WorkerNetwork)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (workerIds.Count == 0)
        {
            ExecuteParentPhase(plan.ParentBeforeNetwork, tempDays, parentContext, runtime, batch);
            ExecuteParentPhase(plan.ParentAfterWorker, tempDays, parentContext, runtime, batch);
            batch.FinishedAt = DateTime.Now;
            return batch;
        }

        var requiresAdministrator = workerIds.Any(id => ServiceDeskActionRegistry.Find(id)?.RequiresAdministrator == true);
        if (requiresAdministrator && parentContext.HasAdministratorToken is null)
            throw new InvalidOperationException("Не удалось подтвердить административный токен перед запуском worker.");
        var requestElevation = requiresAdministrator && parentContext.HasAdministratorToken == false;

        // StartWorker is deliberately the first mutating boundary. If UAC is cancelled,
        // no parent-side action has run and the exception propagates unchanged.
        using var worker = runtime.StartWorker(plan, sessionId, nonce, requestElevation);

        var ready = worker.Receive();
        WorkerProtocol.ValidateMessage(ready, sessionId, nonce, WorkerMessageType.Ready);
        worker.Send(Message(sessionId, nonce, WorkerMessageType.Ready));

        var beforeNetwork = worker.Receive();
        WorkerProtocol.ValidateMessage(beforeNetwork, sessionId, nonce, WorkerMessageType.BeforeNetwork);
        MergeWorkerResult(batch, beforeNetwork.Result, sessionId);

        ExecuteParentPhase(plan.ParentBeforeNetwork, tempDays, parentContext, runtime, batch);
        worker.Send(Message(sessionId, nonce, WorkerMessageType.ContinueNetwork));

        var final = worker.Receive();
        WorkerProtocol.ValidateMessage(final, sessionId, nonce, WorkerMessageType.FinalResult);
        MergeWorkerResult(batch, final.Result, sessionId);
        worker.WaitForExit();

        ExecuteParentPhase(plan.ParentAfterWorker, tempDays, parentContext, runtime, batch);
        batch.FinishedAt = DateTime.Now;
        return batch;
    }

    private static WorkerMessage Message(string sessionId, string nonce, WorkerMessageType type)
        => new()
        {
            SessionId = sessionId,
            Nonce = nonce,
            Type = type
        };

    private static void ExecuteParentPhase(
        IReadOnlyList<string> ids,
        int tempDays,
        ExecutionContextInfo parentContext,
        IServiceDeskBatchRuntime runtime,
        RemediationBatchResult batch)
    {
        foreach (var id in ids)
        {
            var result = runtime.ExecuteParentAction(id, tempDays, parentContext)
                ?? throw new InvalidOperationException($"Parent action {id} не вернул результат.");
            AddAction(batch, result);
        }
    }

    private static void MergeWorkerResult(
        RemediationBatchResult target,
        RemediationBatchResult? addition,
        string expectedSession)
    {
        if (addition is null) return;
        if (!string.Equals(addition.SessionId, expectedSession, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Session ID результата worker не совпадает с текущей сессией.");

        foreach (var action in addition.Actions) AddAction(target, action);
        target.Elevated |= addition.Elevated;
        if (addition.StartedAt != default && (target.StartedAt == default || addition.StartedAt < target.StartedAt))
            target.StartedAt = addition.StartedAt;
        if (addition.FinishedAt > target.FinishedAt) target.FinishedAt = addition.FinishedAt;
    }

    private static void AddAction(RemediationBatchResult batch, RemediationActionResult action)
    {
        batch.Actions.Add(action);
        batch.Elevated |= action.ExecutionContext?.IsElevated == true
            || action.ExecutionContext?.HasAdministratorToken == true;
    }
}
