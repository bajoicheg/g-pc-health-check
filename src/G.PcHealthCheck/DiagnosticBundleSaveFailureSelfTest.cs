using System.Reflection;

namespace G.PcHealthCheck;

internal static class DiagnosticBundleSaveFailureSelfTest
{
    public static int Run()
    {
        try
        {
            var type = typeof(MainForm).Assembly.GetType("G.PcHealthCheck.DiagnosticBundleForm");
            Require(type is not null, "Diagnostic bundle form missing.");
            using var form = (Form)Activator.CreateInstance(type!)!;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var statusField = type!.GetField("_status", flags);
            var stageField = type.GetField("_stage", flags);
            var applyFailure = type.GetMethod("ApplySaveFailure", flags);
            Require(statusField is not null && stageField is not null && applyFailure is not null,
                "Diagnostic bundle save failure status boundary missing.");

            var status = (Label)statusField!.GetValue(form)!;
            status.Text = "Сохраняю пакет… · 1,0 с";
            stageField!.SetValue(form, "Сохраняю пакет…");
            applyFailure!.Invoke(form, [new IOException("synthetic bundle save failure")]);

            var stage = (string?)stageField.GetValue(form) ?? "";
            Require(status.Text.Contains("Сохранение не завершено", StringComparison.Ordinal)
                && status.Text.Contains("IOException", StringComparison.Ordinal)
                && status.Text.Contains("0x", StringComparison.Ordinal)
                && !status.Text.Contains("Сохраняю", StringComparison.Ordinal),
                "Diagnostic bundle save failure left the UI looking like an active save.");
            Require(stage == status.Text && !stage.Contains("Сохраняю", StringComparison.Ordinal),
                "Diagnostic bundle timer stage can overwrite the terminal save failure status.");

            Console.WriteLine("Diagnostic bundle save failure regression: 1/1 passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: diagnostic bundle save failure regression: " + (ex.InnerException?.Message ?? ex.Message));
            return 198;
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
