namespace G.PcHealthCheck;

public sealed partial class MainForm
{
    private void ArmReadableLayout(TableLayoutPanel root)
    {
        if (root.GetControlFromPosition(0, 2) is not TableLayoutPanel metrics) return;
        metrics.Name = "AdaptiveMetrics";
        bool busy = false;
        void Arrange()
        {
            if (busy || metrics.ClientSize.Width <= 0) return;
            busy = true;
            try
            {
                var cards = metrics.Controls.OfType<Panel>().ToArray();
                var geometry = ReadableLayout.Metrics(metrics.ClientSize.Width, DeviceDpi / 96f, cards.Length);
                metrics.SuspendLayout();
                metrics.ColumnCount = geometry.Columns; metrics.RowCount = geometry.Rows;
                metrics.ColumnStyles.Clear(); metrics.RowStyles.Clear();
                for (int i = 0; i < geometry.Columns; i++) metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / geometry.Columns));
                for (int i = 0; i < geometry.Rows; i++) metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / geometry.Rows));
                for (int i = 0; i < cards.Length; i++) metrics.SetCellPosition(cards[i], new TableLayoutPanelCellPosition(i % geometry.Columns, i / geometry.Columns));
                root.RowStyles[2].Height = geometry.Height;
                if (root.Parent is Panel viewport)
                {
                    // Keep the tabs/details usable when the physical screen is short.
                    // AutoScroll exposes all content and the footer instead of extending the window off-screen.
                    root.Height = Math.Max(viewport.ClientSize.Height,
                        (int)Math.Ceiling(860 * DeviceDpi / 96f) + geometry.Height - (int)Math.Ceiling(112 * DeviceDpi / 96f));
                }
                metrics.ResumeLayout();
            }
            finally { busy = false; }
        }
        if (root.Parent is Panel viewport) viewport.ClientSizeChanged += (_, _) => Arrange();
        metrics.SizeChanged += (_, _) => Arrange();
        DpiChanged += (_, _) => ReadableWindowLayout.AfterScaling(this, Arrange);
        Arrange();
    }

    private static void ArmProportionalSplit(SplitContainer split, double initialRatio, int upperMin, int lowerMin)
    {
        double ratio = initialRatio; bool arranging = false;
        split.SplitterMoved += (_, _) => { if (!arranging && split.Height > split.SplitterWidth) ratio = (double)split.SplitterDistance / (split.Height - split.SplitterWidth); };
        split.SizeChanged += (_, _) =>
        {
            int available = split.ClientSize.Height - split.SplitterWidth;
            float scale = split.DeviceDpi / 96f;
            if (available <= 0) return;
            arranging = true;
            try
            {
                // Set minimums only when the actual pane can accommodate both.
                split.Panel1MinSize = 0; split.Panel2MinSize = 0;
                int upper = (int)Math.Ceiling(upperMin * scale), lower = (int)Math.Ceiling(lowerMin * scale);
                if (available >= upper + lower)
                {
                    split.SplitterDistance = upperMin == 90 && lowerMin == 80
                        ? ReadableLayout.Split(available, scale, ratio)
                        : Math.Clamp((int)(available * ratio), upper, available - lower);
                    split.Panel1MinSize = upper; split.Panel2MinSize = lower;
                }
                else split.SplitterDistance = available / 2;
            }
            finally { arranging = false; }
        };
    }

    private TextBox? _actionDetails;

    private static void RefreshSelectedDetails(DataGridView grid, TextBox detail)
    {
        var row = grid.CurrentRow;
        detail.Text = row is null || !row.Selected ? "" : string.Join(Environment.NewLine + Environment.NewLine,
            grid.Columns.Cast<DataGridViewColumn>().Select(c => c.HeaderText + ": " + Convert.ToString(row.Cells[c.Index].Value)
                + (c.Name == "Availability" && !string.IsNullOrWhiteSpace(row.Cells[c.Index].ToolTipText)
                    ? Environment.NewLine + row.Cells[c.Index].ToolTipText : "")));
    }

    private Control DetailPane(DataGridView grid, string name)
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Name = name + "Pane" };
        ArmProportionalSplit(split, .70, 40, 35);
        var detail = new TextBox { Name = name, Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, BackColor = SystemColors.Window, AccessibleName = name };
        if (ReferenceEquals(grid, _actions)) _actionDetails = detail;
        void Refresh() => RefreshSelectedDetails(grid, detail);
        grid.CurrentCellChanged += (_, _) => Refresh();
        // SelectionChanged precedes CurrentCellChanged; never read old CurrentRow here.
        grid.SelectionChanged += (_, _) =>
        {
            if (grid.SelectedRows.Count == 0) detail.Clear();
            else if (grid.FindForm() is Form form)
                ReadableWindowLayout.AfterScaling(form, Refresh); // after CurrentCell update, also same-cell reselection
        };
        grid.CellValueChanged += (_, _) => Refresh();
        grid.RowsAdded += (_, _) => Refresh();
        split.Panel1.Controls.Add(grid); split.Panel2.Controls.Add(detail);
        return split;
    }
}
