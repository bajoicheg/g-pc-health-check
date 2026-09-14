using System.Collections;
using System.Reflection;

namespace G.PcHealthCheck;

internal static class PhasedWorkerSelfTest
{
    private static readonly string[] AllActions =
    [
        "CleanTemp", "FlushDns", "RegisterDns", "DhcpReleaseRenew", "WinsockReset", "TcpIpReset",
        "RestartNetworkAdapters", "RestartSpooler", "ClearPrintQueue", "RestartUpdateServices", "GpUpdate",
        "TimeResync", "Dism", "Sfc"
    ];

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

        Test("worker protocol has exact authenticated phase messages", () =>
        {
            var messageType = Need("WorkerMessageType");
            Require(messageType.IsEnum, "WorkerMessageType is not an enum.");
            Require(Enum.GetNames(messageType).SequenceEqual(new[] { "Ready", "BeforeNetwork", "ContinueNetwork", "FinalResult" }),
                "Worker message phase names/order changed.");

            var message = Need("WorkerMessage");
            foreach (var property in new[] { "SessionId", "Nonce", "Type", "Result" })
                Require(message.GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null,
                    "WorkerMessage." + property + " is missing.");
        });

        Test("worker protocol rejects wrong session nonce and out-of-order phase", () =>
        {
            var messageType = Need("WorkerMessageType");
            var workerMessage = Need("WorkerMessage");
            var protocol = Need("WorkerProtocol");
            var validate = protocol.GetMethod("ValidateMessage", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("WorkerProtocol.ValidateMessage is missing.");
            var session = "00000000-1111-2222-3333-444444444444";
            var nonce = new string('A', 64);
            object Message(string s, string n, string phase)
            {
                var value = Activator.CreateInstance(workerMessage)
                    ?? throw new InvalidOperationException("Cannot create WorkerMessage.");
                workerMessage.GetProperty("SessionId")!.SetValue(value, s);
                workerMessage.GetProperty("Nonce")!.SetValue(value, n);
                workerMessage.GetProperty("Type")!.SetValue(value, Enum.Parse(messageType, phase));
                return value;
            }
            void Validate(object value, string expectedPhase)
                => validate.Invoke(null, [value, session, nonce, Enum.Parse(messageType, expectedPhase)]);
            void Reject(object value, string expectedPhase)
            {
                try { Validate(value, expectedPhase); }
                catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException or InvalidOperationException) { return; }
                throw new InvalidOperationException("Invalid worker message was accepted.");
            }

            Validate(Message(session, nonce, "Ready"), "Ready");
            Reject(Message("00000000-1111-2222-3333-555555555555", nonce, "Ready"), "Ready");
            Reject(Message(session, new string('B', 64), "Ready"), "Ready");
            Reject(Message(session, nonce, "FinalResult"), "BeforeNetwork");
        });

        Test("phase plan keeps user work in parent and network disruption late", () =>
        {
            var executor = Need("ServiceDeskBatchExecutor");
            var build = executor.GetMethod("BuildPhasePlan", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("ServiceDeskBatchExecutor.BuildPhasePlan is missing.");
            var context = SameUserContext();
            var plan = build.Invoke(null, [AllActions, context])
                ?? throw new InvalidOperationException("BuildPhasePlan returned null.");

            var before = Values(plan, "WorkerBeforeNetwork");
            var parent = Values(plan, "ParentBeforeNetwork");
            var network = Values(plan, "WorkerNetwork");
            var after = Values(plan, "ParentAfterWorker");

            Require(before.SequenceEqual(new[] { "ClearPrintQueue", "RestartUpdateServices", "TimeResync", "GpUpdate", "Dism", "Sfc" }),
                "Worker-before-network phase differs from approved order/coalescing.");
            Require(parent.SequenceEqual(new[] { "GpUpdate", "CleanTemp" }),
                "Original-user phase must be GpUpdate user half then CleanTemp.");
            Require(network.SequenceEqual(new[] { "WinsockReset", "TcpIpReset", "RestartNetworkAdapters", "DhcpReleaseRenew", "RegisterDns" }),
                "Network phase differs from approved late-disruption order.");
            Require(after.SequenceEqual(new[] { "FlushDns" }), "Final parent phase must contain only FlushDns.");
            Require(!before.Contains("CleanTemp") && !before.Contains("FlushDns") && !network.Contains("CleanTemp") && !network.Contains("FlushDns"),
                "Parent-only action leaked into elevated worker phases.");
            Require(!network.Contains("GpUpdate"), "GpUpdate leaked into the network-disruptive phase.");
        });

        Test("full GpUpdate requires verified original-user parent identity", () =>
        {
            var executor = Need("ServiceDeskBatchExecutor");
            var build = executor.GetMethod("BuildPhasePlan", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("ServiceDeskBatchExecutor.BuildPhasePlan is missing.");
            var mismatch = SameUserContext() with { ProcessSid = "S-1-5-21-SYNTHETIC-PROCESS", SessionSid = "S-1-5-21-SYNTHETIC-SESSION" };
            try { _ = build.Invoke(null, [new[] { "GpUpdate" }, mismatch]); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { return; }
            throw new InvalidOperationException("GpUpdate phase plan accepted an unverified original-user parent identity.");
        });

        Console.WriteLine($"Phased worker protocol/planning self-test: {count - failures.Count}/{count} passed.");
        return failures.Count == 0 ? 0 : 221;
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

    private static IReadOnlyList<string> Values(object plan, string property)
    {
        var value = plan.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(plan)
            ?? throw new InvalidOperationException("Phase plan property missing: " + property);
        return ((IEnumerable)value).Cast<object>().Select(x => Convert.ToString(x) ?? "").ToList();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
