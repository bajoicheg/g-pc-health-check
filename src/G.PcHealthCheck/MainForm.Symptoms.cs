namespace G.PcHealthCheck;
public sealed partial class MainForm
{
    private readonly Button _symptomButton = new() { Name = "SymptomEntry", AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
    private List<SymptomNote> _symptomNotes = [];
    private Control SymptomEntry()
    {
        var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0), WrapContents = true };
        _symptomButton.Text = AppLocalization.T("Evidence.Symptom.Title");
        row.Controls.Add(_symptomButton);
        _symptomButton.Click += (_, _) =>
        {
            if (_isBusy) return;
            var menu = new ContextMenuStrip();
            foreach (var route in SymptomRoutes.All)
            {
                var item = menu.Items.Add(AppLocalization.T(route.TitleKey));
                item.Click += (_, _) => OpenSymptom(route);
            }
            menu.Closed += (_, _) => menu.Dispose();
            menu.Show(_symptomButton, new Point(0, _symptomButton.Height));
        };
        return row;
    }
    private void OpenSymptom(SymptomRoute route)
    {
        if (_isBusy) return;
        if (_current is null) _symptomNotes = SymptomRoutes.Select(_symptomNotes, route.Id);
        else
        {
            try
            {
                var saved = _reports.SaveSymptomSelection(_current, route.Id);
                _current = saved.Snapshot;
                _symptomNotes = saved.Snapshot.SymptomNotes.ToList();
                _latestReport = saved.Html;
            }
            catch (Exception)
            {
                MessageBox.Show(this, AppLocalization.T("Evidence.Symptom.SaveFailed"), "G PC Health", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }
        using Form form = route.ToolId switch
        {
            "performance-session" => new PerformanceSessionForm(),
            "incident-review" => new IncidentReviewForm(true),
            "resource-probe" => new ResourceProbeForm(),
            "common-problems" => new CommonProblemsForm(autoScan: false),
            _ => throw new ArgumentException("Unknown symptom tool")
        };
        form.ShowDialog(this);
    }
}
