namespace G.PcHealthCheck;

internal sealed class ProcessObservationForm : Form
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly ProcessObservationTarget _target;
    private readonly ComboBox _duration = Choice([30, 60, 120, 300, 600], 120);
    private readonly ComboBox _interval = Choice([1, 2, 5], 2);
    private readonly ComboBox _processMetric = new() { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _systemMetric = new() { Width = 245, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _start = Button(AppLocalization.T("ProcessObservation.Form.Start"), "ObservationStart");
    private readonly Button _stop = Button(AppLocalization.T("ProcessObservation.Form.Stop"), "ObservationStop");
    private readonly Button _mark = Button(AppLocalization.T("ProcessObservation.Form.Mark"), "ObservationMark");
    private readonly Button _copy = Button(AppLocalization.T("ProcessObservation.Form.Copy"), "ObservationCopy");
    private readonly Button _export = Button(AppLocalization.T("ProcessObservation.Form.Export"), "ObservationExport");
    private readonly TextBox _note = new() { Width = 220, MaxLength = 160, Text = AppLocalization.T("ProcessObservation.Form.NoteDefault") };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = true, Text = AppLocalization.T("ProcessObservation.Form.Ready") };
    private readonly Label _stats = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly ProgressBar _progress = new() { Width = 90, Style = ProgressBarStyle.Marquee, Visible = false };
    private readonly ProcessObservationTimeline _processChart = new() { Dock = DockStyle.Fill };
    private readonly PerformanceTimeline _systemChart = new() { Name = "SystemTimeline", Dock = DockStyle.Fill };
    private readonly DataGridView _grid = new() { Name = "ObservationSamples", Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, MultiSelect = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
    private readonly TextBox _detail = TextArea();
    private readonly TextBox _overview = TextArea();
    private readonly ListBox _markers = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private readonly List<PerformanceMarker> _marks = [];
    private ProcessObservationSnapshot? _current;
    private ProcessObservationSnapshot? _live;
    private MonotonicPerformanceClock? _clock;
    private CancellationTokenSource? _cancellation;
    private bool _busy;
    private bool _exporting;

    public ProcessObservationForm(ProcessObservationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target); _target = target;
        if (target.Pid == 0 || !ProcessObservationCore.ValidCreatedAt(target.CreatedAt)) throw new ArgumentException(AppLocalization.T("ProcessObservation.Form.InvalidTarget"), nameof(target));
        Text = AppLocalization.T("ProcessObservation.Form.Title"); AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 9F);
        Size = new Size(1280, 930); MinimumSize = new Size(1020, 760); StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 7 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 31)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 31)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 38)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Controls.Add(root);
        root.Controls.Add(new Label { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 0, 0, 6), Text = AppLocalization.T("ProcessObservation.Form.Intro", target.Name, target.Pid, target.CreatedAt) }, 0, 0);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        tools.Controls.AddRange([Caption(AppLocalization.T("ProcessObservation.Form.Duration")), _duration, Caption(AppLocalization.T("ProcessObservation.Form.Interval")), _interval, _start, _stop, _progress]); tools.SetFlowBreak(_progress, true);
        tools.Controls.AddRange([_processMetric, _systemMetric, _note, _mark, _copy, _export]); root.Controls.Add(tools, 0, 1);
        foreach (var metric in Enum.GetValues<ProcessMetric>()) _processMetric.Items.Add(ProcessObservationReport.Name(metric));
        foreach (var metric in Enum.GetValues<SessionMetric>()) _systemMetric.Items.Add(PerformanceSessionReport.Name(metric));
        _processMetric.SelectedIndex = _systemMetric.SelectedIndex = 0;
        root.Controls.Add(_stats, 0, 2); root.Controls.Add(_processChart, 0, 3); root.Controls.Add(_systemChart, 0, 4);
        Column("ProcessOffset", AppLocalization.T("ProcessObservation.Column.ProcessOffset"), typeof(double), 95, "0.000"); Column("State", AppLocalization.T("ProcessObservation.Column.State"), typeof(string), 170);
        Column("ProcessCpu", "CPU, %", typeof(double), 80); Column("Working", AppLocalization.T("ProcessObservation.Column.Working"), typeof(double), 110); Column("Private", "Private, MiB", typeof(double), 100);
        Column("Read", AppLocalization.T("ProcessObservation.Column.Read"), typeof(double), 110); Column("Write", AppLocalization.T("ProcessObservation.Column.Write"), typeof(double), 110); Column("SystemOffset", AppLocalization.T("ProcessObservation.Column.SystemOffset"), typeof(double), 110, "0.000");
        Column("SystemCpu", AppLocalization.T("ProcessObservation.Column.SystemCpu"), typeof(double), 95); Column("Memory", AppLocalization.T("ProcessObservation.Column.SystemMemory"), typeof(double), 95); Column("Disk", AppLocalization.T("ProcessObservation.Column.Disk"), typeof(double), 90); Column("Queue", AppLocalization.T("ProcessObservation.Column.Queue"), typeof(double), 90);
        var tabs = new TabControl { Dock = DockStyle.Fill }; Page(tabs, AppLocalization.T("ProcessObservation.Tab.Measurements"), _grid); Page(tabs, AppLocalization.T("ProcessObservation.Tab.Markers"), _markers); Page(tabs, AppLocalization.T("ProcessObservation.Tab.Detail"), _detail); Page(tabs, AppLocalization.T("ProcessObservation.Tab.Summary"), _overview); root.Controls.Add(tabs, 0, 5); root.Controls.Add(_status, 0, 6);
        _overview.Text = AppLocalization.T("ProcessObservation.Form.Overview", ProcessObservationReport.Boundary);
        _start.Click += async (_, _) => await RunAsync();
        _stop.Click += (_, _) => { _cancellation?.Cancel(); _status.Text = AppLocalization.T("ProcessObservation.Form.StopRequested"); UpdateButtons(); };
        _mark.Click += (_, _) => Mark(); _copy.Click += (_, _) => { if (_current is { } s) TryUi(() => Clipboard.SetText(ProcessObservationReport.Summary(s))); };
        _export.Click += async (_, _) => await ExportAsync();
        _duration.SelectedIndexChanged += (_, _) => SessionOptionsChanged(); _interval.SelectedIndexChanged += (_, _) => SessionOptionsChanged();
        _processMetric.SelectedIndexChanged += (_, _) => Display(); _systemMetric.SelectedIndexChanged += (_, _) => Display();
        _grid.CurrentCellChanged += (_, _) => { if (_grid.CurrentRow?.Tag is ProcessObservationSample sample) _detail.Text = ProcessObservationReport.Detail(sample); };
        _timer.Tick += (_, _) =>
        {
            if (_clock is not { } clock || _live is not { } live) return;
            live.System.ElapsedMs = clock.ElapsedMs;
            if (_cancellation?.IsCancellationRequested != true) _status.Text = AppLocalization.T("ProcessObservation.Form.Running", clock.ElapsedMs / 1000d, live.System.Options.DurationSeconds, live.Samples.Count, _marks.Count);
            Display();
        };
        FormClosing += (_, e) => { if (_exporting) { e.Cancel = true; _status.Text = AppLocalization.T("ProcessObservation.Form.WaitSave"); } else _cancellation?.Cancel(); };
        UpdateButtons();
    }
    private PerformanceSessionOptions CurrentOptions() => new((int)_duration.SelectedItem!, (int)_interval.SelectedItem!);
    private string SavedStatusText(ProcessObservationSnapshot snapshot)
    {
        var evidence = snapshot.System.Outcome == "Failed"
            ? AppLocalization.T("ProcessObservation.Form.Interrupted")
            : AppLocalization.T("ProcessObservation.Form.SavedStatus", PerformanceSessionReport.Outcome(snapshot.System.Outcome), snapshot.System.ElapsedMs / 1000d, snapshot.Samples.Count,
                snapshot.Samples.Count == 0 ? AppLocalization.T("ProcessObservation.Form.NoData") : ProcessObservationCore.StateText(snapshot.Samples[^1].Process.Counters.State));
        return CurrentOptions() == snapshot.System.Options
            ? evidence
            : AppLocalization.T("ProcessObservation.Form.PreviousOptions", snapshot.System.Options.DurationSeconds, snapshot.System.Options.IntervalSeconds, evidence);
    }
    private void SessionOptionsChanged()
    {
        if (!_busy && _current is { } snapshot) _status.Text = SavedStatusText(snapshot);
    }
    private async Task RunAsync()
    {
        if (_busy || IsDisposed) return;
        if (!Gate.Wait(0)) { _status.Text = AppLocalization.T("ProcessObservation.Form.PreviousBusy"); return; }
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation; _busy = true;
        var clock = new MonotonicPerformanceClock(); _clock = clock;
        var options = CurrentOptions();
        var live = new ProcessObservationSnapshot { Target = _target, System = new() { Options = options, StartedAt = clock.Now, Outcome = "Running" } };
        _live = live; _current = null; _marks.Clear(); _markers.Items.Clear(); _grid.Rows.Clear(); _detail.Clear(); _stats.Text = AppLocalization.T("ProcessObservation.Form.WaitFirst"); _timer.Start(); UpdateButtons();
        try
        {
            var progress = new Progress<ProcessObservationSample>(sample =>
            {
                if (IsDisposed || !ReferenceEquals(_live, live) || !_busy) return;
                live.Samples.Add(sample); live.System.Samples.Add(sample.System); live.System.ElapsedMs = clock.ElapsedMs; Add(sample); Display();
            });
            var contextProgress = new Progress<ExecutionContextInfo>(context => { if (!IsDisposed && ReferenceEquals(_live, live)) live.ExecutionContext = context; });
            var result = await Task.Run(async () =>
            {
                var context = ExecutionContextService.Capture(); ((IProgress<ExecutionContextInfo>)contextProgress).Report(context);
                using var process = new WindowsProcessObservationSource(_target); using var system = new WindowsPerformanceSessionSource();
                return await ProcessObservationService.RunAsync(_target, options, process, system, clock, context, progress, cancellation.Token);
            });
            if (IsDisposed) return;
            result.System.Markers = _marks.ToList(); _current = result; _live = null;
            _grid.Rows.Clear(); foreach (var sample in result.Samples) Add(sample);
            _overview.Text = ProcessObservationReport.Summary(result); Display();
            _status.Text = SavedStatusText(result);
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            live.System.Outcome = cancellation.IsCancellationRequested ? "Stopped" : "Failed"; live.System.FinishedAt = clock.Now; live.System.ElapsedMs = clock.ElapsedMs;
            live.System.Markers = _marks.ToList(); live.System.Warnings.Add(AppLocalization.T("ProcessObservation.Form.SessionError", ex.GetType().Name, ex.HResult.ToString("X8")));
            _current = live; _live = null; _overview.Text = ProcessObservationReport.Summary(live); _status.Text = SavedStatusText(live); Display();
        }
        finally { Gate.Release(); _cancellation = null; _clock = null; _busy = false; if (!IsDisposed) { _timer.Stop(); UpdateButtons(); } }
    }
    private void Mark()
    {
        if (!_busy || _clock is null || _cancellation?.IsCancellationRequested != false) return;
        if (!PerformanceStatistics.AddMarker(_marks, _clock.ElapsedMs, _note.Text)) { _status.Text = AppLocalization.T("ProcessObservation.Form.MarkerInvalid"); return; }
        var marker = _marks[^1]; _markers.Items.Add(AppLocalization.T("ProcessObservation.Form.MarkerItem", marker.OffsetMs / 1000d, marker.Note));
        if (_live is { } live) live.System.Markers = _marks.ToList(); Display();
    }
    private void Display()
    {
        var snapshot = _live ?? _current; if (snapshot is null || _processMetric.SelectedIndex < 0 || _systemMetric.SelectedIndex < 0) return;
        _processChart.Display(snapshot, (ProcessMetric)_processMetric.SelectedIndex); _systemChart.Display(snapshot.System, (SessionMetric)_systemMetric.SelectedIndex);
        _stats.Text = ProcessObservationReport.Statistics(snapshot, (ProcessMetric)_processMetric.SelectedIndex);
    }
    private void Add(ProcessObservationSample s)
    {
        var i = _grid.Rows.Add(s.Process.OffsetMs / 1000d, ProcessObservationCore.StateText(s.Process.Counters.State), s.Reading.CpuPercent, s.Reading.WorkingSetMiB, s.Reading.PrivateCommitMiB, s.Reading.ReadMiBps, s.Reading.WriteMiBps, s.System.OffsetMs / 1000d, s.System.Reading.CpuPercent, s.System.Reading.MemoryUsedPercent, s.System.Reading.DiskBusyPercent, s.System.Reading.DiskQueueLength);
        _grid.Rows[i].Tag = s;
    }
    private async Task ExportAsync()
    {
        if (_busy || _current is not { } current) return;
        using var dialog = new FolderBrowserDialog { Description = AppLocalization.T("ProcessObservation.Form.ExportDescription"), UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var parent = dialog.SelectedPath; _busy = _exporting = true; _status.Text = AppLocalization.T("ProcessObservation.Form.Saving"); UpdateButtons();
        try { var path = await Task.Run(() => ProcessObservationReport.Save(current, parent)); if (!IsDisposed) _status.Text = AppLocalization.T("ProcessObservation.Form.Saved", path); }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                ApplyExportFailure(ex);
                MessageBox.Show(this, AppLocalization.T("ProcessObservation.Form.ExportMessage", ex.Message), AppLocalization.T("ProcessObservation.Form.ExportTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally { _busy = _exporting = false; if (!IsDisposed) UpdateButtons(); }
    }
    private void ApplyExportFailure(Exception ex)
    {
        _status.Text = AppLocalization.T("ProcessObservation.Form.SaveFailed", ex.GetType().Name, ex.HResult.ToString("X8"));
    }
    private void UpdateButtons()
    {
        _start.Enabled = _duration.Enabled = _interval.Enabled = !_busy;
        _stop.Enabled = _mark.Enabled = _note.Enabled = _busy && _cancellation is { IsCancellationRequested: false };
        _copy.Enabled = _export.Enabled = !_busy && _current is not null; _progress.Visible = _busy;
    }
    private void Column(string name, string title, Type type, int width, string format = "0.##")
        => _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = title, ValueType = type, Width = width, SortMode = DataGridViewColumnSortMode.Automatic, DefaultCellStyle = new DataGridViewCellStyle { Format = type == typeof(string) ? "" : format, NullValue = "—" } });
    private static ComboBox Choice(int[] values, int selected) { var c = new ComboBox { Width = 70, DropDownStyle = ComboBoxStyle.DropDownList }; foreach (var x in values) c.Items.Add(x); c.SelectedItem = selected; return c; }
    private static Button Button(string text, string name) => new() { Text = text, Name = name, AutoSize = true, Padding = new Padding(4, 2, 4, 2) };
    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(4, 7, 4, 3) };
    private static TextBox TextArea() => new() { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private static void Page(TabControl tabs, string name, Control control) { var page = new TabPage(name); page.Controls.Add(control); tabs.TabPages.Add(page); }
    private void TryUi(Action action) { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, AppLocalization.T("ProcessObservation.Form.ActionFailed"), MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
    protected override void Dispose(bool disposing) { if (disposing) { _cancellation?.Cancel(); _timer.Dispose(); } base.Dispose(disposing); }
}

internal static class ProcessObservationLink
{
    public static void Attach(Form owner, DataGridView grid, Button ownerQuery, bool eventsMode)
    {
        if (eventsMode || owner.Controls.Find("ObserveSelectedProcess", true).Length != 0) return;
        var parent = ownerQuery.Parent ?? throw new InvalidOperationException(AppLocalization.T("ProcessObservation.Link.PanelMissing"));
        var button = new Button { Name = "ObserveSelectedProcess", Text = AppLocalization.T("ProcessObservation.Link.Button"), AutoSize = true, Enabled = false, Padding = new Padding(4, 2, 4, 2) }; parent.Controls.Add(button);
        void Refresh()
        {
            button.Enabled = ownerQuery.Enabled && grid.Enabled && grid.CurrentRow?.Tag is ProcessReviewEntry p && p.Pid > 0 && p.CreatedAt is DateTimeOffset created && ProcessObservationCore.ValidCreatedAt(created);
        }
        grid.CurrentCellChanged += (_, _) => Refresh(); grid.EnabledChanged += (_, _) => Refresh(); ownerQuery.EnabledChanged += (_, _) => Refresh();
        button.Click += (_, _) =>
        {
            if (!button.Enabled || grid.CurrentRow?.Tag is not ProcessReviewEntry row) return;
            try { using var form = new ProcessObservationForm(ProcessObservationCore.FromEntry(row)); form.ShowDialog(owner); }
            catch (Exception ex) { MessageBox.Show(owner, ex.Message, AppLocalization.T("ProcessObservation.Link.UnavailableTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        Refresh();
    }
}
