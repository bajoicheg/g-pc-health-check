using System.Collections;
using System.Reflection;

namespace G.PcHealthCheck;

internal static class WindowsBatchRuntimeSelfTest
{
    public class FakeOpsProxy : DispatchProxy
    {
        internal static readonly List<string> Calls = [];

        internal static void Reset() => Calls.Clear();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null) throw new InvalidOperationException("Missing fake operation method.");
            args ??= [];
            if (targetMethod.Name != "RunFixedCommand")
                throw new InvalidOperationException("Unexpected parent fake operation: " + targetMethod.Name);
            var id = Convert.ToString(args[0]) ?? "";
            Calls.Add("RunFixedCommand:" + id);
            var result = Activator.CreateInstance(targetMethod.ReturnType)
                ?? throw new InvalidOperationException("Cannot create synthetic native result.");
            Set(result, "Id", id);
            Set(result, "Success", true);
            Set(result, "ExitCode", 0);
            Set(result, "Message", "Synthetic success");
            Set(result, "Output", "synthetic");
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
        Type RuntimeType() => Need("WindowsServiceDeskBatchRuntime");
        Type SessionType() => Need("WindowsServiceDeskWorkerSession");

        Test("user Group Policy is a fixed command with exact arguments", () =>
        {
            var commands = Need("FixedCommand");
            var native = Need("WindowsRepairOperations");
            var value = Enum.Parse(commands, "GpUpdateUser");
            var method = native.GetMethod("GetCommandSpec", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("GetCommandSpec missing.");
            var spec = method.Invoke(null, [value]) ?? throw new InvalidOperationException("GpUpdateUser command spec missing.");
            var file = Convert.ToString(spec.GetType().GetProperty("FileName")?.GetValue(spec)) ?? "";
            var arguments = Convert.ToString(spec.GetType().GetProperty("Arguments")?.GetValue(spec)) ?? "";
            Require(file.EndsWith("gpupdate.exe", StringComparison.OrdinalIgnoreCase), "GpUpdateUser executable is not fixed gpupdate.exe.");
            Require(arguments == "/target:user /force /wait:60", "GpUpdateUser arguments changed: " + arguments);
        });

        Test("concrete runtime implements batch and worker-session boundaries", () =>
        {
            Require(typeof(IServiceDeskBatchRuntime).IsAssignableFrom(RuntimeType()), "WindowsServiceDeskBatchRuntime does not implement IServiceDeskBatchRuntime.");
            Require(typeof(IServiceDeskWorkerSession).IsAssignableFrom(SessionType()), "WindowsServiceDeskWorkerSession does not implement IServiceDeskWorkerSession.");
            Require(RuntimeType().GetConstructor(Type.EmptyTypes) is not null, "WindowsServiceDeskBatchRuntime default constructor is missing.");
        });

        Test("parent actions route only to fixed user-GP CleanTemp and final FlushDns", () =>
        {
            var core = RuntimeType().GetMethod("ExecuteParentActionCore", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("WindowsServiceDeskBatchRuntime.ExecuteParentActionCore is missing.");
            var original = SameUserContext();
            var current = SameUserContext();
            var fake = DispatchProxy.Create(typeof(IWindowsRepairOperations), typeof(FakeOpsProxy));
            var cleaned = new List<ExecutionContextInfo>();
            Func<ExecutionContextInfo, int, RemediationActionResult> cleaner = (context, days) =>
            {
                cleaned.Add(context);
                return new RemediationActionResult { Id = "CleanTemp", Success = true, DeletedFiles = days, Message = "Synthetic clean" };
            };

            RemediationActionResult Run(string id)
                => (RemediationActionResult)(core.Invoke(null, [id, 3, original, current, fake, cleaner])
                    ?? throw new InvalidOperationException("Parent action returned no result."));

            FakeOpsProxy.Reset();
            var gp = Run("GpUpdate");
            Require(gp.Success && gp.Id == "GpUpdate" && FakeOpsProxy.Calls.SequenceEqual(new[] { "RunFixedCommand:GpUpdateUser" }),
                "Parent GpUpdate did not use only the fixed user-policy command.");

            FakeOpsProxy.Reset(); cleaned.Clear();
            var clean = Run("CleanTemp");
            Require(clean.Success && cleaned.Count == 1 && ReferenceEquals(cleaned[0], current) && FakeOpsProxy.Calls.Count == 0,
                "CleanTemp did not stay on the verified current parent context.");

            FakeOpsProxy.Reset(); cleaned.Clear();
            var flush = Run("FlushDns");
            Require(flush.Success && flush.Id == "FlushDns" && FakeOpsProxy.Calls.SequenceEqual(new[] { "RunFixedCommand:FlushDns" }) && cleaned.Count == 0,
                "Final parent FlushDns did not use only the fixed flush command.");

            try { _ = Run("Dism"); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { return; }
            throw new InvalidOperationException("Non-parent worker action was accepted by parent action boundary.");
        });

        Test("user-bound parent actions reject identity drift and elevated Temp before mutation", () =>
        {
            var core = RuntimeType().GetMethod("ExecuteParentActionCore", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("WindowsServiceDeskBatchRuntime.ExecuteParentActionCore is missing.");
            var original = SameUserContext();
            var fake = DispatchProxy.Create(typeof(IWindowsRepairOperations), typeof(FakeOpsProxy));
            var cleanCalls = 0;
            Func<ExecutionContextInfo, int, RemediationActionResult> cleaner = (_, _) =>
            {
                cleanCalls++;
                return new RemediationActionResult { Id = "CleanTemp", Success = true };
            };

            void Reject(string id, ExecutionContextInfo current)
            {
                FakeOpsProxy.Reset(); cleanCalls = 0;
                try { _ = core.Invoke(null, [id, 3, original, current, fake, cleaner]); }
                catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { }
                Require(FakeOpsProxy.Calls.Count == 0 && cleanCalls == 0, $"{id} mutated after parent identity/safety drift.");
            }

            Reject("GpUpdate", original with
            {
                SessionId = 2,
                ProcessSid = "S-1-5-21-SYNTHETIC-OTHER",
                SessionSid = "S-1-5-21-SYNTHETIC-OTHER"
            });
            Reject("CleanTemp", original with { IsElevated = true, HasAdministratorToken = true, ElevationType = 2 });
            Reject("CleanTemp", original with { SessionId = 2 });
        });

        Test("worker launch request contains only fixed worker phases and preserves order", () =>
        {
            var build = RuntimeType().GetMethod("BuildWorkerRequest", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("WindowsServiceDeskBatchRuntime.BuildWorkerRequest is missing.");
            var plan = new ServiceDeskPhasePlan
            {
                WorkerBeforeNetwork = ["ClearPrintQueue", "GpUpdate", "Dism", "Sfc"],
                ParentBeforeNetwork = ["GpUpdate", "CleanTemp"],
                WorkerNetwork = ["WinsockReset", "RegisterDns"],
                ParentAfterWorker = ["FlushDns"]
            };
            var session = "00000000-1111-2222-3333-444444444444";
            var nonce = new string('A', 64);
            var request = build.Invoke(null, [plan, session, nonce])
                ?? throw new InvalidOperationException("BuildWorkerRequest returned null.");
            var actionIds = Values(request, "ActionIds");
            var pipeName = Convert.ToString(Value(request, "PipeName")) ?? "";
            var arguments = Convert.ToString(Value(request, "Arguments")) ?? "";

            Require(actionIds.SequenceEqual(new[] { "ClearPrintQueue", "GpUpdate", "Dism", "Sfc", "WinsockReset", "RegisterDns" }),
                "Worker request action order or partition changed.");
            Require(!actionIds.Contains("CleanTemp") && !actionIds.Contains("FlushDns"), "Parent-only action leaked into worker request.");
            Require(pipeName == "GPcHealthCheck-" + session, "Worker request pipe is not session-bound.");
            Require(arguments.Contains("--phased-worker", StringComparison.Ordinal)
                    && arguments.Contains(session, StringComparison.Ordinal)
                    && arguments.Contains("ClearPrintQueue,GpUpdate,Dism,Sfc,WinsockReset,RegisterDns", StringComparison.Ordinal)
                    && arguments.Contains(pipeName, StringComparison.Ordinal)
                    && arguments.Contains(nonce, StringComparison.Ordinal),
                "Worker request arguments lost fixed phased/session/nonce binding.");
        });

        Console.WriteLine($"Windows Service Desk batch runtime self-test: {count - failures.Count}/{count} passed.");
        return failures.Count == 0 ? 0 : 216;
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
            ProfileSource = "synthetic",
            IsElevated = false,
            HasAdministratorToken = false,
            AdministratorMember = false,
            ElevationType = 3
        };

    private static object Value(object target, string property)
        => target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target)
            ?? throw new InvalidOperationException("Property missing: " + property);

    private static IReadOnlyList<string> Values(object target, string property)
        => ((IEnumerable)Value(target, property)).Cast<object>().Select(x => Convert.ToString(x) ?? "").ToList();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
