using System.Reflection;

namespace G.PcHealthCheck;

internal static class IncidentCancelStatusSelfTest
{
    public static int Run()
    {
        try
        {
            var type = typeof(AssessmentService).Assembly.GetType("G.PcHealthCheck.IncidentReviewForm")
                ?? throw new InvalidOperationException("IncidentReviewForm is not implemented.");
            using var form = (Form)Activator.CreateInstance(type, [true])!;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var begin = type.GetMethod("Begin", flags) ?? throw new InvalidOperationException("Incident Begin boundary missing.");
            var end = type.GetMethod("End", flags) ?? throw new InvalidOperationException("Incident End boundary missing.");

            var cancellation = (CancellationTokenSource)begin.Invoke(form, null)!;
            cancellation.Cancel();
            end.Invoke(form, [cancellation]);

            var status = (Label)(type.GetField("_status", flags)?.GetValue(form)
                ?? throw new InvalidOperationException("Incident status label missing."));
            Require(status.Text.Contains("Отмен", StringComparison.OrdinalIgnoreCase),
                "Cancelled incident operation lost its cancellation status after completion.");
            Require(!status.Text.Contains("Завершено", StringComparison.OrdinalIgnoreCase),
                "Cancelled incident operation is presented as successfully completed.");

            Console.WriteLine("Incident cancellation status regression: 1/1 passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: incident cancellation status regression: " + (ex.InnerException?.Message ?? ex.Message));
            return 212;
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
