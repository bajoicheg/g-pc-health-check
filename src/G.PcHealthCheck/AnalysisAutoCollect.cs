using System.Runtime.CompilerServices;

namespace G.PcHealthCheck;

internal static class AnalysisAutoCollect
{
    private const int WmShowWindow = 0x0018;

    private sealed class State
    {
        public bool Attached { get; set; }
        public bool Started { get; set; }
        public Func<Task>? Collect { get; set; }
    }

    private sealed class ShowMessageFilter : IMessageFilter
    {
        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg == WmShowWindow && m.WParam != IntPtr.Zero && Control.FromHandle(m.HWnd) is Form form)
                EnsureEligibleAttached(form, startIfVisible: false);
            return false;
        }
    }

    private static readonly ConditionalWeakTable<Form, State> States = new();
    private static readonly IMessageFilter Filter = new ShowMessageFilter();
    private static bool _installed;

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;
        Application.AddMessageFilter(Filter);
        Application.Idle += OnApplicationIdle;
    }

    internal static void Attach(Form form, Func<Task> collect)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(collect);
        var state = States.GetValue(form, _ => new State());
        if (state.Attached) return;
        Hook(form, state, collect);
    }

    internal static bool IsAttached(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        // ReadOnlyReviewForm already has its original Shown-based auto-collection path.
        // Treat it as coordinated without adding a second handler.
        if (form is ReadOnlyReviewForm) return true;
        EnsureEligibleAttached(form, startIfVisible: false);
        return States.TryGetValue(form, out var state) && state.Attached;
    }

    private static void OnApplicationIdle(object? sender, EventArgs e)
    {
        foreach (var form in Application.OpenForms.Cast<Form>().ToArray())
            EnsureEligibleAttached(form, startIfVisible: true);
    }

    private static void EnsureEligibleAttached(Form form, bool startIfVisible)
    {
        if (form.IsDisposed || form is ReadOnlyReviewForm || !IsEligible(form)) return;
        var state = States.GetValue(form, _ => new State());
        if (!state.Attached)
            Hook(form, state, () => TriggerStartButtonAsync(form));

        if (startIfVisible && form.Visible && !state.Started && form.IsHandleCreated)
        {
            try
            {
                form.BeginInvoke(new Action(async () => await StartOnceAsync(form, state)));
            }
            catch (InvalidOperationException) when (form.IsDisposed || !form.IsHandleCreated)
            {
            }
        }
    }

    private static bool IsEligible(Form form)
        => form is IncidentReviewForm
            or EndpointReviewForm
            or StorageReviewForm
            or PerformanceSessionForm
            or DiagnosticBundleForm;

    private static void Hook(Form form, State state, Func<Task> collect)
    {
        state.Attached = true;
        state.Collect = collect;
        form.Shown += async (_, _) => await StartOnceAsync(form, state);
    }

    private static async Task StartOnceAsync(Form form, State state)
    {
        if (state.Started || form.IsDisposed) return;
        state.Started = true;
        var collect = state.Collect ?? throw new InvalidOperationException("Auto-collection callback is missing.");
        await collect();
    }

    private static Task TriggerStartButtonAsync(Form form)
    {
        var buttonName = form switch
        {
            IncidentReviewForm => "IncidentRun",
            EndpointReviewForm => "EndpointStart",
            StorageReviewForm => "StorageStart",
            PerformanceSessionForm => "SessionStart",
            DiagnosticBundleForm => "BundleStart",
            _ => ""
        };
        if (buttonName.Length == 0) return Task.CompletedTask;
        var button = form.Controls.Find(buttonName, true).OfType<Button>().SingleOrDefault()
            ?? throw new InvalidOperationException($"Safe auto-collection start control {buttonName} is missing on {form.GetType().Name}.");
        button.PerformClick();
        return Task.CompletedTask;
    }
}
