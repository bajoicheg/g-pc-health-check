using System.Reflection;

namespace G.PcHealthCheck;

internal static class AnalysisAutoCollectSelfTest
{
    private sealed class ProbeForm : Form
    {
        public void RaiseShown() => OnShown(EventArgs.Empty);
    }

    public static int Run()
    {
        var failures = new List<string>();
        var count = 0;
        void Test(string name, Action action)
        {
            count++;
            try { action(); }
            catch (Exception ex)
            {
                failures.Add(name + ": " + ex.GetBaseException().Message);
                Console.Error.WriteLine("FAIL: " + failures[^1]);
            }
        }

        var assembly = typeof(MainForm).Assembly;
        Type Policy() => assembly.GetType("G.PcHealthCheck.AnalysisAutoCollect")
            ?? throw new InvalidOperationException("Safe Analysis auto-collection coordinator is missing.");
        MethodInfo Method(string name) => Policy().GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"AnalysisAutoCollect.{name} is missing.");
        bool Attached(Form form) => (bool)(Method("IsAttached").Invoke(null, [form])
            ?? throw new InvalidOperationException("AnalysisAutoCollect.IsAttached returned no value."));

        Test("shared Shown gate starts collection once", () =>
        {
            using var form = new ProbeForm();
            var invocations = 0;
            Func<Task> collect = () => { invocations++; return Task.CompletedTask; };
            Method("Attach").Invoke(null, [form, collect]);
            Require(Attached(form), "Attached safe form is not registered.");
            form.RaiseShown();
            form.RaiseShown();
            Application.DoEvents();
            Require(invocations == 1, $"Shown auto-collection ran {invocations} times instead of once.");
        });

        Test("safe Analysis views are wired for auto-collection", () =>
        {
            var forms = new Form[]
            {
                new ReadOnlyReviewForm(true, 3),
                new ReadOnlyReviewForm(false, 3),
                new IncidentReviewForm(true),
                new IncidentReviewForm(false),
                new EndpointReviewForm(),
                new StorageReviewForm(true),
                new StorageReviewForm(false),
                new PerformanceSessionForm(),
                new DiagnosticBundleForm()
            };
            try
            {
                foreach (var form in forms)
                    Require(Attached(form), $"{form.GetType().Name} is not wired to safe auto-collection.");
            }
            finally
            {
                foreach (var form in forms) form.Dispose();
            }
        });

        Test("targeted or consent-driven Analysis views remain idle", () =>
        {
            var forms = new Form[]
            {
                new ResourceProbeForm(),
                new FileUseForm(),
                new ProcessObservationForm(new ProcessObservationTarget(42, DateTimeOffset.Now, "Synthetic process"))
            };
            try
            {
                foreach (var form in forms)
                    Require(!Attached(form), $"{form.GetType().Name} must remain idle until explicit target/consent/start.");
            }
            finally
            {
                foreach (var form in forms) form.Dispose();
            }
        });

        Console.WriteLine($"Analysis auto-collection self-test: {count - failures.Count}/{count} passed.");
        return failures.Count == 0 ? 0 : 231;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
