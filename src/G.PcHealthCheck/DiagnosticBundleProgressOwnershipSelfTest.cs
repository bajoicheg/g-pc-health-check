using System.Reflection;

namespace G.PcHealthCheck;

internal static class DiagnosticBundleProgressOwnershipSelfTest
{
    public static int Run()
    {
        try
        {
            var type = typeof(MainForm).Assembly.GetType("G.PcHealthCheck.DiagnosticBundleForm")
                ?? throw new InvalidOperationException("DiagnosticBundleForm is not implemented.");
            using var form = (Form)Activator.CreateInstance(type)!;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var cancellationField = type.GetField("_cancellation", flags);
            var stageField = type.GetField("_stage", flags);
            var apply = type.GetMethod("ApplyCollectionProgress", flags);
            Require(cancellationField is not null && stageField is not null && apply is not null,
                "Diagnostic bundle progress ownership boundary missing.");

            var grid = (DataGridView)form.Controls.Find("BundleSourceGrid", true).Single();
            var health = grid.Rows.Cast<DataGridViewRow>()
                .Single(row => Equals(row.Tag, DiagnosticBundleCategory.Health));

            using var oldRun = new CancellationTokenSource();
            using var newRun = new CancellationTokenSource();
            cancellationField!.SetValue(form, oldRun);
            apply!.Invoke(form, [oldRun, Progress("old run active")]);
            Require(((string?)stageField!.GetValue(form))?.Contains("old run active", StringComparison.Ordinal) == true,
                "Active diagnostic-bundle progress was not accepted.");
            Require(string.Equals(health.Cells["State"].Value?.ToString(), "Сбор…", StringComparison.Ordinal),
                "Active diagnostic-bundle source state was not updated.");

            cancellationField.SetValue(form, newRun);
            stageField.SetValue(form, "new run waiting");
            health.Cells["State"].Value = "Собрано";
            apply.Invoke(form, [oldRun, Progress("late old progress")]);
            Require(string.Equals((string?)stageField.GetValue(form), "new run waiting", StringComparison.Ordinal),
                "Late progress from a previous diagnostic-bundle run overwrote the newer stage.");
            Require(string.Equals(health.Cells["State"].Value?.ToString(), "Собрано", StringComparison.Ordinal),
                "Late progress from a previous diagnostic-bundle run overwrote saved source evidence.");

            apply.Invoke(form, [newRun, Progress("new run active")]);
            Require(((string?)stageField.GetValue(form))?.Contains("new run active", StringComparison.Ordinal) == true,
                "Current diagnostic-bundle progress was not accepted.");

            newRun.Cancel();
            stageField.SetValue(form, "new run cancellation requested");
            health.Cells["State"].Value = "Собрано";
            apply.Invoke(form, [newRun, Progress("late cancelled progress")]);
            Require(string.Equals((string?)stageField.GetValue(form), "new run cancellation requested", StringComparison.Ordinal),
                "Progress after diagnostic-bundle cancellation overwrote the cancellation stage.");
            Require(string.Equals(health.Cells["State"].Value?.ToString(), "Собрано", StringComparison.Ordinal),
                "Progress after diagnostic-bundle cancellation overwrote saved source evidence.");

            Console.WriteLine("Diagnostic bundle progress ownership regression: 1/1 passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: diagnostic bundle progress ownership regression: " + (ex.InnerException?.Message ?? ex.Message));
            return 196;
        }
    }

    private static DiagnosticBundleProgress Progress(string message)
        => new(DiagnosticBundleCategory.Health, "Starting", message, DateTimeOffset.UtcNow);

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
