using System.Reflection;

namespace G.PcHealthCheck;

internal static class CommonProblemsProgressOwnershipSelfTest
{
    public static int Run()
    {
        try
        {
            using var form = new CommonProblemsForm();
            var type = typeof(CommonProblemsForm);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var ownerField = type.GetField("_progressOwner", flags);
            var statusField = type.GetField("_status", flags);
            var apply = type.GetMethod("ApplyProgress", flags);
            Require(ownerField is not null && statusField is not null && apply is not null,
                "Common Problems progress ownership boundary missing.");

            var status = (Label)statusField!.GetValue(form)!;
            var oldScan = new object();
            var command = new object();
            var newScan = new object();

            ownerField!.SetValue(form, oldScan);
            apply!.Invoke(form, [oldScan, CancellationToken.None, "old scan active"]);
            Require(status.Text == "old scan active", "Active Common Problems scan progress was not accepted.");

            ownerField.SetValue(form, command);
            status.Text = "command starting";
            apply.Invoke(form, [oldScan, CancellationToken.None, "late old scan"]);
            Require(status.Text == "command starting",
                "Late scan progress overwrote Common Problems command status.");

            apply.Invoke(form, [command, CancellationToken.None, "command active"]);
            Require(status.Text == "command active", "Active Common Problems command progress was not accepted.");

            ownerField.SetValue(form, newScan);
            status.Text = "follow-up scan starting";
            apply.Invoke(form, [command, CancellationToken.None, "late command progress"]);
            Require(status.Text == "follow-up scan starting",
                "Late command progress overwrote the follow-up Common Problems scan status.");

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            status.Text = "cancellation requested";
            apply.Invoke(form, [newScan, cancelled.Token, "late cancelled scan progress"]);
            Require(status.Text == "cancellation requested",
                "Progress after Common Problems scan cancellation overwrote the cancellation status.");

            ownerField.SetValue(form, null);
            status.Text = "completed status";
            apply.Invoke(form, [newScan, CancellationToken.None, "late completed progress"]);
            Require(status.Text == "completed status",
                "Queued Common Problems progress after completion overwrote the final status.");

            Console.WriteLine("Common problems progress ownership regression: 1/1 passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: common problems progress ownership regression: " + (ex.InnerException?.Message ?? ex.Message));
            return 193;
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
