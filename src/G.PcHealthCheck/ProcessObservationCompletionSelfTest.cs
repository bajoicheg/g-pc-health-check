using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace G.PcHealthCheck;

internal static class ProcessObservationCompletionSelfTest
{
    public static int Run()
    {
        var n = 0; var failures = new List<string>();
        void Test(string name, Action test) { n++; try { test(); } catch (Exception ex) { failures.Add(name + ": " + (ex.InnerException?.Message ?? ex.Message)); } }
        Test("actual process-view render enables only a known selected instance", () =>
        {
            using var form = new IncidentReviewForm(false);
            var grid = (DataGridView)form.Controls.Find("IncidentGrid", true).Single();
            var button = (Button)form.Controls.Find("ObserveSelectedProcess", true).Single();
            var known = new ProcessReviewEntry { Pid = 42, CreatedAt = ProcessObservationSelfTest.Stamp, CreationKey = "synthetic-known", Name = "known" };
            var unknown = new ProcessReviewEntry { Pid = 43, Name = "unknown" };
            typeof(IncidentReviewForm).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, new ProcessReviewSnapshot { Processes = [known, unknown], State = "Complete" });
            typeof(IncidentReviewForm).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [null]);
            var good = grid.Rows.Cast<DataGridViewRow>().Single(x => ReferenceEquals(x.Tag, known));
            var bad = grid.Rows.Cast<DataGridViewRow>().Single(x => ReferenceEquals(x.Tag, unknown));
            grid.CurrentCell = good.Cells[0]; Require(button.Enabled, "Known selection is not observable.");
            grid.CurrentCell = bad.Cells[0]; Require(!button.Enabled, "Unknown creation time can start observation.");
            grid.CurrentCell = good.Cells[0]; Require(button.Enabled, "Reselection did not restore availability.");
        });
        Test("link is unique and respects provider-busy controls", () =>
        {
            using var form = new Form(); var panel = new FlowLayoutPanel(); form.Controls.Add(panel);
            var grid = new DataGridView { AllowUserToAddRows = false }; grid.Columns.Add("PID", "PID"); panel.Controls.Add(grid);
            var query = new Button { Enabled = true }; panel.Controls.Add(query);
            grid.Rows.Add("42"); grid.Rows[0].Tag = new ProcessReviewEntry { Pid = 42, CreatedAt = ProcessObservationSelfTest.Stamp }; grid.CurrentCell = grid.Rows[0].Cells[0];
            ProcessObservationLink.Attach(form, grid, query, false); ProcessObservationLink.Attach(form, grid, query, false);
            var button = (Button)form.Controls.Find("ObserveSelectedProcess", true).Single(); Require(button.Enabled, "Known idle selection disabled.");
            query.Enabled = false; Require(!button.Enabled, "Busy owner query ignored."); query.Enabled = true; Require(button.Enabled, "Idle transition ignored.");
            grid.Enabled = false; Require(!button.Enabled, "Disabled grid ignored.");
        });
        Test("sample columns are numeric and observation has no implicit export", () =>
        {
            using var form = new ProcessObservationForm(ProcessObservationSelfTest.Target);
            var grid = (DataGridView)form.Controls.Find("ObservationSamples", true).Single();
            Require(grid.ReadOnly && grid.Rows.Count == 0 && grid.Columns["ProcessCpu"].ValueType == typeof(double) && grid.Columns["SystemOffset"].ValueType == typeof(double), "Grid semantics wrong.");
            Require(!form.Controls.Find("ObservationMark", true).Single().Enabled && !form.Controls.Find("ObservationStop", true).Single().Enabled, "Idle session accepts stop/mark.");
        });
        Test("HTML includes all nine graphs plus encoded raw counter context", () =>
        {
            var s = Fixture(); s.Target = s.Target with { Name = "<script>target</script>" }; s.System.Markers.Add(new(1500, "<img src=x>Симптом"));
            var html = ProcessObservationReport.Html(s);
            Require(Regex.Matches(html, "<svg ").Count == 9 && !html.Contains("<script>") && !html.Contains("<img src=x>") && html.Contains("&lt;script&gt;"), "Missing/unsafe graphs or raw evidence.");
            using var json = JsonDocument.Parse(ProcessObservationReport.Json(s));
            Require(json.RootElement.GetProperty("Samples").GetArrayLength() == 3 && json.RootElement.GetProperty("System").GetProperty("Samples").GetArrayLength() == 3, "Paired/raw data omitted.");
            Require(ProcessObservationReport.EndMs(s) == 3100, "Charts have an inconsistent elapsed-time extent.");
        });
        Test("each process chart paints both populated and missing evidence", () =>
        {
            var s = Fixture(); using var chart = new ProcessObservationTimeline { Size = new Size(700, 190) }; using var bitmap = new Bitmap(700, 190);
            foreach (var metric in Enum.GetValues<ProcessMetric>()) { chart.Display(s, metric); chart.DrawToBitmap(bitmap, new Rectangle(0, 0, 700, 190)); }
            chart.Display(new ProcessObservationSnapshot { Target = s.Target }, ProcessMetric.Cpu); chart.DrawToBitmap(bitmap, new Rectangle(0, 0, 700, 190));
        });
        Test("synchronous progress cancellation preserves exactly one committed pair", () =>
        {
            using var cancellation = new CancellationTokenSource(); using var process = new ProcessSource(); using var system = new SystemSource(); var deliveries = 0;
            var sink = new Sink(_ => { deliveries++; cancellation.Cancel(); });
            var result = ProcessObservationService.RunAsync(ProcessObservationSelfTest.Target, new(4, 1), process, system, new ProcessObservationSelfTest.Clock(), null, sink, cancellation.Token).GetAwaiter().GetResult();
            Require(result.System.Outcome == "Stopped" && result.Samples.Count == 1 && result.System.Samples.Count == 1 && deliveries == 1, "Progress cancellation lost/duplicated pair.");
        });
        Test("identity change is sticky while machine sampling continues", () =>
        {
            using var process = new ProcessSource { ChangeAt = 2 }; using var system = new SystemSource();
            var result = ProcessObservationService.RunAsync(ProcessObservationSelfTest.Target, new(4, 1), process, system, new ProcessObservationSelfTest.Clock(), null, null, default).GetAwaiter().GetResult();
            Require(process.Calls == 2 && result.System.Samples.Count == 4 && result.Samples.Skip(1).All(x => x.Process.Counters.State == "IdentityChanged" && x.Reading.WorkingSetMiB is null), "Changed PID was reopened/rebound.");
        });
        Test("slow provider skips slots without inventing process rate interval", () =>
        {
            var clock = new ProcessObservationSelfTest.Clock(); using var process = new ProcessSource(); using var system = new SystemSource { Clock = clock, Delay = 1500 };
            var result = ProcessObservationService.RunAsync(ProcessObservationSelfTest.Target, new(4, 1), process, system, clock, null, null, default).GetAwaiter().GetResult();
            Require(result.Samples.Count == 2 && result.System.MissedSlots == 2 && result.Samples[1].Process.OffsetMs == 3000 && result.Samples[1].System.OffsetMs == 4500 && result.Samples[1].Reading.CpuPercent is null, "Slow calls hidden or invalid rate bridged.");
        });
        Console.WriteLine($"Process observation completion: {n - failures.Count}/{n} passed."); foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure); return failures.Count == 0 ? 0 : 249;
    }
    private static ProcessObservationSnapshot Fixture()
    {
        var s = new ProcessObservationSnapshot { Target = ProcessObservationSelfTest.Target, System = new() { Options = new(3, 1), StartedAt = ProcessObservationSelfTest.Stamp, FinishedAt = ProcessObservationSelfTest.Stamp.AddMilliseconds(3100), ElapsedMs = 3100, Outcome = "Completed" } };
        ProcessCounterSample? before = null;
        for (var i = 1; i <= 3; i++)
        {
            var raw = ProcessObservationSelfTest.Sample(i * 1000); var host = new PerformanceSample(i * 1000 + 100, 100, ProcessObservationSelfTest.Stamp.AddMilliseconds(i * 1000 + 100), new(10, 40, 20, 1, []));
            s.Samples.Add(new(raw, ProcessObservationCore.Calculate(s.Target, before, raw, 1), host)); s.System.Samples.Add(host); before = raw;
        }
        return s;
    }
    private sealed class Sink(Action<ProcessObservationSample> action) : IProgress<ProcessObservationSample> { public void Report(ProcessObservationSample sample) => action(sample); }
    private sealed class ProcessSource : IProcessObservationSource
    {
        public int Calls { get; private set; } public int ChangeAt { get; init; }
        public ProcessCounters Read(CancellationToken ct) { Calls++; var c = ProcessObservationSelfTest.Counters; return c with { CreatedFileTime = c.CreatedFileTime + (Calls == ChangeAt ? 10UL : 0UL) }; }
        public void Dispose() { }
    }
    private sealed class SystemSource : IPerformanceSessionSource
    {
        public ProcessObservationSelfTest.Clock? Clock { get; init; } public int Delay { get; init; }
        public PerformanceReading Read(CancellationToken ct) { if (Clock is not null) Clock.ElapsedMs += Delay; return new(10, 40, 20, 1, []); }
        public void Dispose() { }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
