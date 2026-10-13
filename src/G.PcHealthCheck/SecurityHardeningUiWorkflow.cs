using System.ComponentModel;
using System.Diagnostics;

namespace G.PcHealthCheck;

internal sealed record SecurityHardeningPreflight(
    ExecutionContextInfo Context,
    SecurityPostureCollectionResult Collection,
    SecurityHardeningPlan Plan);

internal sealed record SecurityHardeningVerification(
    SecurityPostureCollectionResult Before,
    SecurityPostureCollectionResult After,
    SecurityHardeningBatchResult Batch);

internal interface ISecurityHardeningUiRuntime
{
    ExecutionContextInfo CaptureContext();
    Task<SecurityPostureCollectionResult> CollectSecurityAsync(
        SystemInfo system,
        CancellationToken cancellationToken,
        IProgress<string>? progress = null);
    Task<SecurityHardeningBatchResult> ExecuteAsync(
        SecurityHardeningPlan plan,
        CancellationToken cancellationToken,
        IProgress<string>? progress = null);
}

internal static class SecurityHardeningWorkflow
{
    public static async Task<SecurityHardeningPreflight> PreflightAsync(
        SystemInfo system,
        ISecurityHardeningUiRuntime runtime,
        CancellationToken cancellationToken,
        IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(runtime);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(AppLocalization.T("Security.Hardening.Checking"));
        var context = runtime.CaptureContext()
            ?? throw new InvalidOperationException("Security hardening context capture returned null.");
        cancellationToken.ThrowIfCancellationRequested();
        var collection = await runtime.CollectSecurityAsync(system, cancellationToken, progress).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var plan = SecurityHardeningPlanner.Plan(collection.Snapshot, context);
        return new SecurityHardeningPreflight(context, collection, plan);
    }

    public static async Task<SecurityHardeningVerification> ExecuteConfirmedAsync(
        SecurityHardeningPreflight preflight,
        SystemInfo system,
        ISecurityHardeningUiRuntime runtime,
        CancellationToken cancellationToken,
        IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(preflight);
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(runtime);
        if (!preflight.Plan.HasRunnableActions)
            throw new InvalidOperationException("Security hardening preflight contains no runnable actions.");

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(AppLocalization.T("Security.Hardening.Executing"));
        var batch = await runtime.ExecuteAsync(preflight.Plan, cancellationToken, progress).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(AppLocalization.T("Security.Hardening.Verifying"));
        var after = await runtime.CollectSecurityAsync(system, cancellationToken, progress).ConfigureAwait(false);
        return new SecurityHardeningVerification(preflight.Collection, after, batch);
    }
}

internal static class SecurityHardeningPresentation
{
    public static string BuildConfirmation(SecurityHardeningPlan plan, string language)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var lines = new List<string>
        {
            T(language, "Security.Hardening.Confirmation.Intro"),
            T(language, "Security.Hardening.Confirmation.Risk"),
            T(language, "Security.Hardening.Confirmation.Uac") + ": "
                + T(language, plan.Actions.Any(x => x.State == SecurityHardeningActionState.NeedsUac)
                    ? "Security.Hardening.Confirmation.UacRequired"
                    : "Security.Hardening.Confirmation.UacNotRequired"),
            ""
        };

        foreach (var action in plan.Actions)
        {
            var state = T(language, StateKey(action.State));
            lines.Add($"• {action.Id} — {state} — {action.ReasonCode}");
        }
        lines.Add("");
        lines.Add(T(language, "Security.Hardening.Confirmation.PostRescan"));
        return string.Join(Environment.NewLine, lines);
    }

    public static string BuildResult(SecurityHardeningVerification verification, string language)
    {
        ArgumentNullException.ThrowIfNull(verification);
        var before = verification.Before.Assessment;
        var after = verification.After.Assessment;
        var succeeded = verification.Batch.Actions.Count(x => x.Success);
        return T(
            language,
            "Security.Hardening.Result.Summary",
            succeeded,
            verification.Batch.Actions.Count,
            Score(before),
            Score(after),
            before.CoveragePercent,
            after.CoveragePercent);
    }

    private static string Score(SecurityPostureAssessment assessment)
        => assessment.Score?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—";

    private static string StateKey(SecurityHardeningActionState state) => state switch
    {
        SecurityHardeningActionState.Ready => "Security.Hardening.State.Ready",
        SecurityHardeningActionState.NeedsUac => "Security.Hardening.State.NeedsUac",
        SecurityHardeningActionState.BlockedByPolicy => "Security.Hardening.State.BlockedByPolicy",
        _ => "Security.Hardening.State.Unavailable"
    };

    private static string T(string language, string key, params object?[] args)
    {
        var normalized = AppLocalization.NormalizeLanguage(language);
        var culture = System.Globalization.CultureInfo.GetCultureInfo(normalized == "en" ? "en-US" : "ru-RU");
        var value = AppLocalization.TextForCulture(normalized, key);
        return args.Length == 0 ? value : string.Format(culture, value, args);
    }
}

internal sealed record SecurityHardeningWorkerLaunchRequest(
    IReadOnlyList<string> ActionIds,
    string PipeName,
    string Arguments,
    bool RequestElevation);

internal sealed class WindowsSecurityHardeningUiRuntime : ISecurityHardeningUiRuntime
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(20);

    public ExecutionContextInfo CaptureContext()
        => ExecutionContextService.Capture()
            ?? throw new InvalidOperationException("Cannot capture Security hardening execution context.");

    public Task<SecurityPostureCollectionResult> CollectSecurityAsync(
        SystemInfo system,
        CancellationToken cancellationToken,
        IProgress<string>? progress = null)
        => SecurityPostureCollector.CollectAsync(system, cancellationToken, progress);

    public Task<SecurityHardeningBatchResult> ExecuteAsync(
        SecurityHardeningPlan plan,
        CancellationToken cancellationToken,
        IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Task.Run(() => ExecuteCore(plan, cancellationToken, progress), cancellationToken);
    }

    internal static SecurityHardeningWorkerLaunchRequest BuildWorkerRequest(
        SecurityHardeningPlan plan,
        ExecutionContextInfo context,
        string sessionId,
        string nonce)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);
        if (!Guid.TryParse(sessionId, out _))
            throw new InvalidOperationException("Security worker session ID is invalid.");
        if (nonce is not { Length: 64 } || !nonce.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Security worker nonce is invalid.");
        if (context.HasAdministratorToken is null)
            throw new InvalidOperationException("Administrator token state is unknown; Security hardening is unavailable.");

        var runnable = SecurityHardeningActionRegistry.All
            .Select(descriptor => plan.Actions.SingleOrDefault(x => string.Equals(x.Id, descriptor.Id, StringComparison.Ordinal)))
            .Where(x => x is not null && x.CanRun)
            .Cast<SecurityHardeningRecommendation>()
            .ToList();
        if (runnable.Count == 0)
            throw new InvalidOperationException("Security hardening worker request contains no runnable actions.");
        WorkerProtocol.ValidateActionIds(WorkerActionNamespace.SecurityHardening, runnable.Select(x => x.Id).ToArray());

        var avActions = runnable.Where(x => x.Id is "SecurityUpdateAvDefinitions" or "SecurityEnablePrimaryRtp").ToList();
        var providers = avActions.Select(x => x.Provider).Distinct().ToList();
        if (providers.Count > 1)
            throw new InvalidOperationException("Security hardening worker request contains conflicting AV providers.");
        var provider = providers.Count == 0 ? SecurityPrimaryProvider.Unknown : providers[0];
        if (avActions.Any(x => x.Id == "SecurityEnablePrimaryRtp") && provider != SecurityPrimaryProvider.Defender)
            throw new InvalidOperationException("Automatic RTP enable is Defender-only.");
        if (avActions.Any(x => x.Id == "SecurityUpdateAvDefinitions")
            && provider is not (SecurityPrimaryProvider.Defender or SecurityPrimaryProvider.Kaspersky))
            throw new InvalidOperationException("Definitions update provider is not supported.");

        var csv = string.Join(',', runnable.Select(x => x.Id));
        var pipeName = "GPcHealthCheck-" + sessionId;
        var arguments = "--security-worker --session " + Quote(sessionId)
            + " --actions " + Quote(csv)
            + " --provider " + Quote(provider.ToString())
            + " --pipe " + Quote(pipeName)
            + " --nonce " + Quote(nonce);
        return new SecurityHardeningWorkerLaunchRequest(
            runnable.Select(x => x.Id).ToList(),
            pipeName,
            arguments,
            context.HasAdministratorToken == false);
    }

    private SecurityHardeningBatchResult ExecuteCore(
        SecurityHardeningPlan plan,
        CancellationToken cancellationToken,
        IProgress<string>? progress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = CaptureContext();
        var sessionId = Guid.NewGuid().ToString("D");
        var nonce = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var request = BuildWorkerRequest(plan, context, sessionId, nonce);
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine the current executable path.");
        using var pipe = RemediationWorker.CreatePipeServer(request.PipeName);
        Process? child = null;
        try
        {
            var startInfo = RemediationWorker.CreateWorkerStartInfo(executable, request.Arguments, request.RequestElevation);
            try
            {
                child = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Cannot start the Security hardening worker.");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                throw new OperationCanceledException("UAC request was cancelled.", ex);
            }

            progress?.Report(AppLocalization.T("Security.Hardening.WaitingWorker"));
            var connection = pipe.WaitForConnectionAsync(cancellationToken);
            var exit = child.WaitForExitAsync(cancellationToken);
            var connected = Task.WhenAny(connection, exit, Task.Delay(ConnectionTimeout, cancellationToken)).GetAwaiter().GetResult();
            if (connected == exit)
            {
                exit.GetAwaiter().GetResult();
                throw new InvalidOperationException($"Security hardening worker exited before pipe connection. Exit code: {child.ExitCode}.");
            }
            if (connected != connection)
                throw new TimeoutException("Security hardening worker did not connect within two minutes.");
            connection.GetAwaiter().GetResult();

            using var channel = new JsonWorkerMessageChannel(pipe, leaveOpen: true);
            var ready = ReceiveWithTimeout(channel, child, cancellationToken);
            WorkerProtocol.ValidateNamespacedMessage(
                ready, sessionId, nonce, WorkerMessageType.Ready, WorkerActionNamespace.SecurityHardening);
            channel.Send(new WorkerMessage
            {
                SessionId = sessionId,
                Nonce = nonce,
                Type = WorkerMessageType.Ready,
                Namespace = WorkerActionNamespace.SecurityHardening
            });

            var final = ReceiveWithTimeout(channel, child, cancellationToken);
            WorkerProtocol.ValidateNamespacedMessage(
                final, sessionId, nonce, WorkerMessageType.FinalResult, WorkerActionNamespace.SecurityHardening);
            var result = final.SecurityResult
                ?? throw new InvalidDataException("Security hardening worker returned no Security result.");
            if (!string.Equals(result.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Security hardening result session does not match the authenticated worker session.");

            var wait = child.WaitForExitAsync(cancellationToken);
            if (Task.WhenAny(wait, Task.Delay(OperationTimeout, cancellationToken)).GetAwaiter().GetResult() != wait)
                throw new TimeoutException("Security hardening worker did not exit within the operation timeout.");
            wait.GetAwaiter().GetResult();
            if (child.ExitCode is not 0 and not 2)
                throw new InvalidOperationException($"Security hardening worker exited with code {child.ExitCode}.");
            return result;
        }
        finally
        {
            try { if (child is not null && !child.HasExited) child.Kill(true); } catch { }
            child?.Dispose();
        }
    }

    private static WorkerMessage ReceiveWithTimeout(
        JsonWorkerMessageChannel channel,
        Process child,
        CancellationToken cancellationToken)
    {
        var read = Task.Run(channel.Receive, cancellationToken);
        var exit = child.WaitForExitAsync(cancellationToken);
        var completed = Task.WhenAny(read, exit, Task.Delay(OperationTimeout, cancellationToken)).GetAwaiter().GetResult();
        if (completed == read) return read.GetAwaiter().GetResult();
        if (completed == exit)
        {
            exit.GetAwaiter().GetResult();
            throw new InvalidOperationException($"Security hardening worker exited before completing protocol exchange. Exit code: {child.ExitCode}.");
        }
        throw new TimeoutException("Timed out waiting for Security hardening worker message.");
    }

    private static string Quote(string value)
        => "\"" + value.Replace("\"", "\\\"") + "\"";
}
