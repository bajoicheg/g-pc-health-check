using System.Reflection;

namespace G.PcHealthCheck;

internal static class PhasedWorkerEngineSelfTest
{
    private static readonly List<string> Trace = [];

    public class ChannelProxy : DispatchProxy
    {
        internal static Queue<WorkerMessage> Incoming = new();

        internal static void Reset(IEnumerable<WorkerMessage> incoming)
            => Incoming = new Queue<WorkerMessage>(incoming);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null) throw new InvalidOperationException("Missing channel method.");
            args ??= [];
            switch (targetMethod.Name)
            {
                case "Send":
                {
                    var message = args[0] as WorkerMessage ?? throw new InvalidOperationException("Synthetic sent message missing.");
                    Trace.Add("Send:" + message.Type);
                    return null;
                }
                case "Receive":
                {
                    if (Incoming.Count == 0) throw new InvalidOperationException("Synthetic parent message queue exhausted.");
                    var message = Incoming.Dequeue();
                    Trace.Add("Receive:" + message.Type);
                    return message;
                }
                default:
                    throw new InvalidOperationException("Unexpected channel method: " + targetMethod.Name);
            }
        }
    }

    public class OperationsProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null) throw new InvalidOperationException("Missing operations method.");
            args ??= [];
            if (targetMethod.Name != "RunFixedCommand")
                throw new InvalidOperationException("Unexpected synthetic operation: " + targetMethod.Name);

            var command = Convert.ToString(args[0]) ?? "";
            Trace.Add("Operation:" + command);
            var result = Activator.CreateInstance(targetMethod.ReturnType)
                ?? throw new InvalidOperationException("Cannot create synthetic native action result.");
            Set(result, "Id", command);
            Set(result, "Success", true);
            Set(result, "ExitCode", 0);
            Set(result, "Message", "Synthetic success");
            Set(result, "Output", "synthetic");
            Set(result, "RebootRecommended", command is "WinsockReset" or "TcpIpReset");
            return result;
        }

        private static void Set(object target, string property, object? value)
            => target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .SetValue(target, value);
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
        Type ChannelType() => Need("IWorkerMessageChannel");
        Type EngineType() => Need("ServiceDeskWorkerEngine");
        Type OpsType() => Need("IWindowsRepairOperations");

        object Channel(IEnumerable<WorkerMessage> incoming)
        {
            Trace.Clear();
            ChannelProxy.Reset(incoming);
            return DispatchProxy.Create(ChannelType(), typeof(ChannelProxy));
        }

        object Operations()
            => DispatchProxy.Create(OpsType(), typeof(OperationsProxy));

        RemediationBatchResult Execute(ServiceDeskPhasePlan plan, string session, string nonce, object channel, object operations)
        {
            var method = EngineType().GetMethod("Execute", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("ServiceDeskWorkerEngine.Execute is missing.");
            return (RemediationBatchResult)(method.Invoke(null, [plan, session, nonce, channel, operations, WorkerContext()])
                ?? throw new InvalidOperationException("Worker engine returned no result."));
        }

        var session = "00000000-1111-2222-3333-444444444444";
        var nonce = new string('A', 64);
        WorkerMessage Message(WorkerMessageType type, string? n = null, string? s = null)
            => new() { SessionId = s ?? session, Nonce = n ?? nonce, Type = type };

        Test("worker authenticates parent before Phase A and network waits for continuation", () =>
        {
            var plan = Plan(["GpUpdate", "Dism", "Sfc"], ["WinsockReset", "RegisterDns"]);
            var channel = Channel([Message(WorkerMessageType.Ready), Message(WorkerMessageType.ContinueNetwork)]);
            var operations = Operations();
            var result = Execute(plan, session, nonce, channel, operations);

            Require(Index("Send:Ready") < Index("Receive:Ready"), "Worker did not announce Ready before waiting for parent authentication.");
            Require(Index("Receive:Ready") < Index("Operation:GpUpdateComputer"), "Phase A started before authenticated parent Ready.");
            Require(Index("Operation:Sfc") < Index("Send:BeforeNetwork"), "BeforeNetwork checkpoint was sent before Phase A completed.");
            Require(Index("Send:BeforeNetwork") < Index("Receive:ContinueNetwork"), "Worker did not pause for ContinueNetwork.");
            Require(Index("Receive:ContinueNetwork") < Index("Operation:WinsockReset"), "Network mutation started before authenticated ContinueNetwork.");
            Require(Index("Operation:RegisterDns") < Index("Send:FinalResult"), "FinalResult was sent before network phase completed.");
            Require(result.SessionId == session, "Worker result lost the authenticated session ID.");
            Require(result.Actions.Select(x => x.Id).SequenceEqual(new[] { "GpUpdate", "Dism", "Sfc", "WinsockReset", "RegisterDns" }), "Worker result action order changed.");
        });

        Test("wrong parent nonce blocks Phase A before any native operation", () =>
        {
            var plan = Plan(["Dism"], ["WinsockReset"]);
            var channel = Channel([Message(WorkerMessageType.Ready, n: new string('B', 64))]);
            var operations = Operations();
            try { _ = Execute(plan, session, nonce, channel, operations); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException or InvalidOperationException) { }
            Require(!Trace.Any(x => x.StartsWith("Operation:", StringComparison.Ordinal)), "Native operation ran after invalid parent nonce.");
            Require(!Trace.Contains("Send:BeforeNetwork"), "Worker advanced after invalid parent nonce.");
        });

        Test("wrong continuation cannot enter disruptive network phase", () =>
        {
            var plan = Plan(["Dism"], ["WinsockReset", "RegisterDns"]);
            var channel = Channel([Message(WorkerMessageType.Ready), Message(WorkerMessageType.FinalResult)]);
            var operations = Operations();
            try { _ = Execute(plan, session, nonce, channel, operations); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException or InvalidOperationException) { }
            Require(Trace.Where(x => x.StartsWith("Operation:", StringComparison.Ordinal)).SequenceEqual(new[] { "Operation:Dism" }), "Network phase ran without ContinueNetwork or Phase A order changed.");
            Require(!Trace.Contains("Send:FinalResult"), "Worker produced FinalResult after invalid continuation.");
        });

        Test("mixed unknown or parent-only worker IDs are rejected before protocol and mutations", () =>
        {
            foreach (var plan in new[]
            {
                Plan(["Dism", "DefinitelyNotAllowed"], []),
                Plan(["CleanTemp"], []),
                Plan([], ["FlushDns"])
            })
            {
                var channel = Channel([Message(WorkerMessageType.Ready), Message(WorkerMessageType.ContinueNetwork)]);
                var operations = Operations();
                try { _ = Execute(plan, session, nonce, channel, operations); }
                catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException or InvalidOperationException) { }
                Require(Trace.Count == 0, "Worker entered protocol/native execution after invalid fixed-ID plan: " + string.Join(" | ", Trace));
            }
        });

        Console.WriteLine($"Phased worker engine self-test: {count - failures.Count}/{count} passed.");
        return failures.Count == 0 ? 0 : 218;
    }

    private static ServiceDeskPhasePlan Plan(IReadOnlyList<string> before, IReadOnlyList<string> network)
        => new()
        {
            WorkerBeforeNetwork = before,
            WorkerNetwork = network,
            ParentBeforeNetwork = [],
            ParentAfterWorker = []
        };

    private static ExecutionContextInfo WorkerContext()
        => new()
        {
            SessionId = 1,
            ProcessSid = "S-1-5-21-SYNTHETIC-WORKER",
            SessionSid = "S-1-5-21-SYNTHETIC-SESSION",
            ProcessAccount = "SYNTHETIC\\admin",
            SessionAccount = "SYNTHETIC\\user",
            IsElevated = true,
            HasAdministratorToken = true,
            AdministratorMember = true,
            ElevationType = 2
        };

    private static int Index(string value)
    {
        var index = Trace.IndexOf(value);
        if (index >= 0) return index;
        throw new InvalidOperationException("Expected event missing: " + value + ". Actual: " + string.Join(" | ", Trace));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
