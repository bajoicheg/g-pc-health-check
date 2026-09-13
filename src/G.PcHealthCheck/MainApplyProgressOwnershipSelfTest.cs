using System.Reflection;

namespace G.PcHealthCheck;

internal static class MainApplyProgressOwnershipSelfTest
{
    public static int Run()
    {
        try
        {
            using var form = new MainForm();
            var type = typeof(MainForm);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var ownerField = type.GetField("_applyProgressOwner", flags);
            var statusField = type.GetField("_status", flags);
            var apply = type.GetMethod("ApplyOperationProgress", flags);
            Require(ownerField is not null && statusField is not null && apply is not null,
                "Main apply progress ownership boundary missing.");

            var status = (Label)statusField!.GetValue(form)!;
            var remediationPhase = new object();
            var verificationPhase = new object();

            ownerField!.SetValue(form, remediationPhase);
            apply!.Invoke(form, [remediationPhase, "remediation active"]);
            Require(status.Text == "remediation active", "Active remediation progress was not accepted.");

            ownerField.SetValue(form, verificationPhase);
            status.Text = "Повторная диагностика после remediation…";
            apply.Invoke(form, [remediationPhase, "late remediation progress"]);
            Require(status.Text == "Повторная диагностика после remediation…",
                "Late remediation progress overwrote the verification phase status.");

            apply.Invoke(form, [verificationPhase, "verification active"]);
            Require(status.Text == "verification active", "Current verification progress was not accepted.");

            ownerField.SetValue(form, null);
            status.Text = "Готово";
            apply.Invoke(form, [verificationPhase, "late completed progress"]);
            Require(status.Text == "Готово",
                "Progress queued after the apply pipeline completed overwrote the terminal status.");

            Console.WriteLine("Main apply progress ownership regression: 1/1 passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: main apply progress ownership regression: " + (ex.InnerException?.Message ?? ex.Message));
            return 197;
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
