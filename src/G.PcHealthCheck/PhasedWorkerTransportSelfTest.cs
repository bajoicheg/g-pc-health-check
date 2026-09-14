using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Text;

namespace G.PcHealthCheck;

internal static class PhasedWorkerTransportSelfTest
{
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

        var worker = typeof(RemediationWorker);
        var session = "00000000-1111-2222-3333-444444444444";
        var pipe = "GPcHealthCheck-" + session;
        var nonce = new string('A', 64);
        string[] Args(string actions, string? pipeOverride = null, string? nonceOverride = null)
            => ["--phased-worker", "--session", session, "--actions", actions, "--temp-days", "3", "--pipe", pipeOverride ?? pipe, "--nonce", nonceOverride ?? nonce];

        Test("phased CLI rejects invalid requests before pipe connection or mutation", () =>
        {
            var run = worker.GetMethod("RunPhased", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("RemediationWorker.RunPhased is missing.");
            int Invoke(string[] args) => Convert.ToInt32(run.Invoke(null, [args]));

            Require(Invoke(["--phased-worker"]) == 20, "Missing phased-worker session did not return 20.");
            foreach (var actions in new[] { "", "DefinitelyNotAllowed", "CleanTemp", "FlushDns", "Dism,DefinitelyNotAllowed", "GpUpdate,FlushDns" })
                Require(Invoke(Args(actions)) == 21, $"Invalid phased action list [{actions}] did not return 21.");
            Require(Invoke(Args("Dism", pipeOverride: "bad-pipe")) == 24, "Invalid phased-worker pipe did not return 24.");
            Require(Invoke(Args("Sfc", nonceOverride: "bad-nonce")) == 25, "Invalid phased-worker nonce did not return 25.");
        });

        Test("worker phase parser partitions fixed IDs and coalesces redundant Spooler restart", () =>
        {
            var parse = worker.GetMethod("TryBuildWorkerPhasePlan", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("RemediationWorker.TryBuildWorkerPhasePlan is missing.");

            bool Try(string csv, out object? plan)
            {
                object?[] args = [csv, null];
                var ok = Convert.ToBoolean(parse.Invoke(null, args));
                plan = args[1];
                return ok;
            }

            Require(Try("RegisterDns,GpUpdate,Dism,RestartSpooler,ClearPrintQueue,WinsockReset,Sfc", out var plan) && plan is not null,
                "Approved fixed phased worker list was rejected.");
            Require(Values(plan!, "WorkerBeforeNetwork").SequenceEqual(new[] { "ClearPrintQueue", "GpUpdate", "Dism", "Sfc" }),
                "Before-network partition/coalescing changed.");
            Require(Values(plan!, "WorkerNetwork").SequenceEqual(new[] { "WinsockReset", "RegisterDns" }),
                "Network partition/order changed.");
            Require(Values(plan!, "ParentBeforeNetwork").Count == 0 && Values(plan!, "ParentAfterWorker").Count == 0,
                "Parent-only actions entered phased worker plan.");

            foreach (var invalid in new[] { "", "CleanTemp", "FlushDns", "DefinitelyNotAllowed", "Dism,DefinitelyNotAllowed", "GpUpdate,CleanTemp" })
                Require(!Try(invalid, out _), $"Invalid phased worker list was accepted: {invalid}.");
        });

        Test("JSON worker message channel round-trips one bounded framed message", () =>
        {
            var type = typeof(MainForm).Assembly.GetType("G.PcHealthCheck.JsonWorkerMessageChannel")
                ?? throw new InvalidOperationException("JsonWorkerMessageChannel is missing.");
            using var stream = new MemoryStream();
            var channel = Activator.CreateInstance(type, [stream, true])
                ?? throw new InvalidOperationException("Cannot create JsonWorkerMessageChannel.");
            var send = type.GetMethod("Send", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("JsonWorkerMessageChannel.Send is missing.");
            var receive = type.GetMethod("Receive", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("JsonWorkerMessageChannel.Receive is missing.");

            var message = new WorkerMessage
            {
                SessionId = session,
                Nonce = nonce,
                Type = WorkerMessageType.BeforeNetwork,
                Result = new RemediationBatchResult
                {
                    SessionId = session,
                    Actions = [new RemediationActionResult { Id = "Dism", Success = true, Message = "synthetic" }]
                }
            };
            send.Invoke(channel, [message]);
            Require(stream.Length > 0 && stream.Length < 128 * 1024, "Synthetic framed message size is invalid.");
            stream.Position = 0;
            var copy = (WorkerMessage)(receive.Invoke(channel, null)
                ?? throw new InvalidOperationException("Round-trip returned no WorkerMessage."));
            Require(copy.SessionId == session && copy.Nonce == nonce && copy.Type == WorkerMessageType.BeforeNetwork,
                "Worker message envelope changed during round-trip.");
            Require(copy.Result?.SessionId == session && copy.Result.Actions.Count == 1 && copy.Result.Actions[0].Id == "Dism",
                "Worker message result payload changed during round-trip.");

            (channel as IDisposable)?.Dispose();
        });

        Test("duplex pipe and worker launch factories preserve portable path and one-UAC semantics", () =>
        {
            var createPipe = worker.GetMethod("CreatePipeServer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("RemediationWorker.CreatePipeServer is missing.");
            using (var server = (NamedPipeServerStream)(createPipe.Invoke(null, ["GPcHealthCheck-" + Guid.NewGuid().ToString("D")])
                ?? throw new InvalidOperationException("CreatePipeServer returned null.")))
            {
                Require(server.CanRead && server.CanWrite, "Phased worker pipe is not duplex.");
                Require(server.TransmissionMode == PipeTransmissionMode.Byte, "Unexpected pipe transmission mode.");
            }

            var createStart = worker.GetMethod("CreateWorkerStartInfo", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("RemediationWorker.CreateWorkerStartInfo is missing.");
            var path = @"D:\Portable tools\Проверка ПК [test].exe";
            var arguments = "--phased-worker --session synthetic";
            ProcessStartInfo Start(bool elevate) => (ProcessStartInfo)(createStart.Invoke(null, [path, arguments, elevate])
                ?? throw new InvalidOperationException("CreateWorkerStartInfo returned null."));

            var elevated = Start(true);
            Require(elevated.FileName == path && elevated.Arguments == arguments, "Elevated worker rewrote portable path/arguments.");
            Require(elevated.UseShellExecute && elevated.Verb == "runas", "Required UAC launch does not use runas.");
            Require(elevated.WorkingDirectory == Environment.SystemDirectory, "Elevated worker inherited a writable working directory.");

            var activeAdmin = Start(false);
            Require(activeAdmin.FileName == path && activeAdmin.Arguments == arguments, "Already-admin worker rewrote portable path/arguments.");
            Require(!activeAdmin.UseShellExecute && string.IsNullOrEmpty(activeAdmin.Verb) && activeAdmin.CreateNoWindow,
                "Already-admin worker unexpectedly requests a second UAC or opens a console.");
            Require(activeAdmin.WorkingDirectory == Environment.SystemDirectory, "Already-admin worker inherited a writable working directory.");
        });

        Console.WriteLine($"Phased worker transport/CLI self-test: {count - failures.Count}/{count} passed.");
        return failures.Count == 0 ? 0 : 217;
    }

    private static IReadOnlyList<string> Values(object plan, string property)
    {
        var value = plan.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(plan)
            ?? throw new InvalidOperationException("Phase plan property missing: " + property);
        return ((System.Collections.IEnumerable)value).Cast<object>().Select(x => Convert.ToString(x) ?? "").ToList();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
