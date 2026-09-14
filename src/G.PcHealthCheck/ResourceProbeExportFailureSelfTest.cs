using System.Reflection;

namespace G.PcHealthCheck;

internal static class ResourceProbeExportFailureSelfTest
{
    public static int Run()
    {
        try
        {
            var type = typeof(AssessmentService).Assembly.GetType("G.PcHealthCheck.ResourceProbeForm")
                ?? throw new InvalidOperationException("ResourceProbeForm is not implemented.");
            using var form = (Form)Activator.CreateInstance(type)!;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var statusField = type.GetField("_status", flags);
            var applyFailure = type.GetMethod("ApplyExportFailure", flags);
            Require(statusField is not null && applyFailure is not null,
                "Resource export failure status boundary missing.");

            var status = (Label)statusField!.GetValue(form)!;
            status.Text = "Отчёт сохранён: stale-success";
            applyFailure!.Invoke(form, [new IOException("synthetic export failure")]);

            Require(status.Text.Contains("не заверш", StringComparison.OrdinalIgnoreCase),
                "Resource export failure did not become terminal.");
            Require(status.Text.Contains(nameof(IOException), StringComparison.Ordinal),
                "Resource export failure status omits exception type.");
            Require(status.Text.Contains("0x", StringComparison.OrdinalIgnoreCase),
                "Resource export failure status omits HRESULT.");
            Require(!status.Text.Contains("stale-success", StringComparison.Ordinal),
                "Resource export failure left stale success evidence visible.");

            Console.WriteLine("Resource export failure regression: 1/1 passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: resource export failure regression: " + (ex.InnerException?.Message ?? ex.Message));
            return 203;
        }
    }

    private static void Require(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }
}
