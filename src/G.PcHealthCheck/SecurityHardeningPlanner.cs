namespace G.PcHealthCheck;

internal static class SecurityHardeningPlanner
{
    public static SecurityHardeningPlan Plan(SecurityPostureSnapshot snapshot, ExecutionContextInfo context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);

        var provider = PrimaryProvider(snapshot);
        var actions = new List<SecurityHardeningRecommendation>(SecurityHardeningActionRegistry.All.Count)
        {
            PlanDefinitions(snapshot, context, provider),
            PlanRealtimeProtection(snapshot, context, provider),
            PlanFirewall(snapshot, context, provider)
        };

        return new SecurityHardeningPlan
        {
            Actions = actions,
            NoRunnableReasonCode = actions.Any(x => x.CanRun) ? "" : "SecurityHardening.NoRunnable"
        };
    }

    private static SecurityHardeningRecommendation PlanDefinitions(
        SecurityPostureSnapshot snapshot,
        ExecutionContextInfo context,
        SecurityPrimaryProvider provider)
    {
        const string id = "SecurityUpdateAvDefinitions";
        var descriptor = RequiredDescriptor(id);
        if (!TryObservation(snapshot, "SEC-AV-DEFINITIONS", out var observation))
            return Recommendation(descriptor, SecurityHardeningActionState.Unavailable, "EvidenceIncomplete", provider);

        if (observation.Status == SecurityControlStatus.Pass)
            return Recommendation(descriptor, SecurityHardeningActionState.NotNeeded, "AlreadySatisfied", provider);
        if (observation.Status is SecurityControlStatus.Unknown or SecurityControlStatus.NotApplicable)
            return Recommendation(descriptor, SecurityHardeningActionState.Unavailable, "EvidenceIncomplete", provider);
        if (observation.Status is not (SecurityControlStatus.Warn or SecurityControlStatus.Fail))
            return Recommendation(descriptor, SecurityHardeningActionState.Unavailable, "EvidenceIncomplete", provider);

        var supported = provider switch
        {
            SecurityPrimaryProvider.Defender => true,
            SecurityPrimaryProvider.Kaspersky => observation.Evidence.Any(x =>
                x.Source.Contains("KESCLI OPSWAT", StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
        if (!supported)
            return Recommendation(descriptor, SecurityHardeningActionState.Unavailable, "ProviderUnsupported", provider);

        return WithRights(descriptor, context, provider);
    }

    private static SecurityHardeningRecommendation PlanRealtimeProtection(
        SecurityPostureSnapshot snapshot,
        ExecutionContextInfo context,
        SecurityPrimaryProvider provider)
    {
        const string id = "SecurityEnablePrimaryRtp";
        var descriptor = RequiredDescriptor(id);
        if (provider == SecurityPrimaryProvider.Kaspersky)
            return Recommendation(descriptor, SecurityHardeningActionState.Unavailable, "KasperskyRtpManual", provider);
        if (provider != SecurityPrimaryProvider.Defender)
            return Recommendation(descriptor, SecurityHardeningActionState.Unavailable, "ProviderUnsupported", provider);
        if (!TryObservation(snapshot, "SEC-AV-RTP", out var observation))
            return Recommendation(descriptor, SecurityHardeningActionState.Unavailable, "EvidenceIncomplete", provider);
        if (observation.Status == SecurityControlStatus.Pass)
            return Recommendation(descriptor, SecurityHardeningActionState.NotNeeded, "AlreadySatisfied", provider);
        if (observation.Status is SecurityControlStatus.Unknown or SecurityControlStatus.NotApplicable)
            return Recommendation(descriptor, SecurityHardeningActionState.Unavailable, "EvidenceIncomplete", provider);

        var explicitlyOff = EvidenceEquals(observation, "RealTimeProtectionEnabled", "false");
        if (!explicitlyOff)
            return Recommendation(descriptor, SecurityHardeningActionState.Unavailable, "RtpNotLocallyActionable", provider);

        return WithRights(descriptor, context, provider);
    }

    private static SecurityHardeningRecommendation PlanFirewall(
        SecurityPostureSnapshot snapshot,
        ExecutionContextInfo context,
        SecurityPrimaryProvider provider)
    {
        const string id = "SecurityEnableWindowsFirewall";
        var descriptor = RequiredDescriptor(id);
        if (!TryObservation(snapshot, "SEC-FIREWALL", out var observation))
            return Recommendation(descriptor, SecurityHardeningActionState.Unavailable, "EvidenceIncomplete", provider);

        var effectiveProvider = EvidenceValue(observation, "EffectiveProvider");
        if (!string.Equals(effectiveProvider, "Windows", StringComparison.OrdinalIgnoreCase))
            return Recommendation(descriptor, SecurityHardeningActionState.Unavailable, "FirewallOwnershipUnsupported", provider);
        if (observation.Status == SecurityControlStatus.Pass)
            return Recommendation(descriptor, SecurityHardeningActionState.NotNeeded, "AlreadySatisfied", provider);
        if (observation.Status is SecurityControlStatus.Unknown or SecurityControlStatus.NotApplicable)
            return Recommendation(descriptor, SecurityHardeningActionState.Unavailable, "EvidenceIncomplete", provider);
        if (EvidenceEquals(observation, "PolicyEnforced", "true"))
            return Recommendation(descriptor, SecurityHardeningActionState.BlockedByPolicy, "BlockedByPolicy", provider);

        var explicitlyDisabled = new[] { "DomainEnabled", "PrivateEnabled", "PublicEnabled" }
            .Any(key => EvidenceEquals(observation, key, "false"));
        if (!explicitlyDisabled)
            return Recommendation(descriptor, SecurityHardeningActionState.Unavailable, "FirewallStateNotLocallyActionable", provider);

        return WithRights(descriptor, context, provider);
    }

    private static SecurityHardeningRecommendation WithRights(
        SecurityHardeningActionDescriptor descriptor,
        ExecutionContextInfo context,
        SecurityPrimaryProvider provider)
    {
        var state = context.HasAdministratorToken switch
        {
            true => SecurityHardeningActionState.Ready,
            false => SecurityHardeningActionState.NeedsUac,
            null => SecurityHardeningActionState.Unavailable
        };
        var reason = state switch
        {
            SecurityHardeningActionState.Ready => "Ready",
            SecurityHardeningActionState.NeedsUac => "NeedsUac",
            _ => "RightsUnknown"
        };
        return Recommendation(descriptor, state, reason, provider);
    }

    private static SecurityHardeningRecommendation Recommendation(
        SecurityHardeningActionDescriptor descriptor,
        SecurityHardeningActionState state,
        string reasonCode,
        SecurityPrimaryProvider provider)
        => new(
            descriptor.Id,
            state,
            reasonCode,
            provider,
            descriptor.RequiresAdministrator,
            descriptor.ScopeCode);

    private static SecurityHardeningActionDescriptor RequiredDescriptor(string id)
        => SecurityHardeningActionRegistry.Find(id)
           ?? throw new InvalidOperationException("Security hardening descriptor is missing: " + id);

    private static SecurityPrimaryProvider PrimaryProvider(SecurityPostureSnapshot snapshot)
    {
        if (!TryObservation(snapshot, "SEC-AV-ACTIVE", out var observation))
            return SecurityPrimaryProvider.Unknown;
        var product = EvidenceValue(observation, "PrimaryProduct");
        if (string.IsNullOrWhiteSpace(product)) return SecurityPrimaryProvider.Unknown;
        if (product.Contains("Kaspersky", StringComparison.OrdinalIgnoreCase)) return SecurityPrimaryProvider.Kaspersky;
        if (product.Contains("Defender", StringComparison.OrdinalIgnoreCase)
            || product.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)) return SecurityPrimaryProvider.Defender;
        return SecurityPrimaryProvider.Other;
    }

    private static bool TryObservation(
        SecurityPostureSnapshot snapshot,
        string id,
        out SecurityControlObservation observation)
        => snapshot.ControlObservations.TryGetValue(id, out observation!);

    private static string? EvidenceValue(SecurityControlObservation observation, string key)
        => observation.Evidence.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.Ordinal))?.Value;

    private static bool EvidenceEquals(SecurityControlObservation observation, string key, string value)
        => string.Equals(EvidenceValue(observation, key), value, StringComparison.OrdinalIgnoreCase);
}
