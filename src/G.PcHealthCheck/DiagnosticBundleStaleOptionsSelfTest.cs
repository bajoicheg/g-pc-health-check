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
            using var form = (Form)Activator.CreateInstance(type!)!;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var apply = type!.GetMethod("ApplyCollectedResult", flags);
            Require(apply is not null, "Diagnostic bundle collected-result boundary missing.");

            var snapshot = new DiagnosticBundleSnapshot
            {
                Options = DiagnosticBundleCore.DefaultOptions(DiagnosticBundleMode.Quick),
                Outcome = "Partial"
            };
            snapshot.Health.Requested = true;
            snapshot.Health.State = "Complete";
            snapshot.Health.Payload = new DiagnosticBundleHealthPayload(new DiagnosticData(), new ScanResult());
            apply!.Invoke(form, [snapshot]);

            var grid = (DataGridView)form.Controls.Find("BundleSourceGrid", true).Single();
            var health = grid.Rows.Cast<DataGridViewRow>()
                .Single(row => Equals(row.Tag, DiagnosticBundleCategory.Health));
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

            Console.WriteLine("Diagnostic bundle stale-options regression: 1/1 passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: diagnostic bundle stale-options regression: " + (ex.InnerException?.Message ?? ex.Message));
            return 253;
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
