namespace G.PcHealthCheck;

// This is a context/availability view, not an elevated repair launcher.
internal sealed class ExecutionContextForm : Form
{
    private readonly TextBox _facts = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    private readonly DataGridView _matrix = new() { Name = "Availability", Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells };
    private readonly TextBox _detail = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };

    public ExecutionContextForm(ExecutionContextInfo? context)
    {
        Text = AppLocalization.T("ExecutionContext.Form.Title");
        Size = new Size(1080, 780); MinimumSize = new Size(820, 620);
        StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root); root.Controls.Add(_facts, 0, 0); root.Controls.Add(_matrix, 0, 1); root.Controls.Add(_detail, 0, 2);
        _facts.Text = ExecutionPolicy.Describe(context);
        _matrix.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _matrix.Columns.Add(new DataGridViewTextBoxColumn { Name = "Operation", HeaderText = AppLocalization.T("ExecutionContext.Column.Operation"), Width = 240 });
        _matrix.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = AppLocalization.T("ExecutionContext.Column.State"), Width = 155 });
        _matrix.Columns.Add(new DataGridViewTextBoxColumn { Name = "Scope", HeaderText = AppLocalization.T("ExecutionContext.Column.Scope"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        foreach (var (id, titleKey) in new[]
        {
            ("Diagnostics", "ExecutionContext.Action.Diagnostics"), ("TempPreview", "ExecutionContext.Action.TempPreview"),
            ("StartupReview", "ExecutionContext.Action.StartupReview"), ("CleanTemp", "ExecutionContext.Action.CleanTemp"),
            ("FlushDns", "ExecutionContext.Action.FlushDns"), ("Dism", "ExecutionContext.Action.Dism"), ("Sfc", "ExecutionContext.Action.Sfc")
        })
        {
            var availability = ExecutionPolicy.For(id, context ?? new ExecutionContextInfo());
            var row = _matrix.Rows[_matrix.Rows.Add(AppLocalization.T(titleKey), ExecutionPolicy.StateText(availability.State), availability.Scope)];
            row.Tag = availability;
        }
        // SelectionChanged can fire while CurrentRow still points at the previous cell.
        _matrix.CurrentCellChanged += (_, _) => ShowDetail();
        var copy = new Button { Text = AppLocalization.T("ExecutionContext.Copy"), AutoSize = true };
        copy.Click += (_, _) =>
        {
            try
            {
                var lines = _matrix.Rows.Cast<DataGridViewRow>().Select(x => $"{x.Cells[0].Value}: {x.Cells[1].Value}; {x.Cells[2].Value}\r\n{((ActionAvailability)x.Tag!).Reason}");
                Clipboard.SetText(_facts.Text + "\r\n\r\n" + string.Join("\r\n", lines));
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, AppLocalization.T("ExecutionContext.CopyFailed"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        root.Controls.Add(copy, 0, 3); ShowDetail();
    }
    private void ShowDetail() => _detail.Text = (_matrix.CurrentRow?.Tag as ActionAvailability)?.Reason
        ?? AppLocalization.T("ExecutionContext.SelectRow");
}
