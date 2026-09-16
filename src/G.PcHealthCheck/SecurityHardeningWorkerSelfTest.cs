using System.Reflection;

namespace G.PcHealthCheck;

internal static class SecurityHardeningWorkerSelfTest
{
    public static int Run()
    {
        var failures = 0;
        void Test(string name, Action body)
        {
            try { body(); Console.WriteLine($"Security hardening worker self-test: PASS — {name}"); }
            catch (Exception ex) { failures++; Console.Error.WriteLine($"Security hardening worker self-test: FAIL — {name}: {ex.Message}"); }
        }

        Test("worker namespaces reject cross-namespace and unknown actions", () =>
        {
            WorkerProtocol.ValidateActionIds(WorkerActionNamespace.ServiceDesk, new[] { "Dism" });
            WorkerProtocol.ValidateActionIds(WorkerActionNamespace.SecurityHardening,
                new[] { "SecurityUpdateAvDefinitions", "SecurityEnablePrimaryRtp", "SecurityEnableWindowsFirewall" });

            RequireThrows(() => WorkerProtocol.ValidateActionIds(
                WorkerActionNamespace.ServiceDesk, new[] { "SecurityUpdateAvDefinitions" }),
                "ServiceDesk namespace accepted a Security hardening action.");
            RequireThrows(() => WorkerProtocol.ValidateActionIds(
                WorkerActionNamespace.SecurityHardening, new[] { "Dism" }),
                "Security namespace accepted a ServiceDesk action.");
            RequireThrows(() => WorkerProtocol.ValidateActionIds(
                WorkerActionNamespace.SecurityHardening, new[] { "SecurityDoAnything" }),
                "Security namespace accepted an unknown action.");
            RequireThrows(() => WorkerProtocol.ValidateActionIds(
                (WorkerActionNamespace)999, new[] { "Dism" }),
                "Unknown worker namespace was accepted.");
        });

        Test("protocol binds namespace session nonce and message phase", () =>
        {
            var session = Guid.NewGuid().ToString();
            var nonce = new string('a', 64);
            var message = new WorkerMessage
            {
                SessionId = session,
                Nonce = nonce,
                Type = WorkerMessageType.Ready,
                Namespace = WorkerActionNamespace.SecurityHardening
            };
            WorkerProtocol.ValidateNamespacedMessage(
                message, session, nonce, WorkerMessageType.Ready, WorkerActionNamespace.SecurityHardening);

            var wrongNamespace = Clone(message); wrongNamespace.Namespace = WorkerActionNamespace.ServiceDesk;
            RequireThrows(() => WorkerProtocol.ValidateNamespacedMessage(
                wrongNamespace, session, nonce, WorkerMessageType.Ready, WorkerActionNamespace.SecurityHardening),
                "Namespace substitution was accepted.");
            RequireThrows(() => WorkerProtocol.ValidateNamespacedMessage(
                message, Guid.NewGuid().ToString(), nonce, WorkerMessageType.Ready, WorkerActionNamespace.SecurityHardening),
                "Wrong worker session was accepted.");
            RequireThrows(() => WorkerProtocol.ValidateNamespacedMessage(
                message, session, new string('b', 64), WorkerMessageType.Ready, WorkerActionNamespace.SecurityHardening),
                "Wrong worker nonce was accepted.");
            RequireThrows(() => WorkerProtocol.ValidateNamespacedMessage(
                message, session, nonce, WorkerMessageType.FinalResult, WorkerActionNamespace.SecurityHardening),
                "Wrong worker phase was accepted.");
        });

        Test("security operation interface exposes fixed typed mutations only", () =>
        {
            var methods = typeof(ISecurityHardeningOperations).GetMethods();
            Require(methods.Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(
                new[] { "EnableDefenderRealtimeProtection", "EnableWindowsFirewall", "UpdateDefinitions" }, StringComparer.Ordinal),
                "Security operations interface drifted from the three approved fixed operations.");

            var update = methods.Single(x => x.Name == "UpdateDefinitions");
            Require(update.ReturnType == typeof(SecurityActionResult), "UpdateDefinitions result type drifted.");
            Require(update.GetParameters().Length == 1 && update.GetParameters()[0].ParameterType == typeof(SecurityPrimaryProvider),
                "UpdateDefinitions must accept only the typed primary provider.");

            var rtp = methods.Single(x => x.Name == "EnableDefenderRealtimeProtection");
            Require(rtp.GetParameters().Length == 0 && rtp.ReturnType == typeof(SecurityActionResult),
                "Defender RTP operation must be parameterless and fixed.");

            var firewall = methods.Single(x => x.Name == "EnableWindowsFirewall");
            var firewallParameters = firewall.GetParameters();
            Require(firewallParameters.Length == 1
                    && firewallParameters[0].ParameterType == typeof(IReadOnlyList<string>)
                    && string.Equals(firewallParameters[0].Name, "fixedProfiles", StringComparison.Ordinal),
                "Firewall operation must accept only the code-owned fixedProfiles list.");

            foreach (var method in methods)
            {
                foreach (var parameter in method.GetParameters())
                {
                    var name = parameter.Name ?? "";
                    Require(!new[] { "executable", "executablePath", "command", "rawCommand", "registryPath", "serviceName", "principal", "password", "token" }
                        .Contains(name, StringComparer.OrdinalIgnoreCase),
                        $"Security operation API exposes forbidden free-form parameter: {method.Name}.{name}");
                }
            }
        });

        Test("executor dispatches only fixed actions in registry order", () =>
        {
            var fake = new FakeOperations();
            var plan = Plan(
                Rec("SecurityUpdateAvDefinitions", SecurityPrimaryProvider.Defender),
                Rec("SecurityEnablePrimaryRtp", SecurityPrimaryProvider.Defender),
                Rec("SecurityEnableWindowsFirewall", SecurityPrimaryProvider.Defender));
            var batch = SecurityHardeningExecutor.Execute(plan, fake, ElevatedContext());
            Require(batch.Actions.Count == 3 && batch.Actions.All(x => x.Success),
                "Fixed hardening executor did not return three successful action results.");
            Require(fake.Calls.SequenceEqual(new[]
            {
                "UpdateDefinitions:Defender",
                "EnableDefenderRealtimeProtection",
                "EnableWindowsFirewall:Domain,Private,Public"
            }, StringComparer.Ordinal), "Security operations were not dispatched in fixed registry order.");
        });

        Test("executor preserves provider and rejects non-preflight or non-admin execution", () =>
        {
            var kaspersky = new FakeOperations();
            var batch = SecurityHardeningExecutor.Execute(
                Plan(Rec("SecurityUpdateAvDefinitions", SecurityPrimaryProvider.Kaspersky)),
                kaspersky,
                ElevatedContext());
            Require(batch.Actions.Single().Success, "Kaspersky fixed definitions action did not execute.");
            Require(kaspersky.Calls.Single() == "UpdateDefinitions:Kaspersky", "Kaspersky provider was not preserved.");

            var notRunnable = new SecurityHardeningPlan
            {
                Actions =
                [
                    new SecurityHardeningRecommendation(
                        "SecurityUpdateAvDefinitions", SecurityHardeningActionState.Unavailable,
                        "ProviderUnsupported", SecurityPrimaryProvider.Other, true, "PrimaryAvDefinitions")
                ],
                NoRunnableReasonCode = "SecurityHardening.NoRunnable"
            };
            RequireThrows(() => SecurityHardeningExecutor.Execute(notRunnable, new FakeOperations(), ElevatedContext()),
                "Executor accepted a plan without runnable preflight actions.");
            RequireThrows(() => SecurityHardeningExecutor.Execute(
                Plan(Rec("SecurityEnableWindowsFirewall", SecurityPrimaryProvider.Defender)),
                new FakeOperations(), new ExecutionContextInfo { HasAdministratorToken = false, IsElevated = false }),
                "Executor ran an administrator hardening plan without an administrator token.");
        });

        Test("worker CLI parsers preserve namespace isolation before opening a pipe", () =>
        {
            Require(SecurityHardeningWorker.TryBuildPlan(
                "SecurityUpdateAvDefinitions,SecurityEnableWindowsFirewall", "Defender", out var securityPlan)
                && securityPlan.HasRunnableActions,
                "Approved Security worker plan was rejected.");
            Require(!SecurityHardeningWorker.TryBuildPlan("Dism", "Defender", out _),
                "Security worker parser accepted ServiceDesk Dism.");
            Require(!RemediationWorker.TryBuildWorkerPhasePlan("SecurityUpdateAvDefinitions", out _),
                "ServiceDesk phased parser accepted a Security action.");
            Require(!SecurityHardeningWorker.TryBuildPlan("SecurityEnablePrimaryRtp", "Kaspersky", out _),
                "Security worker parser accepted forbidden Kaspersky RTP execution.");
        });

        Console.WriteLine($"Security hardening worker self-test: {6 - failures}/6 passed.");
        return failures == 0 ? 0 : 1;
    }

    private static WorkerMessage Clone(WorkerMessage source)
        => new()
        {
            SessionId = source.SessionId,
            Nonce = source.Nonce,
            Type = source.Type,
            Namespace = source.Namespace,
            Result = source.Result,
            SecurityResult = source.SecurityResult
        };

    private static SecurityHardeningRecommendation Rec(string id, SecurityPrimaryProvider provider)
        => new(id, SecurityHardeningActionState.Ready, "Ready", provider, true, id);

    private static SecurityHardeningPlan Plan(params SecurityHardeningRecommendation[] recommendations)
        => new() { Actions = recommendations.ToList(), NoRunnableReasonCode = "" };

    private static ExecutionContextInfo ElevatedContext() => new()
    {
        AdministratorMember = true,
        HasAdministratorToken = true,
        IsElevated = true,
        ElevationType = 2,
        ProcessSid = "S-1-5-21-1000",
        SessionSid = "S-1-5-21-1000"
    };

    private sealed class FakeOperations : ISecurityHardeningOperations
    {
        public List<string> Calls { get; } = [];

        public SecurityActionResult UpdateDefinitions(SecurityPrimaryProvider provider)
        {
            Calls.Add("UpdateDefinitions:" + provider);
            return SecurityActionResult.Succeeded("SecurityUpdateAvDefinitions", "synthetic");
        }

        public SecurityActionResult EnableDefenderRealtimeProtection()
        {
            Calls.Add("EnableDefenderRealtimeProtection");
            return SecurityActionResult.Succeeded("SecurityEnablePrimaryRtp", "synthetic");
        }

        public SecurityActionResult EnableWindowsFirewall(IReadOnlyList<string> fixedProfiles)
        {
            Calls.Add("EnableWindowsFirewall:" + string.Join(',', fixedProfiles));
            return SecurityActionResult.Succeeded("SecurityEnableWindowsFirewall", "synthetic");
        }
    }

    private static void RequireThrows(Action action, string message)
    {
        try { action(); }
        catch { return; }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
