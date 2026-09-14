using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class PerformanceSessionUiSelfTest
{
    public static int Run()
    {
        var count = 0; var failures = new List<string>();
        void Test(string name, Action test) { count++; try { test(); } catch (Exception ex) { failures.Add(name + ": " + (ex.InnerException?.Message ?? ex.Message)); } }
        Test("menu keeps all previous analysis tools and attaches once", () =>
        {
            using var main = new Form();
            IncidentReviewMenu.Attach(main);
            var before = main.MainMenuStrip!.Items.Find("ReadOnlyInspections", false).OfType<ToolStripMenuItem>().Single().DropDownItems.Count;
            var menu = TypeOf("PerformanceSessionMenu").GetMethod("Attach", BindingFlags.Public | BindingFlags.Static)!;
            menu.Invoke(null, [main]); menu.Invoke(null, [main]);
            var group = main.MainMenuStrip.Items.Find("ReadOnlyInspections", false).OfType<ToolStripMenuItem>().Single();
            Require(group.DropDownItems.Count == before + 1 && group.DropDownItems.Find("PerformanceSession", false).Length == 1, "Duplicate or replaced menu.");
        });
        Test("window opens idle", () =>
        {
            using var form = (Form)Activator.CreateInstance(TypeOf("PerformanceSessionForm"))!;
            Require(form.Controls.Find("SessionStart", true).Single().Enabled, "Start unavailable.");
            Require(!form.Controls.Find("SessionStop", true).Single().Enabled && !form.Controls.Find("SessionMark", true).Single().Enabled, "Idle window already running.");
            Require(!form.Controls.Find("SessionExport", true).Single().Enabled, "Uncollected session export enabled.");
        });
        Test("memory native structure matches Windows 64-byte contract", () =>
        {
            var type = TypeOf("WindowsPerformanceSessionSource").GetNestedType("MemoryStatus", BindingFlags.NonPublic)!;
            Require(Marshal.SizeOf(type) == 64, "Incorrect native memory structure size.");
        });
        Test("native counters and local disk read are non-destructive", () =>
        {
            using var source = (IPerformanceSessionSource)Activator.CreateInstance(TypeOf("WindowsPerformanceSessionSource"))!;
            Thread.Sleep(30);
            var r = source.Read(default);
            Require(r.MemoryUsedPercent is >= 0 and <= 100, "GlobalMemoryStatusEx did not produce valid physical memory.");
            Require(r.CpuPercent is null or (>= 0 and <= 100), "Invalid CPU percentage.");
            Require(r.DiskBusyPercent is null or (>= 0 and <= 100), "Invalid disk percentage.");
            Require(r.CpuPercent.HasValue || r.Warnings.Count > 0, "Missing CPU unexplained.");
        });
        Test("source pre-cancellation returns without a sample", () =>
        {
            using var source = (IPerformanceSessionSource)Activator.CreateInstance(TypeOf("WindowsPerformanceSessionSource"))!;
            using var c = new CancellationTokenSource(); c.Cancel();
            try { source.Read(c.Token); } catch (OperationCanceledException) { return; }
            throw new InvalidOperationException("Cancelled source returned data.");
        });
        Test("sample detail follows current cell after reselection", () =>
        {
            using var form = (Form)Activator.CreateInstance(TypeOf("PerformanceSessionForm"))!;
            var type = TypeOf("PerformanceSessionForm");
            var currentField = type.GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic);
            var samplesField = type.GetField("_samples", BindingFlags.Instance | BindingFlags.NonPublic);
            var detailField = type.GetField("_detail", BindingFlags.Instance | BindingFlags.NonPublic);
            var showDetail = type.GetMethod("ShowDetail", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(currentField is not null && samplesField is not null && detailField is not null && showDetail is not null, "Performance detail selection boundary missing.");
            currentField!.SetValue(form, PerformanceSessionSelfTest.Snapshot(10, 20));
            var grid = (DataGridView)samplesField!.GetValue(form)!;
            var detail = (TextBox)detailField!.GetValue(form)!;
            var first = new PerformanceSample(1000, 5, DateTimeOffset.Parse("2026-01-01T00:00:01Z"), new(10, 40, 5, 0, ["FIRST_SYNTHETIC_WARNING"]));
            var second = new PerformanceSample(2000, 5, DateTimeOffset.Parse("2026-01-01T00:00:02Z"), new(20, 40, 5, 0, ["SECOND_SYNTHETIC_WARNING"]));
            var firstRow = grid.Rows.Add(); grid.Rows[firstRow].Tag = first;
            var secondRow = grid.Rows.Add(); grid.Rows[secondRow].Tag = second;
            grid.CurrentCell = grid.Rows[0].Cells[0];
            showDetail!.Invoke(form, null);
            Require(detail.Text.Contains("FIRST_SYNTHETIC_WARNING", StringComparison.Ordinal), "First performance sample detail was not established.");
            grid.CurrentCell = grid.Rows[1].Cells[0];
            Application.DoEvents();
            Require(grid.CurrentCell?.RowIndex == 1, "Second performance sample row was not selected.");
            Require(detail.Text.Contains("SECOND_SYNTHETIC_WARNING", StringComparison.Ordinal), "Performance sample detail still shows the previously selected row.");
        });
        Test("completed-session options distinguish next run from visible evidence", () =>
        {
            using var form = (Form)Activator.CreateInstance(TypeOf("PerformanceSessionForm"))!;
            var type = TypeOf("PerformanceSessionForm"); var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var currentField = type.GetField("_current", flags); var durationField = type.GetField("_duration", flags); var intervalField = type.GetField("_interval", flags); var stateField = type.GetField("_state", flags);
            Require(currentField is not null && durationField is not null && intervalField is not null && stateField is not null, "Performance option/evidence UI boundary missing.");
            var snapshot = new PerformanceSessionSnapshot { Options = new(120, 2), Outcome = "Completed", ElapsedMs = 120000, StartedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z") };
            currentField!.SetValue(form, snapshot);
            var duration = (ComboBox)durationField!.GetValue(form)!; var interval = (ComboBox)intervalField!.GetValue(form)!; var state = (Label)stateField!.GetValue(form)!;
            state.Text = "Завершённый сеанс: параметры 120/2.";
            duration.SelectedItem = 60; Application.DoEvents();
            Require(state.Text.Contains("следующ", StringComparison.OrdinalIgnoreCase) && state.Text.Contains("предыдущ", StringComparison.OrdinalIgnoreCase), "Changed duration is visually presented as if it described the visible completed session.");
            duration.SelectedItem = 120; Application.DoEvents();
            Require(!state.Text.Contains("следующ", StringComparison.OrdinalIgnoreCase), "Restored duration still marks completed evidence as stale.");
            interval.SelectedItem = 1; Application.DoEvents();
            Require(state.Text.Contains("следующ", StringComparison.OrdinalIgnoreCase) && state.Text.Contains("предыдущ", StringComparison.OrdinalIgnoreCase), "Changed interval is visually presented as if it described the visible completed session.");
            interval.SelectedItem = 2; Application.DoEvents();
            Require(!state.Text.Contains("следующ", StringComparison.OrdinalIgnoreCase), "Restored interval still marks completed evidence as stale.");
        });
        Test("performance export failure leaves terminal status", () =>
        {
            using var form = (Form)Activator.CreateInstance(TypeOf("PerformanceSessionForm"))!;
            var type = TypeOf("PerformanceSessionForm"); var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var stateField = type.GetField("_state", flags);
            var applyFailure = type.GetMethod("ApplyExportFailure", flags);
            Require(stateField is not null && applyFailure is not null, "Performance session export failure status boundary missing.");
            var state = (Label)stateField!.GetValue(form)!;
            state.Text = "Сохранено: C:\\previous";
            applyFailure!.Invoke(form, [new IOException("synthetic export failure")]);
            Require(state.Text.Contains("не заверш", StringComparison.OrdinalIgnoreCase), "Failed performance export still looks successful.");
            Require(state.Text.Contains(nameof(IOException), StringComparison.Ordinal), "Performance export failure status omits exception type.");
            Require(state.Text.Contains("0x", StringComparison.OrdinalIgnoreCase), "Performance export failure status omits HRESULT.");
        });
        foreach (var metric in Enum.GetValues<SessionMetric>())
            Test("render graph with missing data " + metric, () =>
            {
                using var chart = (Control)Activator.CreateInstance(TypeOf("PerformanceTimeline"))!;
                chart.Size = new Size(700, 220);
                var s = PerformanceSessionSelfTest.Snapshot(10, null, 90);
                s.Markers.Add(new(1500, "Симптом"));
                chart.GetType().GetMethod("Display")!.Invoke(chart, [s, metric]);
                using var bitmap = new Bitmap(chart.Width, chart.Height); chart.DrawToBitmap(bitmap, chart.ClientRectangle);
            });
        Test("exports use distinct paths and preserve complete observations", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "GPcHealthCheck-Session-Test-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root); var s = PerformanceSessionSelfTest.Snapshot(10, null, 30);
                var save = TypeOf("PerformanceSessionExport").GetMethod("Save", BindingFlags.Public | BindingFlags.Static)!;
                var first = (string)save.Invoke(null, [s, root])!; var second = (string)save.Invoke(null, [s, root])!;
                Require(first != second && File.Exists(Path.Combine(first, "report.html")), "Report overwritten.");
                using var j = JsonDocument.Parse(File.ReadAllText(Path.Combine(first, "session.json")));
                Require(j.RootElement.GetProperty("Snapshot").GetProperty("Samples").GetArrayLength() == 3, "Missing samples discarded.");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
        Console.WriteLine($"Performance session integration: {count - failures.Count}/{count} passed.");
        foreach (var f in failures) Console.Error.WriteLine("FAIL: " + f);
        return failures.Count == 0 ? 0 : 221;
    }

    private static Type TypeOf(string name) => typeof(AssessmentService).Assembly.GetType("G.PcHealthCheck." + name) ?? throw new InvalidOperationException(name + " not implemented.");
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
}
