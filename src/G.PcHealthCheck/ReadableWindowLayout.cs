namespace G.PcHealthCheck;

// Clamp physical window bounds to the current monitor; preserve virtual content via scrolling.
internal static class ReadableWindowLayout
{
    internal static void Attach(Form form)
    {
        form.Shown += (_, _) => Fit(form, Screen.FromControl(form).WorkingArea);
        form.DpiChanged += (_, _) => Fit(form, Screen.FromControl(form).WorkingArea);
    }
    internal static void Fit(Form form, Rectangle workArea)
    {
        var minimum = ReadableLayout.WindowSize(form.MinimumSize.Width, form.MinimumSize.Height, workArea.Width, workArea.Height);
        form.MinimumSize = new Size(minimum.Width, minimum.Height);
        var desired = ReadableLayout.WindowSize(form.Width, form.Height, workArea.Width, workArea.Height);
        int left = Math.Clamp(form.Left, workArea.Left, workArea.Right - desired.Width);
        int top = Math.Clamp(form.Top, workArea.Top, workArea.Bottom - desired.Height);
        form.Bounds = new Rectangle(left, top, desired.Width, desired.Height);
    }
}
