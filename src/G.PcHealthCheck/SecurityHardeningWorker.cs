using System.IO.Pipes;

namespace G.PcHealthCheck;

internal static class SecurityHardeningWorker
{
    private const string PipePrefix = "GPcHealthCheck-";

    public static int Run(string[] args)
    {
        try
        {
            var session = Arg(args, "--session");
            var actionsCsv = Arg(args, "--actions");
            var providerText = Arg(args, "--provider");
            var pipeName = Arg(args, "--pipe");
            var nonce = Arg(args, "--nonce");

            if (!Guid.TryParse(session, out _)) return 20;
            if (!TryBuildPlan(actionsCsv, providerText, out var requestedPlan)) return 21;
            if (!IsValidPipeName(pipeName, session!)) return 24;
            if (!IsValidNonce(nonce)) return 25;
            if (!DiagnosticsService.IsAdministrator()) return 22;

            var context = ExecutionContextService.Capture();
            if (context.HasAdministratorToken != true) return 22;

            var diagnostic = new DiagnosticsService().CollectAsync(cancellationToken: CancellationToken.None)
                .GetAwaiter().GetResult();
            var fresh = SecurityPostureCollector.CollectAsync(diagnostic.System, CancellationToken.None)
                .GetAwaiter().GetResult();
            var freshPlan = SecurityHardeningPlanner.Plan(fresh.Snapshot, context);
            if (!TryAuthorizeFresh(requestedPlan, freshPlan, out var approvedPlan)) return 23;

            using var client = new NamedPipeClientStream(".", pipeName!, PipeDirection.InOut, PipeOptions.None);
            client.Connect((int)TimeSpan.FromSeconds(45).TotalMilliseconds);
            using var channel = new JsonWorkerMessageChannel(client, leaveOpen: true);
            channel.Send(Message(session!, nonce!, WorkerMessageType.Ready));
            WorkerProtocol.ValidateNamespacedMessage(
                channel.Receive(), session!, nonce!, WorkerMessageType.Ready, WorkerActionNamespace.SecurityHardening);

            var result = SecurityHardeningExecutor.Execute(
                approvedPlan,
                new WindowsSecurityHardeningOperations(),
                context,
                session);
            channel.Send(Message(session!, nonce!, WorkerMessageType.FinalResult, result));
            return result.Actions.All(x => x.Success) ? 0 : 2;
        }
        catch
        {
            return 99;
        }
    }

    internal static bool TryBuildPlan(string? actionsCsv, string? providerText, out SecurityHardeningPlan plan)
    {
        plan = new SecurityHardeningPlan { NoRunnableReasonCode = "SecurityHardening.InvalidWorkerRequest" };
        if (!Enum.TryParse<SecurityPrimaryProvider>(providerText, ignoreCase: true, out var provider)
            || !Enum.IsDefined(typeof(SecurityPrimaryProvider), provider)
            || provider == SecurityPrimaryProvider.Unknown)
            return false;

        var requested = (actionsCsv ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (requested.Length == 0) return false;
        try { WorkerProtocol.ValidateActionIds(WorkerActionNamespace.SecurityHardening, requested); }
        catch { return false; }

        var selected = requested.ToHashSet(StringComparer.Ordinal);
        var recommendations = new List<SecurityHardeningRecommendation>();
        foreach (var descriptor in SecurityHardeningActionRegistry.All)
        {
            if (!selected.Contains(descriptor.Id)) continue;
            if (descriptor.Id == "SecurityEnablePrimaryRtp" && provider != SecurityPrimaryProvider.Defender)
                return false;
            if (descriptor.Id == "SecurityUpdateAvDefinitions"
                && provider is not (SecurityPrimaryProvider.Defender or SecurityPrimaryProvider.Kaspersky))
                return false;
            recommendations.Add(new SecurityHardeningRecommendation(
                descriptor.Id,
                SecurityHardeningActionState.Ready,
                "WorkerRequestValidated",
                provider,
                descriptor.RequiresAdministrator,
                descriptor.ScopeCode));
        }
        if (recommendations.Count != selected.Count) return false;
        plan = new SecurityHardeningPlan { Actions = recommendations, NoRunnableReasonCode = "" };
        return true;
    }

    private static bool TryAuthorizeFresh(
        SecurityHardeningPlan requested,
        SecurityHardeningPlan fresh,
        out SecurityHardeningPlan approved)
    {
        approved = new SecurityHardeningPlan { NoRunnableReasonCode = "SecurityHardening.FreshPreflightRejected" };
        var freshById = fresh.Actions.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var selected = new List<SecurityHardeningRecommendation>();
        foreach (var request in requested.Actions.Where(x => x.CanRun))
        {
            if (!freshById.TryGetValue(request.Id, out var current) || !current.CanRun)
                return false;
            if (request.Id is "SecurityUpdateAvDefinitions" or "SecurityEnablePrimaryRtp"
                && current.Provider != request.Provider)
                return false;
            selected.Add(current with { State = SecurityHardeningActionState.Ready, ReasonCode = "FreshWorkerPreflight" });
        }
        if (selected.Count == 0) return false;
        approved = new SecurityHardeningPlan { Actions = selected, NoRunnableReasonCode = "" };
        return true;
    }

    private static WorkerMessage Message(
        string sessionId,
        string nonce,
        WorkerMessageType type,
        SecurityHardeningBatchResult? result = null)
        => new()
        {
            SessionId = sessionId,
            Nonce = nonce,
            Type = type,
            Namespace = WorkerActionNamespace.SecurityHardening,
            SecurityResult = result
        };

    private static bool IsValidPipeName(string? pipeName, string session)
        => !string.IsNullOrWhiteSpace(pipeName)
           && pipeName.Length <= 128
           && string.Equals(pipeName, PipePrefix + session, StringComparison.OrdinalIgnoreCase);

    private static bool IsValidNonce(string? nonce)
        => nonce is { Length: 64 } && nonce.All(Uri.IsHexDigit);

    private static string? Arg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }
}
