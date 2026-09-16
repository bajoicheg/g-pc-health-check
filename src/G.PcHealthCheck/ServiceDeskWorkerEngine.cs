namespace G.PcHealthCheck;

internal interface IWorkerMessageChannel
{
    void Send(WorkerMessage message);
    WorkerMessage Receive();
}

internal static class ServiceDeskWorkerEngine
{
    private static readonly HashSet<string> BeforeNetworkIds = new(
        ["RestartSpooler", "ClearPrintQueue", "RestartUpdateServices", "TimeResync", "GpUpdate", "Dism", "Sfc"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> NetworkIds = new(
        ["WinsockReset", "TcpIpReset", "RestartNetworkAdapters", "DhcpReleaseRenew", "RegisterDns"],
        StringComparer.OrdinalIgnoreCase);

    internal static RemediationBatchResult Execute(
        ServiceDeskPhasePlan plan,
        string sessionId,
        string nonce,
        IWorkerMessageChannel channel,
        IWindowsRepairOperations operations,
        ExecutionContextInfo workerContext)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(workerContext);

        ValidateWorkerPlan(plan);
        var allWorkerIds = plan.WorkerBeforeNetwork.Concat(plan.WorkerNetwork).ToArray();
        WorkerProtocol.ValidateActionIds(WorkerActionNamespace.ServiceDesk, allWorkerIds);
        if (!Guid.TryParse(sessionId, out _))
            throw new InvalidDataException("Worker session ID имеет некорректный формат.");
        if (nonce.Length != 64 || !nonce.All(Uri.IsHexDigit))
            throw new InvalidDataException("Worker nonce имеет некорректный формат.");

        var started = DateTime.Now;
        channel.Send(Message(sessionId, nonce, WorkerMessageType.Ready));
        WorkerProtocol.ValidateNamespacedMessage(
            channel.Receive(), sessionId, nonce, WorkerMessageType.Ready, WorkerActionNamespace.ServiceDesk);

        var before = ExecutePhase(plan.WorkerBeforeNetwork, sessionId, operations, workerContext);
        channel.Send(Message(sessionId, nonce, WorkerMessageType.BeforeNetwork, before));

        WorkerProtocol.ValidateNamespacedMessage(
            channel.Receive(), sessionId, nonce, WorkerMessageType.ContinueNetwork, WorkerActionNamespace.ServiceDesk);

        var network = ExecutePhase(plan.WorkerNetwork, sessionId, operations, workerContext);
        channel.Send(Message(sessionId, nonce, WorkerMessageType.FinalResult, network));

        var combined = new RemediationBatchResult
        {
            SessionId = sessionId,
            StartedAt = started,
            FinishedAt = DateTime.Now,
            Elevated = workerContext.IsElevated == true || workerContext.HasAdministratorToken == true
        };
        combined.Actions.AddRange(before.Actions);
        combined.Actions.AddRange(network.Actions);
        combined.Elevated |= before.Elevated || network.Elevated;
        return combined;
    }

    private static RemediationBatchResult ExecutePhase(
        IReadOnlyList<string> ids,
        string sessionId,
        IWindowsRepairOperations operations,
        ExecutionContextInfo workerContext)
    {
        var batch = new RemediationBatchResult
        {
            SessionId = sessionId,
            StartedAt = DateTime.Now,
            Elevated = workerContext.IsElevated == true || workerContext.HasAdministratorToken == true
        };

        foreach (var id in ids)
        {
            var started = DateTime.Now;
            RemediationActionResult result;
            try
            {
                result = id.Equals("GpUpdate", StringComparison.OrdinalIgnoreCase)
                    ? ServiceDeskNonNetworkHandlers.ExecuteMachineGpUpdate(operations)
                    : ServiceDeskNonNetworkHandlers.Execute(id, operations);
            }
            catch (Exception ex)
            {
                result = new RemediationActionResult
                {
                    Id = id,
                    Success = false,
                    Message = $"Worker action: {ex.GetType().Name}, 0x{ex.HResult:X8}."
                };
            }
            result.StartedAt = started;
            result.FinishedAt = DateTime.Now;
            result.ExecutionContext = workerContext;
            result.TargetScope = "machine-worker";
            batch.Actions.Add(result);
        }

        batch.FinishedAt = DateTime.Now;
        return batch;
    }

    private static void ValidateWorkerPlan(ServiceDeskPhasePlan plan)
    {
        if (plan.ParentBeforeNetwork.Count != 0 || plan.ParentAfterWorker.Count != 0)
            throw new InvalidOperationException("Parent-only phases must not enter the elevated worker engine.");

        ValidatePhase(plan.WorkerBeforeNetwork, BeforeNetworkIds, "before-network");
        ValidatePhase(plan.WorkerNetwork, NetworkIds, "network");
        if (plan.WorkerBeforeNetwork.Count == 0 && plan.WorkerNetwork.Count == 0)
            throw new InvalidOperationException("Worker plan contains no fixed worker actions.");
    }

    private static void ValidatePhase(
        IReadOnlyList<string> ids,
        IReadOnlySet<string> allowed,
        string phase)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var previousOrder = -1;
        foreach (var raw in ids)
        {
            if (string.IsNullOrWhiteSpace(raw) || !allowed.Contains(raw))
                throw new InvalidOperationException($"Action ID is not allowed in worker {phase} phase: {raw}.");
            var descriptor = ServiceDeskActionRegistry.Find(raw)
                ?? throw new InvalidOperationException($"Fixed worker action is absent from registry: {raw}.");
            if (!seen.Add(descriptor.Id))
                throw new InvalidOperationException($"Duplicate worker action in {phase} phase: {descriptor.Id}.");
            if (descriptor.PhaseOrder <= previousOrder)
                throw new InvalidOperationException($"Worker {phase} phase is not in fixed registry order.");
            previousOrder = descriptor.PhaseOrder;
        }
    }

    private static WorkerMessage Message(
        string sessionId,
        string nonce,
        WorkerMessageType type,
        RemediationBatchResult? result = null)
        => new()
        {
            SessionId = sessionId,
            Nonce = nonce,
            Type = type,
            Namespace = WorkerActionNamespace.ServiceDesk,
            Result = result
        };
}
