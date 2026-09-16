using System.Reflection;

namespace G.PcHealthCheck;

internal static class SecurityHardeningPlannerSelfTest
{
    private static readonly string[] ExpectedIds =
    [
        "SecurityUpdateAvDefinitions",
        "SecurityEnablePrimaryRtp",
        "SecurityEnableWindowsFirewall"
    ];

    public static int Run()
    {
        var failures = 0;
        void Test(string name, Action body)
        {
            try { body(); Console.WriteLine($"Security hardening planner self-test: PASS — {name}"); }
            catch (Exception ex) { failures++; Console.Error.WriteLine($"Security hardening planner self-test: FAIL — {name}: {ex.Message}"); }
        }

        Test("registry is the exact three-action allow-list and contains no prohibited mutations", () =>
        {
            var ids = SecurityHardeningActionRegistry.All.Select(x => x.Id).ToArray();
            Require(ids.SequenceEqual(ExpectedIds, StringComparer.Ordinal),
                "Security hardening registry must contain the exact approved action IDs in fixed order.");
            Require(SecurityHardeningActionRegistry.Find("SecurityEnableKasperskyRtp") is null,
                "Kaspersky RTP automatic action is explicitly forbidden in 0.17.0.");

            var forbidden = new[]
            {
                "Firmware", "Bios", "BootOrder", "BitLocker", "RecoveryKey", "LocalAdmin", "AdministratorMembership",
                "WindowsUpdate", "InstallUpdate", "Uac", "Tpm", "Vbs", "Hvci", "Tamper", "Exclusion", "Uninstall"
            };
            foreach (var id in ids)
                foreach (var fragment in forbidden)
                    Require(!id.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                        $"Forbidden mutation class leaked into automatic hardening registry: {id}");
        });

        Test("Kaspersky primary permits definitions update only when KESCLI support is proven", () =>
        {
            var supported = Snapshot(
                primaryProduct: "Kaspersky Endpoint Security for Windows",
                definitions: SecurityControlStatus.Fail,
                definitionSource: "KESCLI OPSWAT",
                rtp: SecurityControlStatus.Fail,
                rtpValue: "false",
                firewallProvider: "Windows",
                firewallStatus: SecurityControlStatus.Pass,
                firewallPolicy: false,
                firewallEnabled: true);
            var plan = SecurityHardeningPlanner.Plan(supported, StandardContext());
            Require(RunnableIds(plan).SequenceEqual(new[] { "SecurityUpdateAvDefinitions" }, StringComparer.Ordinal),
                "Supported Kaspersky posture must expose definitions update only.");
            var update = Action(plan, "SecurityUpdateAvDefinitions");
            Require(update.Provider == SecurityPrimaryProvider.Kaspersky, "Kaspersky provider classification drifted.");
            Require(update.State == SecurityHardeningActionState.NeedsUac, "Standard-user Kaspersky update must request UAC, not run silently.");
            Require(!Action(plan, "SecurityEnablePrimaryRtp").CanRun,
                "Kaspersky RTP must remain manual/read-only in 0.17.0.");

            var unsupported = Snapshot(
                primaryProduct: "Kaspersky Endpoint Security for Windows",
                definitions: SecurityControlStatus.Fail,
                definitionSource: "WSC",
                rtp: SecurityControlStatus.Fail,
                rtpValue: "false",
                firewallProvider: "Windows",
                firewallStatus: SecurityControlStatus.Pass,
                firewallPolicy: false,
                firewallEnabled: true);
            var unsupportedPlan = SecurityHardeningPlanner.Plan(unsupported, StandardContext());
            Require(Action(unsupportedPlan, "SecurityUpdateAvDefinitions").State == SecurityHardeningActionState.Unavailable,
                "Kaspersky definitions update must be unavailable unless supported local KESCLI evidence is present.");
        });

        Test("Defender primary with RTP off exposes only fixed Defender RTP candidate", () =>
        {
            var snapshot = Snapshot(
                primaryProduct: "Microsoft Defender Antivirus",
                definitions: SecurityControlStatus.Pass,
                definitionSource: "Defender",
                rtp: SecurityControlStatus.Fail,
                rtpValue: "false",
                firewallProvider: "Windows",
                firewallStatus: SecurityControlStatus.Pass,
                firewallPolicy: false,
                firewallEnabled: true);
            var plan = SecurityHardeningPlanner.Plan(snapshot, StandardContext());
            Require(RunnableIds(plan).SequenceEqual(new[] { "SecurityEnablePrimaryRtp" }, StringComparer.Ordinal),
                "Defender RTP off must expose exactly the Defender RTP hardening action.");
            var rtp = Action(plan, "SecurityEnablePrimaryRtp");
            Require(rtp.Provider == SecurityPrimaryProvider.Defender, "Defender provider classification drifted.");
            Require(rtp.State == SecurityHardeningActionState.NeedsUac, "Standard-user Defender RTP enable must require UAC.");
        });

        Test("Defender stale definitions expose fixed definitions update", () =>
        {
            var snapshot = Snapshot(
                primaryProduct: "Microsoft Defender Antivirus",
                definitions: SecurityControlStatus.Fail,
                definitionSource: "Defender",
                rtp: SecurityControlStatus.Pass,
                rtpValue: "true",
                firewallProvider: "Windows",
                firewallStatus: SecurityControlStatus.Pass,
                firewallPolicy: false,
                firewallEnabled: true);
            var plan = SecurityHardeningPlanner.Plan(snapshot, ElevatedContext());
            Require(RunnableIds(plan).SequenceEqual(new[] { "SecurityUpdateAvDefinitions" }, StringComparer.Ordinal),
                "Defender stale definitions must expose the fixed definitions action.");
            Require(Action(plan, "SecurityUpdateAvDefinitions").State == SecurityHardeningActionState.Ready,
                "Already-elevated context must not request another UAC prompt.");
        });

        Test("firewall hardening refuses third-party ownership and central policy", () =>
        {
            var thirdParty = Snapshot(
                primaryProduct: "Microsoft Defender Antivirus",
                definitions: SecurityControlStatus.Pass,
                definitionSource: "Defender",
                rtp: SecurityControlStatus.Pass,
                rtpValue: "true",
                firewallProvider: "ThirdParty",
                firewallStatus: SecurityControlStatus.Pass,
                firewallPolicy: false,
                firewallEnabled: false);
            var thirdPartyPlan = SecurityHardeningPlanner.Plan(thirdParty, StandardContext());
            Require(Action(thirdPartyPlan, "SecurityEnableWindowsFirewall").State == SecurityHardeningActionState.Unavailable,
                "Third-party effective firewall ownership must make Windows Firewall enable unavailable.");

            var policy = Snapshot(
                primaryProduct: "Microsoft Defender Antivirus",
                definitions: SecurityControlStatus.Pass,
                definitionSource: "Defender",
                rtp: SecurityControlStatus.Pass,
                rtpValue: "true",
                firewallProvider: "Windows",
                firewallStatus: SecurityControlStatus.Fail,
                firewallPolicy: true,
                firewallEnabled: false);
            var policyPlan = SecurityHardeningPlanner.Plan(policy, StandardContext());
            Require(Action(policyPlan, "SecurityEnableWindowsFirewall").State == SecurityHardeningActionState.BlockedByPolicy,
                "Centrally enforced firewall state must be BlockedByPolicy, never bypassed.");

            var local = Snapshot(
                primaryProduct: "Microsoft Defender Antivirus",
                definitions: SecurityControlStatus.Pass,
                definitionSource: "Defender",
                rtp: SecurityControlStatus.Pass,
                rtpValue: "true",
                firewallProvider: "Windows",
                firewallStatus: SecurityControlStatus.Fail,
                firewallPolicy: false,
                firewallEnabled: false);
            var localPlan = SecurityHardeningPlanner.Plan(local, ElevatedContext());
            Require(Action(localPlan, "SecurityEnableWindowsFirewall").State == SecurityHardeningActionState.Ready,
                "Locally controllable Windows Firewall off state must be a fixed hardening candidate.");
        });

        Test("no applicable hardening action yields disabled-plan explanation", () =>
        {
            var healthy = Snapshot(
                primaryProduct: "Microsoft Defender Antivirus",
                definitions: SecurityControlStatus.Pass,
                definitionSource: "Defender",
                rtp: SecurityControlStatus.Pass,
                rtpValue: "true",
                firewallProvider: "Windows",
                firewallStatus: SecurityControlStatus.Pass,
                firewallPolicy: false,
                firewallEnabled: true);
            var plan = SecurityHardeningPlanner.Plan(healthy, StandardContext());
            Require(!plan.HasRunnableActions, "Healthy posture must not create a runnable hardening action.");
            Require(!string.IsNullOrWhiteSpace(plan.NoRunnableReasonCode), "No-runnable plan must carry an explanation code for the blue button.");
        });

        Test("unknown rights never authorize an administrator hardening action", () =>
        {
            var snapshot = Snapshot(
                primaryProduct: "Microsoft Defender Antivirus",
                definitions: SecurityControlStatus.Fail,
                definitionSource: "Defender",
                rtp: SecurityControlStatus.Fail,
                rtpValue: "false",
                firewallProvider: "Windows",
                firewallStatus: SecurityControlStatus.Fail,
                firewallPolicy: false,
                firewallEnabled: false);
            var plan = SecurityHardeningPlanner.Plan(snapshot, new ExecutionContextInfo());
            Require(!plan.HasRunnableActions, "Unknown execution rights must not authorize hardening.");
            Require(plan.Actions.Where(x => x.Id is "SecurityUpdateAvDefinitions" or "SecurityEnablePrimaryRtp" or "SecurityEnableWindowsFirewall")
                .All(x => x.State == SecurityHardeningActionState.Unavailable),
                "Unknown rights must make all otherwise-needed administrator actions unavailable.");
        });

        Test("planner authorization surface accepts raw snapshot plus execution context only", () =>
        {
            var methods = typeof(SecurityHardeningPlanner).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(x => x.Name == "Plan")
                .ToList();
            Require(methods.Count == 1, "Planner must expose exactly one Plan authorization surface.");
            var parameters = methods[0].GetParameters();
            Require(parameters.Length == 2
                    && parameters[0].ParameterType == typeof(SecurityPostureSnapshot)
                    && parameters[1].ParameterType == typeof(ExecutionContextInfo),
                "Planner must consume only fresh raw SecurityPostureSnapshot plus ExecutionContextInfo.");
            Require(!parameters.Any(x => x.ParameterType == typeof(ScanResult) || x.ParameterType == typeof(SecurityPostureAssessment)),
                "Rendered/public scan state must never authorize security hardening.");
        });

        Console.WriteLine($"Security hardening planner self-test: {8 - failures}/8 passed.");
        return failures == 0 ? 0 : 1;
    }

    private static SecurityHardeningRecommendation Action(SecurityHardeningPlan plan, string id)
        => plan.Actions.Single(x => string.Equals(x.Id, id, StringComparison.Ordinal));

    private static IReadOnlyList<string> RunnableIds(SecurityHardeningPlan plan)
        => plan.Actions.Where(x => x.CanRun).Select(x => x.Id).ToList();

    private static SecurityPostureSnapshot Snapshot(
        string primaryProduct,
        SecurityControlStatus definitions,
        string definitionSource,
        SecurityControlStatus rtp,
        string rtpValue,
        string firewallProvider,
        SecurityControlStatus firewallStatus,
        bool firewallPolicy,
        bool firewallEnabled)
    {
        var snapshot = new SecurityPostureSnapshot();
        snapshot.ControlObservations["SEC-AV-ACTIVE"] = Observation(
            "SEC-AV-ACTIVE", SecurityControlStatus.Pass,
            new("PrimaryProduct", primaryProduct, "WSC"), new("ProductState", "On", "WSC"));
        snapshot.ControlObservations["SEC-AV-DEFINITIONS"] = Observation(
            "SEC-AV-DEFINITIONS", definitions,
            new("SignatureState", definitions == SecurityControlStatus.Pass ? "UpToDate" : "OutOfDate", "WSC"),
            new("DefinitionProvider", primaryProduct, definitionSource));
        snapshot.ControlObservations["SEC-AV-RTP"] = Observation(
            "SEC-AV-RTP", rtp,
            new("RealTimeProtectionEnabled", rtpValue, primaryProduct.Contains("Kaspersky", StringComparison.OrdinalIgnoreCase) ? "KESCLI OPSWAT" : "Defender"));
        snapshot.ControlObservations["SEC-FIREWALL"] = Observation(
            "SEC-FIREWALL", firewallStatus,
            new("EffectiveProvider", firewallProvider, "WindowsFirewall/WSC"),
            new("DomainEnabled", firewallEnabled ? "true" : "false", "WindowsFirewall/WSC"),
            new("PrivateEnabled", firewallEnabled ? "true" : "false", "WindowsFirewall/WSC"),
            new("PublicEnabled", firewallEnabled ? "true" : "false", "WindowsFirewall/WSC"),
            new("PolicyEnforced", firewallPolicy ? "true" : "false", "WindowsFirewall/WSC"));
        return snapshot;
    }

    private static SecurityControlObservation Observation(
        string id,
        SecurityControlStatus status,
        params SecurityEvidence[] evidence)
        => new(id, status, evidence, id);

    private static ExecutionContextInfo StandardContext() => new()
    {
        AdministratorMember = false,
        HasAdministratorToken = false,
        IsElevated = false,
        ElevationType = 3,
        ProcessSid = "S-1-5-21-1000",
        SessionSid = "S-1-5-21-1000"
    };

    private static ExecutionContextInfo ElevatedContext() => new()
    {
        AdministratorMember = true,
        HasAdministratorToken = true,
        IsElevated = true,
        ElevationType = 2,
        ProcessSid = "S-1-5-21-1000",
        SessionSid = "S-1-5-21-1000"
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
