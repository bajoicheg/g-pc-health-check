using System.Drawing.Drawing2D;

namespace G.PcHealthCheck;

internal sealed class ProcessObservationTimeline : Control
{
    private ProcessObservationSnapshot? _snapshot;
    private ProcessMetric _metric;
    public ProcessObservationTimeline()
    {
        Name = "ProcessTimeline"; AccessibleName = AppLocalization.T("ProcessObservation.Timeline.Accessible");
        DoubleBuffered = true; ResizeRedraw = true; BackColor = Color.White; MinimumSize = new Size(400, 150);
    }
    public void Display(ProcessObservationSnapshot snapshot, ProcessMetric metric) { _snapshot = snapshot; _metric = metric; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var s = _snapshot;
        if (s is null) { e.Graphics.DrawString(AppLocalization.T("ProcessObservation.Timeline.NotStarted"), Font, Brushes.DimGray, 14, 24); return; }
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(64, 28, Math.Max(1, Width - 92), Math.Max(1, Height - 62));
        var segments = ProcessObservationCore.Segments(s, _metric); var xmax = ProcessObservationReport.EndMs(s);
        var ymax = _metric == ProcessMetric.Cpu ? 100 : Math.Max(1, segments.SelectMany(x => x).Select(x => x.Value).DefaultIfEmpty(1).Max());
        float X(long ms) => bounds.Left + (float)(ms / xmax * bounds.Width); float Y(double value) => bounds.Bottom - (float)(value / ymax * bounds.Height);
        using var line = new Pen(Color.FromArgb(35, 134, 192), 2); using var grid = new Pen(Color.FromArgb(220, 229, 237)); using var marks = new Pen(Color.FromArgb(165, 106, 0)) { DashStyle = DashStyle.Dash };
        g.DrawString(ProcessObservationReport.Name(_metric), Font, Brushes.Black, 10, 5);
        for (var i = 0; i <= 4; i++) { var value = ymax * i / 4; var y = Y(value); g.DrawLine(grid, bounds.Left, y, bounds.Right, y); g.DrawString(value.ToString("0.##", AppLocalization.Culture), Font, Brushes.DimGray, 3, y - Font.Height / 2f); }
        foreach (var mark in s.System.Markers.Where(x => x.OffsetMs >= 0 && x.OffsetMs <= xmax)) g.DrawLine(marks, X(mark.OffsetMs), bounds.Top, X(mark.OffsetMs), bounds.Bottom);
        foreach (var segment in segments)
        {
            var points = segment.Select(x => new PointF(X(x.OffsetMs), Y(x.Value))).ToArray();
            if (points.Length > 1) g.DrawLines(line, points);
            foreach (var point in points) g.FillEllipse(Brushes.SteelBlue, point.X - 2, point.Y - 2, 4, 4);
        }
        if (segments.Count == 0) g.DrawString(AppLocalization.T("ProcessObservation.Timeline.NoValues"), Font, Brushes.DimGray, bounds.Left + 10, bounds.Top + 20);
        g.DrawString(AppLocalization.T("ProcessObservation.Unit.Seconds", "0"), Font, Brushes.DimGray, bounds.Left, bounds.Bottom + 8);
        var end = AppLocalization.T("ProcessObservation.Unit.Seconds", (xmax / 1000).ToString("0.0", AppLocalization.Culture));
        g.DrawString(end, Font, Brushes.DimGray, bounds.Right - g.MeasureString(end, Font).Width, bounds.Bottom + 8);
    }
}
