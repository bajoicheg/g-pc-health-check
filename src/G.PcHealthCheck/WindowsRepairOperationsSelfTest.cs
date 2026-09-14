using System.Collections;
using System.Reflection;

namespace G.PcHealthCheck;

internal static class WindowsRepairOperationsSelfTest
{
    public class FakeProxy : DispatchProxy
    {
        internal static readonly List<string> Calls = [];
        internal static readonly Dictionary<string, (string State, string StartMode)> Services = new(StringComparer.OrdinalIgnoreCase);

        internal static void Reset()
        {
            Calls.Clear();
            Services.Clear();
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null) throw new InvalidOperationException("Missing fake target method.");
            args ??= [];
            switch (targetMethod.Name)
            {
                case "RunFixedCommand":
                {
                    var id = args[0]?.ToString() ?? "";
                    Calls.Add("RunFixedCommand:" + id);
                    return New(targetMethod.ReturnType,
                        ("Id", id), ("Success", true), ("ExitCode", 0), ("Message", "Synthetic success"), ("Output", "synthetic"));
                }
                case "QueryService":
                {
                    var service = args[0]?.ToString() ?? "";
                    Calls.Add("QueryService:" + service);
                    var state = Services.TryGetValue(service, out var configured) ? configured : ("Running", "Automatic");
                    return New(targetMethod.ReturnType,
                        ("Service", args[0]), ("State", state.Item1), ("StartMode", state.Item2));
                }
                case "RestartService":
                {
                    var service = args[0]?.ToString() ?? "";
                    var startIfStopped = args.Length > 1 && Convert.ToBoolean(args[1]);
                    Calls.Add($"RestartService:{service}:{startIfStopped}");
                    return New(targetMethod.ReturnType,
                        ("Id", service), ("Success", true), ("ExitCode", 0), ("Message", "Synthetic service success"));
                }
                case "ClearPrintQueue":
                    Calls.Add("ClearPrintQueue");
                    return New(targetMethod.ReturnType,
                        ("Success", true), ("DeletedFiles", 3), ("SkippedFiles", 0), ("Errors", 0), ("SpoolerRestarted", true), ("Message", "Synthetic queue clear"));
                default:
                    throw new InvalidOperationException("Unexpected fake operation: " + targetMethod.Name);
            }
        }

        private static object New(Type type, params (string Name, object? Value)[] values)
        {
            var instance = Activator.CreateInstance(type)
                ?? throw new InvalidOperationException("Cannot create fake result: " + type.FullName);
            foreach (var (name, value) in values)
            {
                var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException($"Fake result property missing: {type.Name}.{name}");
                property.SetValue(instance, value);
            }
            return instance;
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
        Type OpsType() => Need("IWindowsRepairOperations");
        Type CommandsType() => Need("FixedCommand");
        Type ServicesType() => Need("FixedService");
        Type NativeType() => Need("WindowsRepairOperations");
        Type HandlersType() => Need("ServiceDeskNonNetworkHandlers");

        object Fake()
        {
            FakeProxy.Reset();
            return DispatchProxy.Create(OpsType(), typeof(FakeProxy));
        }

        object Execute(string id, object fake)
        {
            var method = HandlersType().GetMethod("Execute", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("ServiceDeskNonNetworkHandlers.Execute is missing.");
            return method.Invoke(null, [id, fake])
                ?? throw new InvalidOperationException("Non-network handler returned no result.");
        }

        bool Success(object result) => Convert.ToBoolean(result.GetType().GetProperty("Success")?.GetValue(result));

        Test("native boundary accepts fixed enums rather than arbitrary command/service text", () =>
        {
            var ops = OpsType();
            Require(ops.IsInterface, "IWindowsRepairOperations is not an interface.");
            var run = ops.GetMethod("RunFixedCommand") ?? throw new InvalidOperationException("RunFixedCommand missing.");
            var restart = ops.GetMethod("RestartService") ?? throw new InvalidOperationException("RestartService missing.");
            var query = ops.GetMethod("QueryService") ?? throw new InvalidOperationException("QueryService missing.");
            Require(run.GetParameters()[0].ParameterType == CommandsType(), "RunFixedCommand does not use FixedCommand.");
            Require(restart.GetParameters()[0].ParameterType == ServicesType() && query.GetParameters()[0].ParameterType == ServicesType(),
                "Service operations do not use FixedService.");
            Require(!ops.GetMethods().Any(m => m.Name.Equals("RunCommand", StringComparison.OrdinalIgnoreCase)
                && m.GetParameters().Any(p => p.ParameterType == typeof(string))),
                "Arbitrary command execution leaked into repair boundary.");
        });

        Test("fixed command map preserves approved executable and arguments", () =>
        {
            var method = NativeType().GetMethod("GetCommandSpec", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("GetCommandSpec missing.");
            void Check(string command, string exe, string arguments)
            {
                var value = Enum.Parse(CommandsType(), command);
                var spec = method.Invoke(null, [value]) ?? throw new InvalidOperationException("Command spec missing: " + command);
                var type = spec.GetType();
                var file = Convert.ToString(type.GetProperty("FileName")?.GetValue(spec)) ?? "";
                var args = Convert.ToString(type.GetProperty("Arguments")?.GetValue(spec)) ?? "";
                Require(file.EndsWith(exe, StringComparison.OrdinalIgnoreCase), $"Wrong executable for {command}: {file}");
                Require(args == arguments, $"Wrong arguments for {command}: {args}");
            }
            Check("FlushDns", "ipconfig.exe", "/flushdns");
            Check("TimeResync", "w32tm.exe", "/resync /rediscover");
            Check("GpUpdateComputer", "gpupdate.exe", "/target:computer /force /wait:60");
            Check("Dism", "dism.exe", "/Online /Cleanup-Image /RestoreHealth");
            Check("Sfc", "sfc.exe", "/scannow");
        });

        Test("RestartSpooler preserves queue while ClearPrintQueue is a separate destructive operation", () =>
        {
            var fake = Fake();
            var restart = Execute("RestartSpooler", fake);
            Require(Success(restart), "Synthetic RestartSpooler failed.");
            Require(FakeProxy.Calls.Contains("RestartService:Spooler:True"), "RestartSpooler did not control fixed Spooler service.");
            Require(!FakeProxy.Calls.Contains("ClearPrintQueue"), "RestartSpooler unexpectedly deleted queue state.");

            fake = Fake();
            var clear = Execute("ClearPrintQueue", fake);
            Require(Success(clear) && FakeProxy.Calls.SequenceEqual(["ClearPrintQueue"]), "ClearPrintQueue did not use the dedicated fixed queue operation.");

            var safe = NativeType().GetMethod("IsSafeSpoolDirectory", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("IsSafeSpoolDirectory missing.");
            var windows = @"C:\Windows";
            var exact = Path.Combine(windows, "System32", "spool", "PRINTERS");
            Require(Convert.ToBoolean(safe.Invoke(null, [windows, exact, FileAttributes.Directory])), "Exact normal spool directory rejected.");
            Require(!Convert.ToBoolean(safe.Invoke(null, [windows, exact, FileAttributes.Directory | FileAttributes.ReparsePoint])), "Reparse spool directory accepted.");
            Require(!Convert.ToBoolean(safe.Invoke(null, [windows, Path.Combine(windows, "Temp"), FileAttributes.Directory])), "Arbitrary sibling directory accepted as print queue.");
            Require(!Convert.ToBoolean(safe.Invoke(null, [windows, Path.Combine(exact, "..", "..", "Temp"), FileAttributes.Directory])), "Traversal path accepted as print queue.");
        });

        Test("queue deletion requires a confirmed stopped Spooler and always attempts recovery", () =>
        {
            var core = NativeType().GetMethod("ClearPrintQueueCore", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("ClearPrintQueueCore safety seam is missing.");
            var windows = @"C:\Windows";
            var exact = Path.Combine(windows, "System32", "spool", "PRINTERS");
            var spool = Path.Combine(exact, "synthetic.spl");
            var deleted = new List<string>();
            var restarted = false;
            var failedStop = core.Invoke(null,
            [
                windows, exact, FileAttributes.Directory,
                (Func<bool>)(() => false),
                (Func<bool>)(() => { restarted = true; return true; }),
                (Func<IEnumerable<string>>)(() => [spool]),
                (Func<string, FileAttributes>)(_ => FileAttributes.Normal),
                (Action<string>)(path => deleted.Add(path))
            ]) ?? throw new InvalidOperationException("Queue core returned no result.");
            Require(deleted.Count == 0, "Queue file was deleted although Spooler stop failed.");
            Require(restarted, "Spooler recovery was not attempted after failed stop.");
            Require(!Success(failedStop), "Failed Spooler stop was reported as successful queue cleanup.");

            deleted.Clear(); restarted = false;
            var stopped = core.Invoke(null,
            [
                windows, exact, FileAttributes.Directory,
                (Func<bool>)(() => true),
                (Func<bool>)(() => { restarted = true; return true; }),
                (Func<IEnumerable<string>>)(() => [spool]),
                (Func<string, FileAttributes>)(_ => FileAttributes.Normal),
                (Action<string>)(path => deleted.Add(path))
            ]) ?? throw new InvalidOperationException("Queue core returned no result.");
            Require(deleted.SequenceEqual([spool]), "Queue file was not deleted after a confirmed Spooler stop.");
            Require(restarted && Success(stopped), "Successful queue cleanup did not restore Spooler/report success.");
        });

        Test("RestartUpdateServices never enables a Disabled service", () =>
        {
            var fake = Fake();
            FakeProxy.Services["WindowsUpdate"] = ("Stopped", "Disabled");
            FakeProxy.Services["Bits"] = ("Stopped", "Automatic");
            var result = Execute("RestartUpdateServices", fake);
            Require(Success(result), "Synthetic update-service repair failed.");
            Require(FakeProxy.Calls.Contains("QueryService:WindowsUpdate") && FakeProxy.Calls.Contains("QueryService:Bits"), "Update services were not queried first.");
            Require(!FakeProxy.Calls.Any(x => x.StartsWith("RestartService:WindowsUpdate", StringComparison.Ordinal)), "Disabled Windows Update was started/enabled.");
            Require(FakeProxy.Calls.Contains("RestartService:Bits:True"), "Stopped non-disabled BITS was not started.");
        });

        Test("time and machine Group Policy use fixed commands while full GpUpdate stays staged", () =>
        {
            var fake = Fake();
            Require(Success(Execute("TimeResync", fake)) && FakeProxy.Calls.Contains("RunFixedCommand:TimeResync"), "TimeResync did not use fixed command.");

            fake = Fake();
            var gp = HandlersType().GetMethod("ExecuteMachineGpUpdate", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("ExecuteMachineGpUpdate missing.");
            var gpResult = gp.Invoke(null, [fake]) ?? throw new InvalidOperationException("Machine GpUpdate returned no result.");
            Require(Success(gpResult) && FakeProxy.Calls.Contains("RunFixedCommand:GpUpdateComputer"), "Machine GpUpdate did not use fixed computer-policy command.");

            var executable = ServiceDeskActionRegistry.ExecutableHandlerIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            Require(executable.SetEquals(new[] { "CleanTemp", "FlushDns", "Dism", "Sfc", "RestartSpooler", "ClearPrintQueue", "RestartUpdateServices", "TimeResync" }),
                "Task 6 executable set is not the approved staged non-network set.");
            Require(!executable.Contains("GpUpdate"), "Full GpUpdate became executable before original-user phase orchestration exists.");
        });

        Console.WriteLine($"Fixed non-network repair self-test: {count - failures.Count}/{count} passed.");
        return failures.Count == 0 ? 0 : 225;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
