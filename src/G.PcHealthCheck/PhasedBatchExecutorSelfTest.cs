using System.Collections;
using System.Reflection;

namespace G.PcHealthCheck;

internal static class PhasedBatchExecutorSelfTest
{
    private static readonly string[] AllActions =
    [
        "CleanTemp", "FlushDns", "RegisterDns", "DhcpReleaseRenew", "WinsockReset", "TcpIpReset",
        "RestartNetworkAdapters", "RestartSpooler", "ClearPrintQueue", "RestartUpdateServices", "GpUpdate",
        "TimeResync", "Dism", "Sfc"
    ];

    public class RuntimeProxy : DispatchProxy
    {
        internal static readonly List<string> Events = [];
        internal static ExecutionContextInfo Context = SameUserContext();
        internal static bool CancelStart;
        internal static Func<string, string, Queue<object>>? MessageScript;

        internal static void Reset(ExecutionContextInfo context)
        {
            Events.Clear();
            Context = context;
            CancelStart = false;
            MessageScript = null;
            SessionProxy.Messages = new Queue<object>();
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null) throw new InvalidOperationException("Missing runtime method.");
            args ??= [];
            switch (targetMethod.Name)
            {
                case "CaptureParentContext":
                    Events.Add("CaptureParentContext");
                    return Context;
                case "StartWorker":
                {
                    var plan = args[0] ?? throw new InvalidOperationException("Worker phase plan missing.");
                    var session = Convert.ToString(args[1]) ?? "";
                    var nonce = Convert.ToString(args[2]) ?? "";
                    var requestElevation = Convert.ToBoolean(args[3]);
                    Events.Add("StartWorker:" + requestElevation);
                    Events.Add("WorkerBefore:" + string.Join(',', Values(plan, "WorkerBeforeNetwork")));
                    Events.Add("WorkerNetwork:" + string.Join(',', Values(plan, "WorkerNetwork")));
                    if (CancelStart) throw new OperationCanceledException("Synthetic UAC cancel.");
                    SessionProxy.Messages = MessageScript?.Invoke(session, nonce)
                        ?? throw new InvalidOperationException("Synthetic worker message script missing.");
                    return DispatchProxy.Create(targetMethod.ReturnType, typeof(SessionProxy));
                }
                case "ExecuteParentAction":
                {
                    var id = Convert.ToString(args[0]) ?? "";
                    Events.Add("Parent:" + id);
                    return new RemediationActionResult
                    {
                        Id = id,
                        Success = true,
                        Message = "Synthetic parent success",
                        StartedAt = DateTime.Now,
                        FinishedAt = DateTime.Now,
                        ExecutionContext = Context
                    };
                }
                default:
                    throw new InvalidOperationException("Unexpected runtime method: " + targetMethod.Name);
            }
        }
    }

    public class SessionProxy : DispatchProxy
    {
        internal static Queue<object> Messages = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null) throw new InvalidOperationException("Missing session method.");
            args ??= [];
            switch (targetMethod.Name)
            {
                case "Receive":
                    if (Messages.Count == 0) throw new InvalidOperationException("Synthetic worker message queue exhausted.");
                    var message = Messages.Dequeue();
                    RuntimeProxy.Events.Add("Receive:" + MessageType(message));
                    return message;
                case "Send":
                    RuntimeProxy.Events.Add("Send:" + MessageType(args[0] ?? throw new InvalidOperationException("Sent message missing.")));
                    return null;
                case "WaitForExit":
                    RuntimeProxy.Events.Add("WaitForExit");
                    return null;
                case "Dispose":
                    RuntimeProxy.Events.Add("DisposeWorker");
                    return null;
                default:
                    throw new InvalidOperationException("Unexpected session method: " + targetMethod.Name);
            }
        }
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

        var assembly = typeof(MainForm).Assembly;
        Type Need(string name) => assembly.GetType("G.PcHealthCheck." + name)
            ?? throw new InvalidOperationException(name + " is missing.");
        Type RuntimeType() => Need("IServiceDeskBatchRuntime");
        Type ExecutorType() => Need("ServiceDeskBatchExecutor");

        object Runtime(ExecutionContextInfo context, bool cancel = false, ScriptMode script = ScriptMode.Valid)
        {
            RuntimeProxy.Reset(context);
            RuntimeProxy.CancelStart = cancel;
            RuntimeProxy.MessageScript = (session, nonce) => Messages(session, nonce, script);
            return DispatchProxy.Create(RuntimeType(), typeof(RuntimeProxy));
        }

        object Execute(IReadOnlyCollection<string> ids, object runtime)
        {
            var method = ExecutorType().GetMethod("ExecuteCore", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("ServiceDeskBatchExecutor.ExecuteCore is missing.");
            return method.Invoke(null, [ids, 3, runtime])
                ?? throw new InvalidOperationException("ExecuteCore returned no result.");
        }

        Test("one worker spans machine parent network and final-parent phases", () =>
        {
            var runtime = Runtime(SameUserContext());
            _ = Execute(AllActions, runtime);
            var e = RuntimeProxy.Events;
            Require(e.Count(x => x == "StartWorker:True") == 1, "Administrative batch did not start exactly one elevated worker.");
            Require(e.Single(x => x.StartsWith("WorkerBefore:", StringComparison.Ordinal)).Contains("GpUpdate", StringComparison.Ordinal), "Machine GpUpdate half is absent from worker Phase A.");
            Require(!e.Single(x => x.StartsWith("WorkerBefore:", StringComparison.Ordinal)).Contains("CleanTemp", StringComparison.Ordinal)
                && !e.Single(x => x.StartsWith("WorkerNetwork:", StringComparison.Ordinal)).Contains("FlushDns", StringComparison.Ordinal),
                "Parent-only action leaked into worker plan.");
            Require(Index(e, "Receive:Ready") < Index(e, "Send:Ready") && Index(e, "Send:Ready") < Index(e, "Receive:BeforeNetwork"),
                "Worker was not authenticated/authorized before Phase A checkpoint.");
            Require(Index(e, "Receive:BeforeNetwork") < Index(e, "Parent:GpUpdate")
                && Index(e, "Parent:GpUpdate") < Index(e, "Parent:CleanTemp")
                && Index(e, "Parent:CleanTemp") < Index(e, "Send:ContinueNetwork"),
                "Original-user phase did not stay between worker machine and network phases.");
            Require(Index(e, "Send:ContinueNetwork") < Index(e, "Receive:FinalResult")
                && Index(e, "Receive:FinalResult") < Index(e, "Parent:FlushDns"),
                "Final parent DNS flush ran before authenticated network completion.");
        });

        Test("cancelled UAC starts no parent mutation", () =>
        {
            var runtime = Runtime(SameUserContext(), cancel: true);
            try { _ = Execute(new[] { "Dism", "CleanTemp", "FlushDns" }, runtime); }
            catch (TargetInvocationException ex) when (ex.InnerException is OperationCanceledException) { }
            Require(RuntimeProxy.Events.Count(x => x.StartsWith("StartWorker:", StringComparison.Ordinal)) == 1, "Synthetic UAC was not requested exactly once.");
            Require(!RuntimeProxy.Events.Any(x => x.StartsWith("Parent:", StringComparison.Ordinal)), "Parent mutation ran after cancelled UAC.");
            Require(!RuntimeProxy.Events.Any(x => x.StartsWith("Receive:", StringComparison.Ordinal) || x.StartsWith("Send:", StringComparison.Ordinal)), "Protocol advanced after cancelled worker start.");
        });

        Test("invalid authenticated phase cannot advance into parent or network", () =>
        {
            var runtime = Runtime(SameUserContext(), script: ScriptMode.WrongBeforeNetworkType);
            try { _ = Execute(new[] { "Dism", "CleanTemp", "WinsockReset", "FlushDns" }, runtime); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException or InvalidOperationException) { }
            Require(RuntimeProxy.Events.Contains("Receive:Ready") && RuntimeProxy.Events.Contains("Send:Ready"), "Initial worker handshake was not exercised.");
            Require(!RuntimeProxy.Events.Any(x => x.StartsWith("Parent:", StringComparison.Ordinal)), "Parent mutation ran after invalid worker phase message.");
            Require(!RuntimeProxy.Events.Contains("Send:ContinueNetwork"), "Network phase advanced after invalid worker checkpoint.");
        });

        Test("already elevated parent starts worker without a second UAC", () =>
        {
            var elevated = SameUserContext() with { HasAdministratorToken = true, IsElevated = true, AdministratorMember = true, ElevationType = 2 };
            var runtime = Runtime(elevated);
            _ = Execute(new[] { "Dism", "Sfc" }, runtime);
            Require(RuntimeProxy.Events.Count(x => x == "StartWorker:False") == 1, "Already-elevated path requested another UAC or skipped the single worker lifetime.");
            Require(!RuntimeProxy.Events.Contains("StartWorker:True"), "Already-elevated path requested UAC.");
        });

        Console.WriteLine($"Phased batch executor self-test: {count - failures.Count}/{count} passed.");
        return failures.Count == 0 ? 0 : 219;
    }

    private enum ScriptMode { Valid, WrongBeforeNetworkType }

    private static Queue<object> Messages(string session, string nonce, ScriptMode mode)
    {
        var assembly = typeof(MainForm).Assembly;
        var messageType = assembly.GetType("G.PcHealthCheck.WorkerMessageType")
            ?? throw new InvalidOperationException("WorkerMessageType is missing.");
        var workerMessage = assembly.GetType("G.PcHealthCheck.WorkerMessage")
            ?? throw new InvalidOperationException("WorkerMessage is missing.");
        object Message(string phase, RemediationBatchResult? result = null)
        {
            var value = Activator.CreateInstance(workerMessage)
                ?? throw new InvalidOperationException("Cannot create WorkerMessage.");
            workerMessage.GetProperty("SessionId")!.SetValue(value, session);
            workerMessage.GetProperty("Nonce")!.SetValue(value, nonce);
            workerMessage.GetProperty("Type")!.SetValue(value, Enum.Parse(messageType, phase));
            workerMessage.GetProperty("Result")!.SetValue(value, result);
            return value;
        }
        var partial = new RemediationBatchResult { SessionId = session, StartedAt = DateTime.Now, FinishedAt = DateTime.Now };
        var final = new RemediationBatchResult { SessionId = session, StartedAt = DateTime.Now, FinishedAt = DateTime.Now };
        return new Queue<object>(
        [
            Message("Ready"),
            Message(mode == ScriptMode.Valid ? "BeforeNetwork" : "FinalResult", partial),
            Message("FinalResult", final)
        ]);
    }

    private static string MessageType(object message)
        => Convert.ToString(message.GetType().GetProperty("Type")?.GetValue(message)) ?? "?";

    private static IReadOnlyList<string> Values(object plan, string property)
    {
        var value = plan.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(plan)
            ?? throw new InvalidOperationException("Phase plan property missing: " + property);
        return ((IEnumerable)value).Cast<object>().Select(x => Convert.ToString(x) ?? "").ToList();
    }

    private static int Index(IReadOnlyList<string> events, string value)
    {
        for (var i = 0; i < events.Count; i++) if (events[i] == value) return i;
        throw new InvalidOperationException("Expected event missing: " + value + ". Actual: " + string.Join(" | ", events));
    }

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
            IsElevated = false,
            HasAdministratorToken = false,
            AdministratorMember = false
        };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
