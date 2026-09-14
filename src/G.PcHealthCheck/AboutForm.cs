namespace G.PcHealthCheck;

internal sealed class AboutForm : Form
{
    public AboutForm()
    {
        Name = "AboutForm";
        Text = AppLocalization.T("About.Title");
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(470, 235);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            ColumnCount = 1,
            RowCount = 5,
            BackColor = SystemColors.Window
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        layout.Controls.Add(new Label
        {
            Text = "G PC Health Check",
            Font = new Font("Segoe UI Semibold", 17F),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        });
        layout.Controls.Add(new Label
        {
            Text = AppLocalization.T("About.Version", Application.ProductVersion),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        });
        layout.Controls.Add(new Label
        {
            Text = "© 2026 V. Vasilev",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12)
        });
        layout.Controls.Add(new Label
        {
            Text = AppLocalization.T("About.Purpose"),
            AutoSize = true,
            MaximumSize = new Size(420, 0)
        });
        var close = new Button
        {
            Name = "AboutClose",
            Text = AppLocalization.T("Common.Close"),
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            DialogResult = DialogResult.OK,
            Padding = new Padding(8, 2, 8, 2)
        };
        layout.Controls.Add(close);
        AcceptButton = close;
        CancelButton = close;
    }
}
