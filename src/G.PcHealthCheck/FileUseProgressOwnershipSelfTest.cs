using System.Reflection;

namespace G.PcHealthCheck;

internal static class FileUseProgressOwnershipSelfTest
{
    public static int Run()
    {
        try
        {
            var type = typeof(MainForm).Assembly.GetType("G.PcHealthCheck.FileUseForm")
                ?? throw new InvalidOperationException("FileUseForm is not implemented.");
            using var form = (Form)Activator.CreateInstance(type)!;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var cancellationField = type.GetField("_cancellation", flags);
            var stageField = type.GetField("_stage", flags);
            var apply = type.GetMethod("ApplyCollectionProgress", flags);
            Require(cancellationField is not null && stageField is not null && apply is not null,
                "File-use progress ownership boundary missing.");

            using var oldRun = new CancellationTokenSource();
            using var newRun = new CancellationTokenSource();
            cancellationField!.SetValue(form, oldRun);
            apply!.Invoke(form, [oldRun, "old run active"]);
            Require((string?)stageField!.GetValue(form) == "old run active", "Active file-use progress was not accepted.");

            cancellationField.SetValue(form, newRun);
            stageField.SetValue(form, "new run waiting");
            apply.Invoke(form, [oldRun, "late old progress"]);
            Require((string?)stageField.GetValue(form) == "new run waiting",
                "Late progress from a previous file-use run overwrote the newer stage.");

            apply.Invoke(form, [newRun, "new run active"]);
            Require((string?)stageField.GetValue(form) == "new run active", "Current file-use progress was not accepted.");

            newRun.Cancel();
            stageField.SetValue(form, "new run cancellation requested");
            apply.Invoke(form, [newRun, "late cancelled progress"]);
            Require((string?)stageField.GetValue(form) == "new run cancellation requested",
                "Progress after file-use cancellation overwrote the cancellation stage.");

            Console.WriteLine("File-use progress ownership regression: 1/1 passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: file-use progress ownership regression: " + (ex.InnerException?.Message ?? ex.Message));
            return 247;
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
