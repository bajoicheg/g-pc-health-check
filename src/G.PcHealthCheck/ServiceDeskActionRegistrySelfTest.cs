using System.Collections;
using System.Reflection;

namespace G.PcHealthCheck;

internal static class ServiceDeskActionRegistrySelfTest
{
    private static readonly string[] ExactAllSet =
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
        Type RegistryType() => assembly.GetType("G.PcHealthCheck.ServiceDeskActionRegistry")
            ?? throw new InvalidOperationException("0.16.0 Service Desk action registry is missing.");
        Type RecommendationType() => assembly.GetType("G.PcHealthCheck.RecommendationClass")
            ?? throw new InvalidOperationException("Stable RecommendationClass is missing.");
        IReadOnlyList<object> Descriptors()
        {
            var value = RegistryType().GetProperty("All", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
                ?? throw new InvalidOperationException("ServiceDeskActionRegistry.All is missing.");
            return ((IEnumerable)value).Cast<object>().ToList();
        }
        static object? Value(object item, string property)
            => item.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item)
                ?? throw new InvalidOperationException($"Descriptor property {property} is missing.");
        static string Id(object item) => Convert.ToString(Value(item, "Id")) ?? "";
        static int Order(object item) => Convert.ToInt32(Value(item, "PhaseOrder"));
        static bool Flag(object item, string property) => Convert.ToBoolean(Value(item, property));
        static string Text(object item, string property) => Convert.ToString(Value(item, property)) ?? "";

        Test("registry contains exact fixed 14-action all-set with unique IDs", () =>
        {
            var all = Descriptors();
            var ids = all.Select(Id).ToList();
            Require(ids.Count == ids.Distinct(StringComparer.Ordinal).Count(), "Registry contains duplicate IDs.");
            var red = all.Where(x => Flag(x, "IncludeInDoEverything")).Select(Id).ToHashSet(StringComparer.Ordinal);
            Require(red.SetEquals(ExactAllSet) && red.Count == ExactAllSet.Length,
                "Do everything set differs from the approved fixed 14-action allow-list.");
        });

        Test("registry has deterministic host risk order and localization metadata", () =>
        {
            var all = Descriptors().ToDictionary(Id, StringComparer.Ordinal);
            foreach (var id in ExactAllSet)
            {
                Require(all.TryGetValue(id, out var item), $"Descriptor missing: {id}.");
                Require(Order(item!) > 0, $"Phase order missing: {id}.");
                Require(Text(item!, "Host") is "Parent" or "Worker" or "Split", $"Invalid host: {id}.");
                Require(Text(item!, "Risk") is "Low" or "Medium" or "High" or "Disruptive", $"Invalid risk: {id}.");
                foreach (var key in new[] { "TitleKey", "ImpactKey", "RiskKey", "VerificationKey" })
                    Require(!string.IsNullOrWhiteSpace(Text(item!, key)), $"{key} missing: {id}.");
            }

            Require(Order(all["Dism"]) < Order(all["Sfc"]), "DISM must precede SFC.");
            foreach (var id in new[] { "WinsockReset", "TcpIpReset", "RestartNetworkAdapters", "DhcpReleaseRenew", "RegisterDns" })
                Require(Order(all["Sfc"]) < Order(all[id]), $"Network phase must follow SFC: {id}.");
            Require(Order(all["RegisterDns"]) < Order(all["FlushDns"]), "FlushDns must be final after post-repair DNS registration.");
            Require(Flag(all["ClearPrintQueue"], "MayDeleteUserVisibleState"), "ClearPrintQueue must disclose print-job deletion.");
            Require(Flag(all["WinsockReset"], "MayRequireReboot") && Flag(all["TcpIpReset"], "MayRequireReboot"), "Network reset reboot impact missing.");
            Require(Flag(all["RestartNetworkAdapters"], "MayBreakConnectivity") && Flag(all["DhcpReleaseRenew"], "MayBreakConnectivity"), "Connectivity disruption metadata missing.");
        });

        Test("prohibited IDs stay absent while Task 8 enables exact 14 and legacy worker excludes split GpUpdate", () =>
        {
            var ids = Descriptors().Select(Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var prohibited in new[] { "DisableDefender", "StopEDR", "DisableFirewall", "ClearEventLog", "RunCommand", "ArbitraryService" })
                Require(!ids.Contains(prohibited), $"Prohibited action is present: {prohibited}.");

            var executable = RegistryType().GetProperty("ExecutableHandlerIds", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
                ?? throw new InvalidOperationException("ExecutableHandlerIds metadata is missing.");
            var executableIds = ((IEnumerable)executable).Cast<object>().Select(Convert.ToString).Where(x => x is not null).Cast<string>().ToHashSet(StringComparer.Ordinal);
            Require(executableIds.SetEquals(ExactAllSet) && executableIds.Count == ExactAllSet.Length,
                "Task 8 executable set must be the exact approved 14-action allow-list.");

            var worker = RegistryType().GetProperty("WorkerExecutableHandlerIds", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
                ?? throw new InvalidOperationException("WorkerExecutableHandlerIds metadata is missing.");
            var workerIds = ((IEnumerable)worker).Cast<object>().Select(Convert.ToString).Where(x => x is not null).Cast<string>().ToHashSet(StringComparer.Ordinal);
            Require(!workerIds.Contains("GpUpdate"), "Legacy one-shot worker accepted full split GpUpdate.");
        });

        Test("recommendation semantics are stable and do not depend on localized Kind", () =>
        {
            var enumType = RecommendationType();
            Require(Enum.GetNames(enumType).SequenceEqual(new[] { "Recommended", "Optional", "Manual" }), "RecommendationClass values changed.");
            var property = typeof(ActionRecommendation).GetProperty("RecommendationClass")
                ?? throw new InvalidOperationException("ActionRecommendation.RecommendationClass is missing.");
            Require(property.PropertyType == enumType, "RecommendationClass property uses the wrong type.");

            var stable = new ActionRecommendation { Id = "Stable", Kind = "NOT-A-RECOMMENDATION", Title = "STABLE-RECOMMENDED", Preselected = false };
            property.SetValue(stable, Enum.Parse(enumType, "Recommended"));
            var localizedOnly = new ActionRecommendation { Id = "LocalizedOnly", Kind = "Рекомендуется", Title = "LOCALIZED-ONLY", Preselected = false };
            property.SetValue(localizedOnly, Enum.Parse(enumType, "Optional"));
            var scan = new ScanResult { Actions = [stable, localizedOnly] };
            var summary = SupportSummary.Build(scan);
            Require(summary.Contains("STABLE-RECOMMENDED", StringComparison.Ordinal), "Stable Recommended action was not selected.");
            Require(!summary.Contains("LOCALIZED-ONLY", StringComparison.Ordinal), "Localized Kind still drives recommendation logic.");
        });

        Console.WriteLine($"Service Desk action registry self-test: {count - failures.Count}/{count} passed.");
        return failures.Count == 0 ? 0 : 229;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
