using System.Reflection;

namespace G.PcHealthCheck;

internal static class MainScanProgressOwnershipSelfTest
{
    public static int Run()
    {
        try
        {
            using var form = new MainForm();
            var type = typeof(MainForm);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var ownerField = type.GetField("_scanProgressOwner", flags);
            var statusField = type.GetField("_status", flags);
            var apply = type.GetMethod("ApplyScanProgress", flags);
            Require(ownerField is not null && statusField is not null && apply is not null,
                "Main scan progress ownership boundary missing.");

            var status = (Label)statusField!.GetValue(form)!;
            var firstRun = new object();
            var secondRun = new object();

            ownerField!.SetValue(form, firstRun);
            apply!.Invoke(form, [firstRun, "first run active"]);
            Require(status.Text == "first run active", "Active main-scan progress was not accepted.");

            ownerField.SetValue(form, secondRun);
            status.Text = "second run waiting";
            apply.Invoke(form, [firstRun, "late first-run progress"]);
            Require(status.Text == "second run waiting",
                "Late progress from a previous main scan overwrote the newer scan status.");

            apply.Invoke(form, [secondRun, "second run active"]);
            Require(status.Text == "second run active", "Current main-scan progress was not accepted.");

            ownerField.SetValue(form, null);
            status.Text = "Готово";
            apply.Invoke(form, [secondRun, "late completed progress"]);
            Require(status.Text == "Готово",
                "Progress queued after main-scan completion overwrote the final status.");

            Console.WriteLine("Main scan progress ownership regression: 1/1 passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: main scan progress ownership regression: " + (ex.InnerException?.Message ?? ex.Message));
            return 195;
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
