using System.Collections;
using System.Reflection;

namespace G.PcHealthCheck;

internal static class ServiceDeskBatchPlannerSelfTest
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

        var assembly = typeof(MainForm).Assembly;
        Type PlannerType() => assembly.GetType("G.PcHealthCheck.ServiceDeskBatchPlanner")
            ?? throw new InvalidOperationException("Service Desk batch planner is missing.");
        Type ModeType() => assembly.GetType("G.PcHealthCheck.BatchMode")
            ?? throw new InvalidOperationException("BatchMode is missing.");
        MethodInfo PlanMethod() => PlannerType().GetMethod("Plan", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ServiceDeskBatchPlanner.Plan is missing.");
        object Plan(string mode, IReadOnlyCollection<ActionRecommendation> recommendations, IReadOnlyCollection<string> selected, Func<string, ActionAvailability> availability)
            => PlanMethod().Invoke(null, [Enum.Parse(ModeType(), mode), recommendations, selected, availability])
                ?? throw new InvalidOperationException("Planner returned null.");
        static object? Value(object item, string property)
            => item.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item)
                ?? throw new InvalidOperationException($"Missing property {property}.");
        static string DescriptorId(object action)
            => Convert.ToString(Value(Value(action, "Descriptor")!, "Id")) ?? "";
        static string State(object action) => Convert.ToString(Value(action, "State")) ?? "";
        static IReadOnlyList<object> Actions(object preflight) => ((IEnumerable)Value(preflight, "Actions")!).Cast<object>().ToList();

        ActionRecommendation Rec(string id, RecommendationClass recommendationClass, bool automated = true)
            => new() { Id = id, Title = id, Kind = "localized-display-only", RecommendationClass = recommendationClass, CanAutomate = automated };
        ActionAvailability Availability(string id)
        {
            var descriptor = ServiceDeskActionRegistry.Find(id);
            if (id == "Dism") return new("Unavailable", "Synthetic stale context", "Synthetic");
            if (descriptor?.RequiresAdministrator == true) return new("NeedsUac", "Synthetic UAC", "Synthetic");
            return new("Ready", "Synthetic ready", "Synthetic");
        }

        Test("green requests only Recommended automated requestable actions", () =>
        {
            var recommendations = new[]
            {
                Rec("CleanTemp", RecommendationClass.Recommended),
                Rec("FlushDns", RecommendationClass.Optional),
                Rec("Dism", RecommendationClass.Recommended),
                Rec("ManualSynthetic", RecommendationClass.Manual, automated: false)
            };
            var preflight = Plan("RecommendedBestEffort", recommendations, Array.Empty<string>(), Availability);
            var actions = Actions(preflight);
            Require(actions.Where(x => State(x) == "Run").Select(DescriptorId).SequenceEqual(new[] { "CleanTemp" }), "Green runnable set is not exact.");
            Require(actions.Any(x => DescriptorId(x) == "Dism" && State(x) == "Skipped"), "Unavailable recommended action is not visible as skipped.");
            Require(actions.All(x => DescriptorId(x) != "FlushDns" && DescriptorId(x) != "ManualSynthetic"), "Green included Optional/Manual action.");
        });

        Test("red requests exact 14 with skips coalescing and disruptive aggregate", () =>
        {
            ActionAvailability RedAvailability(string id)
            {
                if (id == "CleanTemp") return new("Unavailable", "Synthetic elevated GUI", "Synthetic");
                var descriptor = ServiceDeskActionRegistry.Find(id)!;
                return descriptor.RequiresAdministrator ? new("NeedsUac", "Synthetic UAC", "Synthetic") : new("Ready", "Synthetic ready", "Synthetic");
            }
            var preflight = Plan("AllBestEffort", Array.Empty<ActionRecommendation>(), Array.Empty<string>(), RedAvailability);
            var actions = Actions(preflight);
            Require(actions.Select(DescriptorId).ToHashSet(StringComparer.Ordinal).SetEquals(ServiceDeskActionRegistry.All.Select(x => x.Id)), "Red request is not the exact registry all-set.");
            Require(actions.Any(x => DescriptorId(x) == "CleanTemp" && State(x) == "Skipped"), "Unavailable red action must be visible as skipped.");
            Require(actions.Any(x => DescriptorId(x) == "RestartSpooler" && State(x) == "Superseded"), "RestartSpooler was not coalesced by ClearPrintQueue.");
            Require(actions.Any(x => DescriptorId(x) == "ClearPrintQueue" && State(x) == "Run"), "ClearPrintQueue must remain runnable after coalescing.");
            Require(Convert.ToString(Value(preflight, "HighestRisk")) == "Disruptive", "Red highest risk is not Disruptive.");
            Require(Convert.ToBoolean(Value(preflight, "RequiresUac")), "Red preflight lost UAC requirement.");
            Require(Convert.ToBoolean(Value(preflight, "MayBreakConnectivity")), "Red preflight lost connectivity warning.");
            Require(Convert.ToBoolean(Value(preflight, "MayDeleteUserVisibleState")), "Red preflight lost destructive-state warning.");
            Require(Convert.ToBoolean(Value(preflight, "MayRequireReboot")), "Red preflight lost reboot warning.");
        });

        Test("SelectedStrict rejects a stale unavailable selection before modification", () =>
        {
            try
            {
                _ = Plan("SelectedStrict", [Rec("Dism", RecommendationClass.Optional)], ["Dism"], Availability);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
            {
                return;
            }
            throw new InvalidOperationException("SelectedStrict accepted stale unavailable Dism.");
        });

        Test("confirmation cancel invokes no executor", () =>
        {
            var preflight = Plan("AllBestEffort", Array.Empty<ActionRecommendation>(), Array.Empty<string>(), id => new("Unavailable", "Synthetic", "Synthetic"));
            var executeCalls = 0;
            Func<bool> confirm = () => false;
            Func<Task> execute = () => { executeCalls++; return Task.CompletedTask; };
            var method = PlannerType().GetMethod("ExecuteAfterConfirmationAsync", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("ExecuteAfterConfirmationAsync is missing.");
            var task = (Task<bool>)(method.Invoke(null, [preflight, confirm, execute])
                ?? throw new InvalidOperationException("Confirmation helper returned null."));
            var ran = task.GetAwaiter().GetResult();
            Require(!ran && executeCalls == 0, "Cancel path invoked executor/UAC boundary.");
        });

        Test("main UI exposes Select All green and red controls with safe default behavior", () =>
        {
            using var form = new MainForm();
            var ensure = typeof(MainForm).GetMethod("EnsureServiceDeskActionsUi", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Main Service Desk action UI initializer is missing.");
            ensure.Invoke(form, null);
            var selectAll = form.Controls.Find("SelectAllAvailable", true).OfType<CheckBox>().SingleOrDefault()
                ?? throw new InvalidOperationException("SelectAllAvailable is missing.");
            var green = form.Controls.Find("MakeBetter", true).OfType<Button>().SingleOrDefault()
                ?? throw new InvalidOperationException("MakeBetter is missing.");
            var red = form.Controls.Find("DoEverything", true).OfType<Button>().SingleOrDefault()
                ?? throw new InvalidOperationException("DoEverything is missing.");
            Require(!ReferenceEquals(form.AcceptButton, red), "Red Do everything button must never be the default Enter action.");
            Require(green.BackColor != red.BackColor, "Green and red batch buttons are not visually distinct.");

            var scan = new ScanResult
            {
                Data = new DiagnosticData { System = new SystemInfo { ExecutionContext = new ExecutionContextInfo() } },
                Actions =
                [
                    Rec("FlushDns", RecommendationClass.Optional),
                    Rec("ManualSynthetic", RecommendationClass.Manual, automated: false)
                ]
            };
            typeof(MainForm).GetMethod("Populate", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [scan]);
            var grid = (DataGridView)typeof(MainForm).GetField("_actions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            var flush = grid.Rows.Cast<DataGridViewRow>().Single(x => ((ActionRecommendation)x.Tag!).Id == "FlushDns");
            var manual = grid.Rows.Cast<DataGridViewRow>().Single(x => ((ActionRecommendation)x.Tag!).Id == "ManualSynthetic");

            selectAll.Checked = true;
            Application.DoEvents();
            Require(Convert.ToBoolean(flush.Cells["Selected"].Value) && !Convert.ToBoolean(manual.Cells["Selected"].Value ?? false), "Select All did not select exactly requestable automated rows.");
            flush.Cells["Selected"].Value = false;
            Application.DoEvents();
            Require(!selectAll.Checked, "Select All did not resync after a manual row change.");
            selectAll.Checked = true;
            selectAll.Checked = false;
            Application.DoEvents();
            Require(!Convert.ToBoolean(flush.Cells["Selected"].Value ?? false), "Clearing Select All left an automated row selected.");
        });

        Console.WriteLine($"Service Desk batch planner/UI self-test: {count - failures.Count}/{count} passed.");
        return failures.Count == 0 ? 0 : 227;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
