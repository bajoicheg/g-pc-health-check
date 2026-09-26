namespace G.PcHealthCheck;

internal static class SecurityHardeningExecutor
{
    private static readonly string[] FixedFirewallProfiles = ["Domain", "Private", "Public"];

    public static SecurityHardeningBatchResult Execute(
        SecurityHardeningPlan plan,
        ISecurityHardeningOperations operations,
        ExecutionContextInfo workerContext,
        string? sessionId = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(workerContext);

        if (workerContext.HasAdministratorToken != true)
            throw new InvalidOperationException("Security hardening execution requires a confirmed administrator token.");

        var runnable = plan.Actions.Where(x => x.CanRun).ToList();
        if (runnable.Count == 0)
            throw new InvalidOperationException("Security hardening plan contains no runnable preflight actions.");
        if (runnable.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != runnable.Count)
            throw new InvalidOperationException("Security hardening plan contains duplicate action IDs.");

        WorkerProtocol.ValidateActionIds(WorkerActionNamespace.SecurityHardening, runnable.Select(x => x.Id).ToArray());
        ValidateProviderBoundaries(runnable);

        var byId = runnable.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var started = DateTime.Now;
        var batch = new SecurityHardeningBatchResult
        {
            SessionId = sessionId ?? "",
            StartedAt = started,
            Elevated = true
        };

        foreach (var descriptor in SecurityHardeningActionRegistry.All)
        {
            if (!byId.TryGetValue(descriptor.Id, out var recommendation)) continue;
            var actionStarted = DateTime.Now;
            SecurityActionResult result;
            try
            {
                result = descriptor.Id switch
                {
                    "SecurityUpdateAvDefinitions" => operations.UpdateDefinitions(recommendation.Provider),
                    "SecurityEnablePrimaryRtp" => operations.EnableDefenderRealtimeProtection(),
                    "SecurityEnableWindowsFirewall" => operations.EnableWindowsFirewall(FixedFirewallProfiles),
                    _ => throw new InvalidOperationException("Unknown fixed Security hardening action.")
                };
            }
            catch (Exception ex)
            {
                result = new SecurityActionResult
                {
                    Id = descriptor.Id,
                    Success = false,
                    Message = $"Security hardening action failed: {ex.GetType().Name}, 0x{ex.HResult:X8}."
                };
            }

            if (!string.Equals(result.Id, descriptor.Id, StringComparison.Ordinal))
            {
                result = new SecurityActionResult
                {
                    Id = descriptor.Id,
                    Success = false,
                    Message = "Security hardening operation returned a mismatched action ID."
                };
            }
            result.StartedAt = actionStarted;
            result.FinishedAt = DateTime.Now;
            result.ExecutionContext = workerContext;
            result.TargetScope = descriptor.ScopeCode;
            batch.Actions.Add(result);
        }

        if (batch.Actions.Count != runnable.Count)
            throw new InvalidOperationException("Security hardening execution did not cover the complete fixed preflight set.");

        batch.FinishedAt = DateTime.Now;
        return batch;
    }

    private static void ValidateProviderBoundaries(IReadOnlyList<SecurityHardeningRecommendation> actions)
    {
        foreach (var action in actions)
        {
            switch (action.Id)
            {
                case "SecurityUpdateAvDefinitions":
                    if (action.Provider is not (SecurityPrimaryProvider.Defender or SecurityPrimaryProvider.Kaspersky))
                        throw new InvalidOperationException("Definitions update requires a supported primary AV provider.");
                    break;
                case "SecurityEnablePrimaryRtp":
                    if (action.Provider != SecurityPrimaryProvider.Defender)
                        throw new InvalidOperationException("Automatic primary RTP enable is Defender-only in 0.17.0.");
                    break;
                case "SecurityEnableWindowsFirewall":
                    break;
                default:
                    throw new InvalidOperationException("Security hardening plan contains an action outside the fixed registry.");
            }
        }
    }
}
