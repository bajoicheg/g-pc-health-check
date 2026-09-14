using System.Collections;
using System.Reflection;

namespace G.PcHealthCheck;

internal static class NetworkRepairSelfTest
{
    public class FakeProxy : DispatchProxy
    {
        internal static readonly List<string> Calls = [];
        internal static object? Inventory;

        internal static void Reset()
        {
            Calls.Clear();
            Inventory = null;
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
                        ("Id", id), ("Success", true), ("ExitCode", 0), ("Message", "Synthetic success"),
                        ("Output", "synthetic"), ("RebootRecommended", id is "WinsockReset" or "TcpIpReset"));
                }
                case "QueryNetworkAdapters":
                    Calls.Add("QueryNetworkAdapters");
                    return Inventory ?? throw new InvalidOperationException("Synthetic adapter inventory missing.");
                case "SetAdapterEnabled":
                {
                    var id = Convert.ToString(args[0]) ?? "";
                    var enabled = Convert.ToBoolean(args[1]);
                    Calls.Add($"SetAdapterEnabled:{id}:{enabled}");
                    return New(targetMethod.ReturnType,
                        ("Id", id), ("Success", true), ("ExitCode", 0), ("Message", "Synthetic adapter state change"));
                }
                case "CycleDhcpLease":
                {
                    var id = Convert.ToString(args[0]) ?? "";
                    Calls.Add("CycleDhcpLease:" + id);
                    return New(targetMethod.ReturnType,
                        ("Id", id), ("Success", true), ("ExitCode", 0), ("Message", "Synthetic DHCP cycle"));
                }
                default:
                    throw new InvalidOperationException("Unexpected network fake operation: " + targetMethod.Name);
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
        Type AdapterType() => Need("NetworkAdapterSnapshot");
        Type PlannerType() => Need("NetworkRepairPlanner");
        Type HandlerType() => Need("ServiceDeskNetworkHandlers");
        Type OpsType() => Need("IWindowsRepairOperations");
        Type CommandType() => Need("FixedCommand");
        Type NativeType() => Need("WindowsRepairOperations");

        object Adapter(string id, string name, bool physical, bool enabled, bool connected, bool dhcp)
        {
            var item = Activator.CreateInstance(AdapterType())
                ?? throw new InvalidOperationException("Cannot create synthetic adapter.");
            void Set(string property, object value) => AdapterType().GetProperty(property)!.SetValue(item, value);
            Set("DeviceId", id); Set("Name", name); Set("Physical", physical); Set("Enabled", enabled); Set("Connected", connected); Set("DhcpEnabled", dhcp);
            return item;
        }

        IList Inventory(params object[] adapters)
        {
            var list = (IList)(Activator.CreateInstance(typeof(List<>).MakeGenericType(AdapterType()))
                ?? throw new InvalidOperationException("Cannot create typed adapter list."));
            foreach (var adapter in adapters) list.Add(adapter);
            return list;
        }

        object Fake(IList inventory)
        {
            FakeProxy.Reset();
            FakeProxy.Inventory = inventory;
            return DispatchProxy.Create(OpsType(), typeof(FakeProxy));
        }

        IReadOnlyList<string> Ids(string methodName, IList inventory)
        {
            var method = PlannerType().GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"NetworkRepairPlanner.{methodName} is missing.");
            var result = (IEnumerable)(method.Invoke(null, [inventory])
                ?? throw new InvalidOperationException($"{methodName} returned no collection."));
            return result.Cast<object>().Select(x => Convert.ToString(AdapterType().GetProperty("DeviceId")!.GetValue(x)) ?? "").ToList();
        }

        object Execute(string id, object fake)
        {
            var method = HandlerType().GetMethod("Execute", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("ServiceDeskNetworkHandlers.Execute is missing.");
            return method.Invoke(null, [id, fake])
                ?? throw new InvalidOperationException("Network handler returned no result.");
        }

        bool Success(object result) => Convert.ToBoolean(result.GetType().GetProperty("Success")?.GetValue(result));
        bool Reboot(object result) => Convert.ToBoolean(result.GetType().GetProperty("RebootRecommended")?.GetValue(result));

        var physDhcp = () => Adapter("PHYS-DHCP", "Synthetic Ethernet", true, true, true, true);
        var physStatic = () => Adapter("PHYS-STATIC", "Synthetic Static", true, true, true, false);
        var virtualDhcp = () => Adapter("VIRTUAL-DHCP", "Synthetic VPN", false, true, true, true);
        var disabledDhcp = () => Adapter("DISABLED-DHCP", "Synthetic disabled", true, false, false, true);
        var disconnectedDhcp = () => Adapter("DISCONNECTED-DHCP", "Synthetic disconnected", true, true, false, true);

        Test("network commands are fixed and contain no UI-supplied target", () =>
        {
            var method = NativeType().GetMethod("GetCommandSpec", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("GetCommandSpec missing.");
            void Check(string command, string exe, string arguments)
            {
                var value = Enum.Parse(CommandType(), command);
                var spec = method.Invoke(null, [value]) ?? throw new InvalidOperationException("Command spec missing: " + command);
                var type = spec.GetType();
                var file = Convert.ToString(type.GetProperty("FileName")?.GetValue(spec)) ?? "";
                var args = Convert.ToString(type.GetProperty("Arguments")?.GetValue(spec)) ?? "";
                Require(file.EndsWith(exe, StringComparison.OrdinalIgnoreCase), $"Wrong executable for {command}: {file}");
                Require(args == arguments, $"Wrong fixed arguments for {command}: {args}");
            }
            Check("WinsockReset", "netsh.exe", "winsock reset");
            Check("TcpIpReset", "netsh.exe", "int ip reset");
            Check("RegisterDns", "ipconfig.exe", "/registerdns");
        });

        Test("adapter planners separate physical restart targets from DHCP targets", () =>
        {
            var inventory = Inventory(physDhcp(), physStatic(), virtualDhcp(), disabledDhcp(), disconnectedDhcp());
            var restart = Ids("EligibleForRestart", inventory);
            Require(restart.SequenceEqual(new[] { "PHYS-DHCP", "PHYS-STATIC" }), "Restart target filter changed.");
            var dhcp = Ids("EligibleForDhcp", inventory);
            Require(dhcp.SequenceEqual(new[] { "PHYS-DHCP", "VIRTUAL-DHCP" }), "DHCP target filter changed or static/disconnected adapter was included.");
        });

        Test("adapter restart always attempts re-enable after a post-disable failure", () =>
        {
            var inventory = Inventory(physDhcp());
            var calls = new List<string>();
            var method = PlannerType().GetMethod("RestartEligibleAdapters", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("RestartEligibleAdapters is missing.");
            var result = method.Invoke(null,
            [
                inventory,
                (Func<string, bool, bool>)((id, enabled) => { calls.Add($"{id}:{enabled}"); return true; }),
                (Action<string>)(_ => throw new InvalidOperationException("synthetic after-disable failure"))
            ]);
            Require(result is not null, "Restart planner returned no evidence.");
            Require(calls.SequenceEqual(new[] { "PHYS-DHCP:False", "PHYS-DHCP:True" }), "Adapter was not re-enabled from cleanup/finally after failure.");
        });

        Test("network handlers use only filtered synthetic adapter inventory", () =>
        {
            var inventory = Inventory(physDhcp(), physStatic(), virtualDhcp(), disabledDhcp(), disconnectedDhcp());
            var fake = Fake(inventory);
            var restart = Execute("RestartNetworkAdapters", fake);
            Require(Success(restart), "Synthetic adapter restart failed.");
            Require(FakeProxy.Calls.Contains("SetAdapterEnabled:PHYS-DHCP:False") && FakeProxy.Calls.Contains("SetAdapterEnabled:PHYS-DHCP:True"), "DHCP physical adapter was not restarted.");
            Require(FakeProxy.Calls.Contains("SetAdapterEnabled:PHYS-STATIC:False") && FakeProxy.Calls.Contains("SetAdapterEnabled:PHYS-STATIC:True"), "Static physical adapter was not restarted.");
            Require(!FakeProxy.Calls.Any(x => x.Contains("VIRTUAL-DHCP", StringComparison.Ordinal) || x.Contains("DISABLED-DHCP", StringComparison.Ordinal) || x.Contains("DISCONNECTED-DHCP", StringComparison.Ordinal)), "Ineligible adapter entered physical restart operation.");

            fake = Fake(inventory);
            var dhcp = Execute("DhcpReleaseRenew", fake);
            Require(Success(dhcp), "Synthetic DHCP cycle failed.");
            Require(FakeProxy.Calls.Contains("CycleDhcpLease:PHYS-DHCP") && FakeProxy.Calls.Contains("CycleDhcpLease:VIRTUAL-DHCP"), "Eligible DHCP adapter was not cycled.");
            Require(!FakeProxy.Calls.Contains("CycleDhcpLease:PHYS-STATIC") && !FakeProxy.Calls.Contains("CycleDhcpLease:DISABLED-DHCP") && !FakeProxy.Calls.Contains("CycleDhcpLease:DISCONNECTED-DHCP"), "Static/disabled/disconnected adapter received DHCP cycle.");
        });

        Test("network order reboot flags and executable staging match approved Task 7 boundary", () =>
        {
            var descriptors = ServiceDeskActionRegistry.All.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
            var order = new[] { "WinsockReset", "TcpIpReset", "RestartNetworkAdapters", "DhcpReleaseRenew", "RegisterDns" }
                .Select(id => descriptors[id].PhaseOrder).ToArray();
            Require(order.SequenceEqual(order.OrderBy(x => x)) && order.Distinct().Count() == order.Length, "Network phase order is not deterministic Winsock→TCP/IP→adapters→DHCP→RegisterDns.");

            var inventory = Inventory(physDhcp());
            var fake = Fake(inventory);
            var winsock = Execute("WinsockReset", fake);
            var tcp = Execute("TcpIpReset", fake);
            var register = Execute("RegisterDns", fake);
            Require(Success(winsock) && Reboot(winsock), "Winsock reset lost reboot recommendation.");
            Require(Success(tcp) && Reboot(tcp), "TCP/IP reset lost reboot recommendation.");
            Require(Success(register) && !Reboot(register), "Register DNS incorrectly requires reboot.");

            var executable = ServiceDeskActionRegistry.ExecutableHandlerIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            Require(executable.Count == 13 && !executable.Contains("GpUpdate") && ServiceDeskActionRegistry.All.Where(x => !x.Id.Equals("GpUpdate", StringComparison.OrdinalIgnoreCase)).All(x => executable.Contains(x.Id)),
                "Task 7 must stage exactly all actions except full GpUpdate.");
            Require(typeof(RemediationActionResult).GetProperty("RebootRecommended")?.PropertyType == typeof(bool), "Per-action reboot marker missing.");
        });

        Console.WriteLine($"Network repair self-test: {count - failures.Count}/{count} passed.");
        return failures.Count == 0 ? 0 : 223;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
