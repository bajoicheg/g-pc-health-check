using System.Reflection;

namespace G.PcHealthCheck;

internal static class SecurityHardeningUiSelfTest
{
    public static int Run()
    {
        var failures = 0;
        void Test(string name, Action body)
        {
            try { body(); Console.WriteLine($"Security hardening UI self-test: PASS — {name}"); }
            catch (Exception ex) { failures++; Console.Error.WriteLine($"Security hardening UI self-test: FAIL — {name}: {ex.GetBaseException().Message}"); }
        }

        Test("superbuttons are blue green red in fixed order and red is never default", () =>
        {
            using var form = new MainForm();
            var ensure = typeof(MainForm).GetMethod("EnsureServiceDeskActionsUi", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Service Desk action bar initializer is missing.");
            ensure.Invoke(form, null);
            var bar = form.Controls.Find("ServiceDeskActionBar", true).OfType<FlowLayoutPanel>().Single();
            var buttons = bar.Controls.OfType<Button>().ToArray();
            Require(buttons.Select(x => x.Name).SequenceEqual(new[] { "HardenSecurity", "MakeBetter", "DoEverything" }, StringComparer.Ordinal),
                "Superbutton order must be HardenSecurity -> MakeBetter -> DoEverything.");
            Require(buttons[0].BackColor.ToArgb() == Color.FromArgb(35, 134, 192).ToArgb(), "HardenSecurity color must be #2386C0.");
            Require(buttons[1].BackColor.ToArgb() == Color.FromArgb(23, 122, 75).ToArgb(), "MakeBetter color drifted from #177A4B.");
            Require(buttons[2].BackColor.ToArgb() == Color.FromArgb(181, 54, 54).ToArgb(), "DoEverything color drifted from #B53636.");
            Require(!ReferenceEquals(form.AcceptButton, buttons[2]), "Red DoEverything must never be the default Enter action.");
            Require(AppLocalization.TextForCulture("ru", "Security.Hardening.Button") != "Security.Hardening.Button", "RU hardening button resource is missing.");
            Require(AppLocalization.TextForCulture("en", "Security.Hardening.Button") != "Security.Hardening.Button", "EN hardening button resource is missing.");
        });

        Test("confirmation lists runnable skipped reasons risk UAC policy and post-rescan", () =>
        {
            var plan = new SecurityHardeningPlan
            {
                Actions =
                [
                    new("SecurityUpdateAvDefinitions", SecurityHardeningActionState.NeedsUac, "DefinitionsStale", SecurityPrimaryProvider.Defender, true, "PrimaryAvDefinitions"),
                    new("SecurityEnablePrimaryRtp", SecurityHardeningActionState.Unavailable, "PrimaryRtpAlreadyEnabled", SecurityPrimaryProvider.Defender, true, "PrimaryAvRealtimeProtection"),
                    new("SecurityEnableWindowsFirewall", SecurityHardeningActionState.BlockedByPolicy, "FirewallCentrallyManaged", SecurityPrimaryProvider.Defender, true, "WindowsFirewallProfiles")
                ]
            };
            foreach (var language in new[] { "ru", "en" })
            {
                var text = SecurityHardeningPresentation.BuildConfirmation(plan, language);
                foreach (var action in plan.Actions)
                {
                    Require(text.Contains(action.Id, StringComparison.Ordinal), "Confirmation omitted action ID: " + action.Id);
                    Require(text.Contains(action.ReasonCode, StringComparison.Ordinal), "Confirmation omitted reason code: " + action.ReasonCode);
                }
                Require(text.Contains(AppLocalization.TextForCulture(language, "Security.Hardening.Confirmation.PostRescan"), StringComparison.Ordinal),
                    "Confirmation omitted mandatory post-action security recollection statement.");
                Require(text.Contains(AppLocalization.TextForCulture(language, "Security.Hardening.Confirmation.Risk"), StringComparison.Ordinal),
                    "Confirmation omitted security-change risk statement.");
                Require(text.Contains(AppLocalization.TextForCulture(language, "Security.Hardening.Confirmation.Uac"), StringComparison.Ordinal),
                    "Confirmation omitted UAC statement.");
            }
        });

        Test("workflow uses fresh preflight then executes once and recollects security afterward", () =>
        {
            var before = Collection(definitions: SecurityControlStatus.Fail);
            var after = Collection(definitions: SecurityControlStatus.Pass);
            var runtime = new FakeRuntime(StandardContext(), before, after);
            var system = new SystemInfo { ComputerName = "PC01", Manufacturer = "Synthetic" };
            var preflight = SecurityHardeningWorkflow.PreflightAsync(system, runtime, CancellationToken.None).GetAwaiter().GetResult();
            Require(runtime.Calls.SequenceEqual(new[] { "CaptureContext", "CollectSecurity" }, StringComparer.Ordinal),
                "Preflight must freshly capture context and raw Security posture before confirmation.");
            Require(preflight.Plan.HasRunnableActions && preflight.Plan.Actions.Any(x => x.Id == "SecurityUpdateAvDefinitions" && x.CanRun),
                "Fresh preflight did not derive the expected fixed action.");

            var verification = SecurityHardeningWorkflow.ExecuteConfirmedAsync(preflight, system, runtime, CancellationToken.None).GetAwaiter().GetResult();
            Require(runtime.Calls.SequenceEqual(new[] { "CaptureContext", "CollectSecurity", "Execute", "CollectSecurity" }, StringComparer.Ordinal),
                "Confirmed flow must execute once and perform a separate post-action Security recollection.");
            Require(verification.Before.Assessment.Controls.Any(x => x.Id == "SEC-AV-DEFINITIONS" && x.Status == SecurityControlStatus.Fail),
                "Verification lost before posture.");
            Require(verification.After.Assessment.Controls.Any(x => x.Id == "SEC-AV-DEFINITIONS" && x.Status == SecurityControlStatus.Pass),
                "Verification did not use fresh after posture.");
        });

        Test("parent worker request is one fixed security worker and one UAC decision", () =>
        {
            var plan = new SecurityHardeningPlan
            {
                Actions = [new("SecurityEnableWindowsFirewall", SecurityHardeningActionState.NeedsUac, "WindowsFirewallDisabled", SecurityPrimaryProvider.Unknown, true, "WindowsFirewallProfiles")]
            };
            var session = "00000000-1111-2222-3333-444444444444";
            var nonce = new string('A', 64);
            var request = WindowsSecurityHardeningUiRuntime.BuildWorkerRequest(plan, StandardContext(), session, nonce);
            Require(request.RequestElevation, "Standard-user Security batch must request exactly one worker elevation.");
            Require(request.ActionIds.SequenceEqual(new[] { "SecurityEnableWindowsFirewall" }, StringComparer.Ordinal), "Worker request action set drifted.");
            Require(request.PipeName == "GPcHealthCheck-" + session, "Security worker pipe binding drifted.");
            Require(Count(request.Arguments, "--security-worker") == 1 && !request.Arguments.Contains("--phased-worker", StringComparison.OrdinalIgnoreCase),
                "Security worker request is not isolated from Service Desk worker mode.");
            var elevated = WindowsSecurityHardeningUiRuntime.BuildWorkerRequest(plan, ElevatedContext(), session, nonce);
            Require(!elevated.RequestElevation, "Already-elevated parent must not request a second UAC prompt.");
        });

        Test("blue green and red action registries stay strictly separated", () =>
        {
            var securityIds = SecurityHardeningActionRegistry.All.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            var serviceIds = ServiceDeskActionRegistry.All.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            Require(securityIds.SetEquals(new[] { "SecurityUpdateAvDefinitions", "SecurityEnablePrimaryRtp", "SecurityEnableWindowsFirewall" }),
                "Blue registry is not the exact three-action set.");
            Require(serviceIds.Count == 14, "Red Service Desk registry must remain exactly 14 actions.");
            Require(!securityIds.Overlaps(serviceIds), "Blue and Service Desk registries overlap.");
            Require(ServiceDeskActionRegistry.All.All(x => !x.Id.StartsWith("Security", StringComparison.OrdinalIgnoreCase)),
                "Security action leaked into green/red Service Desk registry.");
        });

        Console.WriteLine($"Security hardening UI self-test: {5 - failures}/5 passed.");
        return failures == 0 ? 0 : 1;
    }

    private static SecurityPostureCollectionResult Collection(SecurityControlStatus definitions)
    {
        var snapshot = new SecurityPostureSnapshot();
        snapshot.ControlObservations["SEC-AV-ACTIVE"] = new(
            "SEC-AV-ACTIVE", SecurityControlStatus.Pass,
            [new SecurityEvidence("PrimaryProduct", "Microsoft Defender Antivirus", "WSC")], "SEC-AV-ACTIVE");
        snapshot.ControlObservations["SEC-AV-DEFINITIONS"] = new(
            "SEC-AV-DEFINITIONS", definitions,
            [new SecurityEvidence("SignatureState", definitions == SecurityControlStatus.Pass ? "UpToDate" : "OutOfDate", "Defender")], "SEC-AV-DEFINITIONS");
        snapshot.ControlObservations["SEC-AV-RTP"] = new(
            "SEC-AV-RTP", SecurityControlStatus.Pass,
            [new SecurityEvidence("RealTimeProtectionEnabled", "true", "Defender")], "SEC-AV-RTP");
        snapshot.ControlObservations["SEC-FIREWALL"] = new(
            "SEC-FIREWALL", SecurityControlStatus.Pass,
            [
                new SecurityEvidence("EffectiveProvider", "Windows", "WindowsFirewall/WSC"),
                new SecurityEvidence("DomainEnabled", "true", "WindowsFirewall/WSC"),
                new SecurityEvidence("PrivateEnabled", "true", "WindowsFirewall/WSC"),
                new SecurityEvidence("PublicEnabled", "true", "WindowsFirewall/WSC"),
                new SecurityEvidence("PolicyEnforced", "false", "WindowsFirewall/WSC")
            ], "SEC-FIREWALL");
        return new SecurityPostureCollectionResult(snapshot, SecurityPostureEvaluator.Evaluate(snapshot));
    }

    private static ExecutionContextInfo StandardContext() => new()
    {
        AdministratorMember = false,
        HasAdministratorToken = false,
        IsElevated = false,
        ElevationType = 3,
        ProcessSid = "S-1-5-21-1000",
        SessionSid = "S-1-5-21-1000"
    };

    private static ExecutionContextInfo ElevatedContext() => StandardContext() with
    {
        AdministratorMember = true,
        HasAdministratorToken = true,
        IsElevated = true,
        ElevationType = 2
    };

    private sealed class FakeRuntime(
        ExecutionContextInfo context,
        params SecurityPostureCollectionResult[] collections) : ISecurityHardeningUiRuntime
    {
        private readonly Queue<SecurityPostureCollectionResult> _collections = new(collections);
        public List<string> Calls { get; } = [];

        public ExecutionContextInfo CaptureContext()
        {
            Calls.Add("CaptureContext");
            return context;
        }

        public Task<SecurityPostureCollectionResult> CollectSecurityAsync(
            SystemInfo system,
            CancellationToken cancellationToken,
            IProgress<string>? progress = null)
        {
            _ = system;
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add("CollectSecurity");
            return Task.FromResult(_collections.Dequeue());
        }

        public Task<SecurityHardeningBatchResult> ExecuteAsync(
            SecurityHardeningPlan plan,
            CancellationToken cancellationToken,
            IProgress<string>? progress = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add("Execute");
            var batch = new SecurityHardeningBatchResult { SessionId = Guid.NewGuid().ToString(), StartedAt = DateTime.Now, Elevated = true };
            foreach (var action in plan.Actions.Where(x => x.CanRun))
                batch.Actions.Add(SecurityActionResult.Succeeded(action.Id, "synthetic"));
            batch.FinishedAt = DateTime.Now;
            return Task.FromResult(batch);
        }
    }

    private static int Count(string value, string token)
    {
        var count = 0;
        var at = 0;
        while ((at = value.IndexOf(token, at, StringComparison.OrdinalIgnoreCase)) >= 0) { count++; at += token.Length; }
        return count;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
