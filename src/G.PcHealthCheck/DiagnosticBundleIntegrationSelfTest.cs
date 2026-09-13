using System.Reflection;

namespace G.PcHealthCheck;

internal static class DiagnosticBundleIntegrationSelfTest
{
    public static int Run()
    {
        var failures = new List<string>();
        var count = 0;
        void Test(string name, Action action)
        {
            count++;
            try { action(); }
            catch (Exception ex) { failures.Add(name + ": " + (ex.InnerException?.Message ?? ex.Message)); }
        }

        Test("bundle window opens idle", () =>
        {
            using var form = CreateForm();
            form.PerformLayout();
            Require(form.Controls.Find("BundleStart", true).Single().Enabled, "Start disabled.");
            Require(!form.Controls.Find("BundleSave", true).Single().Enabled, "Save enabled without snapshot.");
            Require(form.Controls.Find("BundleSourceGrid", true).Single() is DataGridView grid && grid.Rows.Count >= 6,
                "Source/privacy matrix missing.");
        });

        Test("Quick mode excludes timed performance", () =>
        {
            using var form = CreateForm();
            var mode = (ComboBox)form.Controls.Find("BundleMode", true).Single();
            mode.SelectedItem = "Быстрый";
            Require(!IsCategoryChecked(form, DiagnosticBundleCategory.Performance), "Quick still includes performance.");
            Require(!IsCategoryEditable(form, DiagnosticBundleCategory.Performance), "Quick allows enabling timed performance.");
        });

        Test("Extended defaults are 60 seconds and 2 seconds", () =>
        {
            using var form = CreateForm();
            var mode = (ComboBox)form.Controls.Find("BundleMode", true).Single();
            mode.SelectedItem = "Расширенный";
            Require(IsCategoryChecked(form, DiagnosticBundleCategory.Performance), "Extended omitted performance.");
            var duration = (ComboBox)form.Controls.Find("BundlePerformanceSeconds", true).Single();
            var interval = (ComboBox)form.Controls.Find("BundlePerformanceInterval", true).Single();
            Require(Convert.ToInt32(duration.SelectedItem) == 60 && Convert.ToInt32(interval.SelectedItem) == 2,
                "Extended timing defaults changed.");
        });

        Test("privacy examples are visible before collection", () =>
        {
            using var form = CreateForm();
            var grid = (DataGridView)form.Controls.Find("BundleSourceGrid", true).Single();
            var text = string.Join(" ", grid.Rows.Cast<DataGridViewRow>()
                .SelectMany(row => row.Cells.Cast<DataGridViewCell>())
                .Select(cell => cell.Value?.ToString() ?? ""));
            Require(text.Contains("SID", StringComparison.OrdinalIgnoreCase)
                && text.Contains("IP", StringComparison.OrdinalIgnoreCase)
                && text.Contains("событ", StringComparison.OrdinalIgnoreCase),
                "Privacy examples are missing.");
        });

        Test("bundle save snapshots UI values before background work", () =>
        {
            var assembly = typeof(MainForm).Assembly;
            var requestType = assembly.GetType("G.PcHealthCheck.DiagnosticBundleSaveRequest");
            Require(requestType is not null, "UI-free diagnostic bundle save request missing.");

            var memberTypes = requestType!.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(property => property.PropertyType)
                .Concat(requestType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Select(field => field.FieldType));
            Require(memberTypes.All(type => !typeof(Control).IsAssignableFrom(type)),
                "Background save request retains a WinForms Control.");

            var execute = requestType.GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Require(execute is not null && execute.GetParameters().Length == 0,
                "Background save request has no parameterless ExecuteAsync boundary.");

            var formType = assembly.GetType("G.PcHealthCheck.DiagnosticBundleForm");
            var capture = formType?.GetMethod("CreateSaveRequest", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(capture is not null && capture.ReturnType == requestType,
                "DiagnosticBundleForm does not snapshot save inputs on the UI thread.");
        });

        Test("bundle marker timing keeps producer monotonic origin", () =>
        {
            var assembly = typeof(MainForm).Assembly;
            var progressType = assembly.GetType("G.PcHealthCheck.DiagnosticBundleProgress");
            Require(progressType?.GetProperty("MonotonicTimestamp")?.PropertyType == typeof(long),
                "Bundle progress does not carry the producer monotonic timestamp.");

            var clockType = assembly.GetType("G.PcHealthCheck.DiagnosticBundleMarkerClock");
            Require(clockType is not null, "Diagnostic bundle marker clock is missing.");
            var start = clockType!.GetMethod("Start", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var elapsed = clockType.GetMethod("ElapsedMs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Require(start is not null && elapsed is not null, "Marker clock boundary is incomplete.");

            var clock = Activator.CreateInstance(clockType)!;
            const long origin = 1_000_000;
            var later = origin + 3L * System.Diagnostics.Stopwatch.Frequency;
            start!.Invoke(clock, [origin]);
            var offset = Convert.ToInt64(elapsed!.Invoke(clock, [later]));
            Require(Math.Abs(offset - 3000) <= 1,
                "Delayed UI dispatch changed the symptom-marker timeline origin.");

            var formType = assembly.GetType("G.PcHealthCheck.DiagnosticBundleForm");
            var field = formType?.GetField("_performanceMarkerClock", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field?.FieldType == clockType, "DiagnosticBundleForm does not use the monotonic marker clock.");
        });

        Test("bundle UI exposes no active probe or remediation controls", () =>
        {
            using var form = CreateForm();
            var forbiddenNames = new[] { "ResourceProbe", "FlushDns", "CleanTemp", "Dism", "Sfc", "ProcessKill", "ServiceRestart" };
            foreach (var name in forbiddenNames)
                Require(form.Controls.Find(name, true).Length == 0, "Forbidden control exposed: " + name);
        });

        Test("menu attaches once", () =>
        {
            var menuType = typeof(MainForm).Assembly.GetType("G.PcHealthCheck.DiagnosticBundleMenu");
            Require(menuType is not null, "Diagnostic bundle menu missing.");
            using var form = new Form();
            var attach = menuType!.GetMethod("Attach", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Require(attach is not null, "Attach method missing.");
            attach!.Invoke(null, [form]); attach.Invoke(null, [form]);
            var group = (ToolStripMenuItem)form.MainMenuStrip!.Items.Find("ReadOnlyInspections", false).Single();
            Require(group.DropDownItems.Find("DiagnosticBundleOpen", false).Length == 1, "Menu duplicated.");
        });

        Console.WriteLine($"Diagnostic bundle UI integration: {count - failures.Count}/{count} passed.");
        foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 251;
    }

    private static Form CreateForm()
    {
        var type = typeof(MainForm).Assembly.GetType("G.PcHealthCheck.DiagnosticBundleForm");
        Require(type is not null, "DiagnosticBundleForm absent.");
        return (Form)Activator.CreateInstance(type!)!;
    }

    private static bool IsCategoryChecked(Form form, DiagnosticBundleCategory category)
    {
        var row = CategoryRow(form, category);
        return Convert.ToBoolean(row.Cells["Included"].Value ?? false);
    }

    private static bool IsCategoryEditable(Form form, DiagnosticBundleCategory category)
        => !CategoryRow(form, category).Cells["Included"].ReadOnly;

    private static DataGridViewRow CategoryRow(Form form, DiagnosticBundleCategory category)
    {
        var grid = (DataGridView)form.Controls.Find("BundleSourceGrid", true).Single();
        return grid.Rows.Cast<DataGridViewRow>().Single(row => Equals(row.Tag, category));
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
