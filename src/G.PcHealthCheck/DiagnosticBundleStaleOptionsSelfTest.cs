using System.Reflection;

namespace G.PcHealthCheck;

internal static class DiagnosticBundleStaleOptionsSelfTest
{
    public static int Run()
    {
        try
        {
            var type = typeof(MainForm).Assembly.GetType("G.PcHealthCheck.DiagnosticBundleForm");
            Require(type is not null, "DiagnosticBundleForm absent.");
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var apply = type!.GetMethod("ApplyCollectedResult", flags);
            Require(apply is not null, "Diagnostic bundle collected-result boundary missing.");

            using (var form = (Form)Activator.CreateInstance(type!)!)
            {
                var snapshot = Snapshot();
                apply!.Invoke(form, [snapshot]);

                var grid = (DataGridView)form.Controls.Find("BundleSourceGrid", true).Single();
                var health = HealthRow(grid);
                Require(string.Equals(health.Cells["State"].Value?.ToString(), "Собрано", StringComparison.Ordinal),
                    "Collected Health state was not rendered before the option change.");

                var mode = (ComboBox)form.Controls.Find("BundleMode", true).Single();
                mode.SelectedItem = "Расширенный";

                Require(string.Equals(health.Cells["State"].Value?.ToString(), "Собрано", StringComparison.Ordinal),
                    "Changing next-run mode replaced the visible state of the current saveable bundle.");
                var status = (Label)form.Controls.Find("BundleStatus", true).Single();
                Require(status.Text.Contains("предыдущ", StringComparison.OrdinalIgnoreCase)
                    && status.Text.Contains("параметр", StringComparison.OrdinalIgnoreCase),
                    "Changing next-run mode did not mark the visible bundle as collected with previous parameters.");
            }

            using (var form = (Form)Activator.CreateInstance(type!)!)
            {
                var snapshot = Snapshot();
                apply!.Invoke(form, [snapshot]);
                var grid = (DataGridView)form.Controls.Find("BundleSourceGrid", true).Single();
                var health = HealthRow(grid);
                var included = health.Cells["Included"];
                var status = (Label)form.Controls.Find("BundleStatus", true).Single();
                status.Text = "Отображается собранный пакет.";

                included.Value = false;
                Application.DoEvents();

                Require(string.Equals(health.Cells["State"].Value?.ToString(), "Собрано", StringComparison.Ordinal),
                    "Changing next-run source selection replaced the visible state of the current saveable bundle.");
                Require(status.Text.Contains("следующ", StringComparison.OrdinalIgnoreCase)
                    && status.Text.Contains("предыдущ", StringComparison.OrdinalIgnoreCase),
                    "Changing next-run source selection did not distinguish it from visible saved evidence.");

                included.Value = true;
                Application.DoEvents();
                Require(!status.Text.Contains("следующ", StringComparison.OrdinalIgnoreCase),
                    "Restoring the saved source selection still marks the visible bundle as stale.");
            }

            using (var form = (Form)Activator.CreateInstance(type!)!)
            {
                var mode = (ComboBox)form.Controls.Find("BundleMode", true).Single();
                mode.SelectedItem = "Расширенный";
                var snapshot = Snapshot(DiagnosticBundleMode.Extended);
                apply!.Invoke(form, [snapshot]);

                var status = (Label)form.Controls.Find("BundleStatus", true).Single();
                var duration = (ComboBox)form.Controls.Find("BundlePerformanceSeconds", true).Single();
                status.Text = "Отображается собранный пакет.";
                duration.SelectedItem = 30;
                Application.DoEvents();

                Require(status.Text.Contains("следующ", StringComparison.OrdinalIgnoreCase)
                    && status.Text.Contains("предыдущ", StringComparison.OrdinalIgnoreCase),
                    "Changing next-run performance duration did not distinguish it from visible saved evidence.");
            }

            Console.WriteLine("Diagnostic bundle stale-options regression: 3/3 passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: diagnostic bundle stale-options regression: " + (ex.InnerException?.Message ?? ex.Message));
            return 253;
        }
    }

    private static DiagnosticBundleSnapshot Snapshot(DiagnosticBundleMode mode = DiagnosticBundleMode.Quick)
    {
        var snapshot = new DiagnosticBundleSnapshot
        {
            Options = DiagnosticBundleCore.DefaultOptions(mode),
            Outcome = "Partial"
        };
        snapshot.Health.Requested = true;
        snapshot.Health.State = "Complete";
        snapshot.Health.Payload = new DiagnosticBundleHealthPayload(new DiagnosticData(), new ScanResult());
        return snapshot;
    }

    private static DataGridViewRow HealthRow(DataGridView grid)
        => grid.Rows.Cast<DataGridViewRow>()
            .Single(row => Equals(row.Tag, DiagnosticBundleCategory.Health));

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
