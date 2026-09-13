using System.Reflection;

namespace G.PcHealthCheck;

internal static class ReadOnlyReviewProgressOwnershipSelfTest
{
    public static int Run()
    {
        try
        {
            using var form = new ReadOnlyReviewForm(false, 3);
            var type = typeof(ReadOnlyReviewForm);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var cancellationField = type.GetField("_cancellation", flags);
            var statusField = type.GetField("_status", flags);
            var apply = type.GetMethod("ApplyCollectionProgress", flags);
            Require(cancellationField is not null && statusField is not null && apply is not null,
                "Read-only review progress ownership boundary missing.");

            var status = (Label)statusField!.GetValue(form)!;
            using var oldRun = new CancellationTokenSource();
            using var newRun = new CancellationTokenSource();

            cancellationField!.SetValue(form, oldRun);
            apply!.Invoke(form, [oldRun, "old run active"]);
            Require(status.Text == "old run active", "Active read-only review progress was not accepted.");

            cancellationField.SetValue(form, newRun);
            status.Text = "new run waiting";
            apply.Invoke(form, [oldRun, "late old progress"]);
            Require(status.Text == "new run waiting",
                "Late progress from a previous read-only review run overwrote the newer status.");

            apply.Invoke(form, [newRun, "new run active"]);
            Require(status.Text == "new run active", "Current read-only review progress was not accepted.");

            cancellationField.SetValue(form, null);
            status.Text = "completed snapshot";
            apply.Invoke(form, [newRun, "late completed progress"]);
            Require(status.Text == "completed snapshot",
                "Queued progress after read-only review completion overwrote the completed status.");

            cancellationField.SetValue(form, newRun);
            newRun.Cancel();
            status.Text = "cancellation requested";
            apply.Invoke(form, [newRun, "late cancelled progress"]);
            Require(status.Text == "cancellation requested",
                "Progress after read-only review cancellation overwrote the cancellation status.");

            Console.WriteLine("Read-only review progress ownership regression: 1/1 passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: read-only review progress ownership regression: " + (ex.InnerException?.Message ?? ex.Message));
            return 192;
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
