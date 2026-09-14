using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;

namespace G.PcHealthCheck;

internal static class ServiceDeskFullBatchSelfTest
{
    private sealed class FakeRuntime : IServiceDeskBatchRuntime
    {
        internal int StartCalls { get; private set; }
        internal bool? LastElevationRequest { get; private set; }
        internal List<string> ParentActions { get; } = [];
        internal FakeSession? Session { get; private set; }

        public ExecutionContextInfo CaptureParentContext() => SameUserContext();

        public IServiceDeskWorkerSession StartWorker(ServiceDeskPhasePlan plan, string sessionId, string nonce, bool requestElevation)
        {
            StartCalls++;
            LastElevationRequest = requestElevation;
            Session = new FakeSession(sessionId, nonce);
            return Session;
        }

        public RemediationActionResult ExecuteParentAction(string actionId, int tempDays, ExecutionContextInfo parentContext)
        {
            ParentActions.Add(actionId);
            return new RemediationActionResult
            {
                Id = actionId,
                Success = true,
                DeletedFiles = actionId.Equals("CleanTemp", StringComparison.OrdinalIgnoreCase) ? tempDays : null,
                Message = "Synthetic parent action",
                ExecutionContext = parentContext,
                TargetScope = actionId.Equals("FlushDns", StringComparison.OrdinalIgnoreCase) ? "machine-parent" : "original-user"
            };
        }
    }

    private sealed class FakeSession : IServiceDeskWorkerSession
    {
        private readonly Queue<WorkerMessage> _incoming;
        internal List<WorkerMessageType> Sent { get; } = [];
        internal bool Waited { get; private set; }

        internal FakeSession(string sessionId, string nonce)
        {
            _incoming = new Queue<WorkerMessage>(
            [
                new WorkerMessage { SessionId = sessionId, Nonce = nonce, Type = WorkerMessageType.Ready },
                new WorkerMessage
                {
                    SessionId = sessionId,
                    Nonce = nonce,
                    Type = WorkerMessageType.BeforeNetwork,
                    Result = new RemediationBatchResult
                    {
                        SessionId = sessionId,
                        Elevated = true,
                        Actions = [new RemediationActionResult { Id = "GpUpdate", Success = true, TargetScope = "machine-worker" }]
                    }
                },
                new WorkerMessage
                {
                    SessionId = sessionId,
                    Nonce = nonce,
                    Type = WorkerMessageType.FinalResult,
                    Result = new RemediationBatchResult { SessionId = sessionId, Elevated = true }
                }
            ]);
        }

        public WorkerMessage Receive()
            => _incoming.Count > 0 ? _incoming.Dequeue() : throw new InvalidOperationException("Synthetic worker message queue exhausted.");

        public void Send(WorkerMessage message) => Sent.Add(message.Type);
        public void WaitForExit() => Waited = true;
        public void Dispose() { }
    }

    public static int Run()
    {
        var failures = new List<string>();
        var count = 0;
        void Test(string name, Action action)
        {
            count++;
            try { action(); }
            catch (Exception ex)
            {
                failures.Add(name + ": " + ex.GetBaseException().Message);
                Console.Error.WriteLine("FAIL: " + failures[^1]);
            }
        }

        Test("Task 8 enables exact 14 actions but legacy worker still excludes split GpUpdate", () =>
        {
            var all = ServiceDeskActionRegistry.All.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var executable = ServiceDeskActionRegistry.ExecutableHandlerIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            Require(all.Count == 14 && executable.SetEquals(all), "Executable handler coverage is not exact 14/14.");
            Require(executable.Contains("GpUpdate"), "Full split GpUpdate is still staged out after phased orchestration exists.");
            Require(!ServiceDeskActionRegistry.WorkerExecutableHandlerIds.Contains("GpUpdate"), "Legacy one-shot worker incorrectly accepts full split GpUpdate.");
        });

        Test("GUI fixed-ID seam executes split GpUpdate through one phased worker", () =>
        {
            var method = typeof(RemediationWorker).GetMethod("ExecuteActionIdsCore", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("RemediationWorker.ExecuteActionIdsCore is missing.");
            var runtime = new FakeRuntime();
            var batch = (RemediationBatchResult)(method.Invoke(null, [new[] { "GpUpdate", "CleanTemp", "FlushDns" }, 3, runtime])
                ?? throw new InvalidOperationException("ExecuteActionIdsCore returned null."));

            Require(runtime.StartCalls == 1 && runtime.LastElevationRequest == true, "Full GpUpdate did not use exactly one elevated worker request.");
            Require(runtime.ParentActions.SequenceEqual(new[] { "GpUpdate", "CleanTemp", "FlushDns" }), "Original-user/final parent phase order changed.");
            Require(runtime.Session is not null && runtime.Session.Sent.SequenceEqual(new[] { WorkerMessageType.Ready, WorkerMessageType.ContinueNetwork }) && runtime.Session.Waited,
                "Phased GUI seam did not complete authenticated worker handshake/lifetime.");
            Require(batch.Actions.Count(x => x.Id == "GpUpdate") == 2
                    && batch.Actions.Any(x => x.Id == "CleanTemp")
                    && batch.Actions.Any(x => x.Id == "FlushDns"),
                "Split GpUpdate/user cleanup/final DNS evidence was not preserved.");
        });

        Test("legacy worker rejects GpUpdate while phased worker parser accepts machine half", () =>
        {
            var parseLegacy = typeof(RemediationWorker).GetMethod("TryParseWorkerActions", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Legacy worker parser is missing.");
            object?[] legacyArgs = ["GpUpdate", null];
            Require(!Convert.ToBoolean(parseLegacy.Invoke(null, legacyArgs)), "Legacy --worker parser accepted split GpUpdate.");
            Require(RemediationWorker.TryBuildWorkerPhasePlan("GpUpdate", out var phased)
                    && phased.WorkerBeforeNetwork.SequenceEqual(new[] { "GpUpdate" })
                    && phased.WorkerNetwork.Count == 0,
                "Phased worker parser rejected the fixed machine half of GpUpdate.");
        });

        Test("red DoEverything UI becomes requestable only with complete 14-action coverage", () =>
        {
            using var form = new MainForm();
            var ensure = typeof(MainForm).GetMethod("EnsureServiceDeskActionsUi", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Main Service Desk action UI initializer is missing.");
            ensure.Invoke(form, null);
            var red = form.Controls.Find("DoEverything", true).OfType<Button>().SingleOrDefault()
                ?? throw new InvalidOperationException("DoEverything is missing.");
            Require(red.Enabled, "DoEverything remains disabled after exact 14/14 executable coverage.");
            Require(typeof(MainForm).GetMethod("DoEverythingAsync", BindingFlags.Instance | BindingFlags.NonPublic) is not null,
                "DoEverything has no dedicated exact-all execution handler.");
            Require(!ReferenceEquals(form.AcceptButton, red), "DoEverything became the default Enter action.");
        });

        Test("red confirmation enumerates exact fixed set and disruptive safety impacts", () =>
        {
            var preflight = FullPreflight();
            var text = ServiceDeskBatchUi.BuildDoEverythingConfirmation(preflight);
            foreach (var id in ServiceDeskActionRegistry.All.Select(x => x.Id))
                Require(text.Contains(id, StringComparison.Ordinal), "Red confirmation omitted fixed action: " + id);
            Require(text.Contains("сеть", StringComparison.OrdinalIgnoreCase), "Red confirmation omitted connectivity disruption warning.");
            Require(text.Contains("удал", StringComparison.OrdinalIgnoreCase), "Red confirmation omitted user-visible deletion warning.");
            Require(text.Contains("перезагруз", StringComparison.OrdinalIgnoreCase), "Red confirmation omitted reboot warning.");
            Require(text.Contains("UAC", StringComparison.OrdinalIgnoreCase), "Red confirmation omitted UAC warning.");
        });

        Test("English red confirmation localizes framing while preserving exact fixed IDs", () =>
        {
            var original = AppLocalization.Language;
            try
            {
                AppLocalization.SetLanguage("en");
                var text = ServiceDeskBatchUi.BuildDoEverythingConfirmation(FullPreflight());
                foreach (var id in ServiceDeskActionRegistry.All.Select(x => x.Id))
                    Require(text.Contains(id, StringComparison.Ordinal), "English red confirmation omitted fixed action: " + id);
                Require(!Regex.IsMatch(text, "[А-Яа-яЁё]"), "English red confirmation still contains Russian framing: " + text.ReplaceLineEndings(" | "));
                Require(text.Contains("UAC", StringComparison.OrdinalIgnoreCase), "English red confirmation omitted UAC warning.");
            }
            finally { AppLocalization.SetLanguage(original); }
        });

        Console.WriteLine($"Service Desk full phased batch self-test: {count - failures.Count}/{count} passed.");
        return failures.Count == 0 ? 0 : 215;
    }

    private static BatchPreflight FullPreflight()
        => ServiceDeskBatchPlanner.Plan(
            BatchMode.AllBestEffort,
            Array.Empty<ActionRecommendation>(),
            Array.Empty<string>(),
            id => ServiceDeskActionRegistry.Find(id)!.RequiresAdministrator
                ? new ActionAvailability("NeedsUac", "Synthetic UAC", "Synthetic")
                : new ActionAvailability("Ready", "Synthetic ready", "Synthetic"));

    private static ExecutionContextInfo SameUserContext()
        => new()
        {
            SessionId = 1,
            ProcessSid = "S-1-5-21-SYNTHETIC-SAME",
            SessionSid = "S-1-5-21-SYNTHETIC-SAME",
            ProcessAccount = "SYNTHETIC\\user",
            SessionAccount = "SYNTHETIC\\user",
            ProcessProfile = @"C:\Users\synthetic",
            SessionProfile = @"C:\Users\synthetic",
            ProfileSource = "synthetic",
            IsElevated = false,
            HasAdministratorToken = false,
            AdministratorMember = true,
            ElevationType = 3
        };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
