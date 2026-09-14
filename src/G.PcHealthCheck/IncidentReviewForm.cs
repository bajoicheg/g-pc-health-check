using System.Diagnostics;
using System.Text;

namespace G.PcHealthCheck;

internal sealed class IncidentReviewForm : Form
{
    private readonly bool _eventsMode;
    private readonly IncidentEvents _events = new(new WindowsIncidentEventSource());
    private readonly ProcessReview _processes = new(new WindowsProcessReviewSource());
    private readonly DataGridView _grid = new() { Name = "IncidentGrid", Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoGenerateColumns = false };
    private readonly TextBox _detail = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, MaxLength = 0 };
    private readonly TextBox _search = new() { Name = "IncidentSearch", Width = 280, MaxLength = 256, PlaceholderText = AppLocalization.T("Incident.Form.SearchPlaceholder") };
    private readonly DateTimePicker _from = DatePicker();
    private readonly DateTimePicker _to = DatePicker();
    private readonly NumericUpDown _id = new() { Minimum = -1, Maximum = 65535, Value = -1, Width = 80 };
    private readonly ComboBox _log = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 135 };
    private readonly CheckBox _warningsOnly = new() { Text = AppLocalization.T("Incident.Form.WarningsOnly"), AutoSize = true };
    private readonly Button _run = Button(AppLocalization.T("Incident.Form.Run"), "IncidentRun");
    private readonly Button _cancel = Button(AppLocalization.T("Incident.Form.Cancel"), "IncidentCancel");
    private readonly Button _owner = Button(AppLocalization.T("Incident.Form.Owner"), "IncidentOwner");
    private readonly Button _copy = Button(AppLocalization.T("Incident.Form.Copy"), "IncidentCopy");
    private readonly Button _export = Button(AppLocalization.T("Incident.Form.Export"), "IncidentExport");
    private readonly Label _snapshot = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly Label _status = new() { AutoSize = true };
    private readonly ProgressBar _progress = new() { Style = ProgressBarStyle.Marquee, Width = 100, Visible = false };
    private readonly Stopwatch _watch = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private object? _current;
    private CancellationTokenSource? _cancellation;
    private bool _busy;

    public IncidentReviewForm(bool eventsMode)
    {
        _eventsMode = eventsMode;
        Text = AppLocalization.T(eventsMode ? "Incident.Form.Title.Events" : "Incident.Form.Title.Processes");
        Size = new Size(1240, 820); MinimumSize = new Size(900, 650); AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F); StartPosition = FormStartPosition.CenterParent;
        _to.Value = DateTime.Now; _from.Value = _to.Value.AddHours(-1);
        _log.Items.AddRange([AppLocalization.T("Incident.Form.AllLogs"), "Application", "System"]); _log.SelectedIndex = 0;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(12) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 5; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 45)); Controls.Add(root);
        root.Controls.Add(new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 0, 0, 8),
            Text = AppLocalization.T(eventsMode ? "Incident.Form.Intro.Events" : "Incident.Form.Intro.Processes")
        }, 0, 0);
        var dates = Flow(); dates.Visible = eventsMode;
        dates.Controls.AddRange([Label(AppLocalization.T("Incident.Form.From")), _from, Label(AppLocalization.T("Incident.Form.To")), _to]);
        var recent = Button(AppLocalization.T("Incident.Form.RecentHour"), "IncidentRecent");
        recent.Click += (_, _) => { _to.Value = DateTime.Now; _from.Value = _to.Value.AddHours(-1); };
        dates.Controls.Add(recent); root.Controls.Add(dates, 0, 1);
        var filters = Flow(); filters.Controls.Add(_search);
        if (eventsMode) filters.Controls.AddRange([_log, Label(AppLocalization.T("Incident.Form.EventId")), _id, _warningsOnly]);
        root.Controls.Add(filters, 0, 2);
        var tools = Flow(); tools.Controls.AddRange([_run, _cancel, _copy, _export]); if (!eventsMode) tools.Controls.Add(_owner);
        tools.Controls.AddRange([_progress, _status]); root.Controls.Add(tools, 0, 3);
        _snapshot.Text = AppLocalization.T("Incident.Form.NotCollected");
        root.Controls.Add(_snapshot, 0, 4); root.Controls.Add(_grid, 0, 5); root.Controls.Add(_detail, 0, 6);
        ConfigureColumns();
        _search.TextChanged += (_, _) => Render(); _log.SelectedIndexChanged += (_, _) => Render(); _id.ValueChanged += (_, _) => Render(); _warningsOnly.CheckedChanged += (_, _) => Render();
        _from.ValueChanged += (_, _) => IntervalChanged(); _to.ValueChanged += (_, _) => IntervalChanged();
        // SelectionChanged precedes CurrentCellChanged and can still expose the old row.
        _grid.CurrentCellChanged += (_, _) => ShowDetail();
        _run.Click += async (_, _) => await CollectAsync(); _owner.Click += async (_, _) => await OwnerAsync();
        _cancel.Click += (_, _) =>
        {
            _cancellation?.Cancel();
            _cancel.Enabled = false;
            _status.Text = AppLocalization.T("Incident.Form.CancelRequested");
        };
        _copy.Click += (_, _) => { if (_current is { } snapshot) TryUi(() => Clipboard.SetText(IncidentReport.Summary(snapshot, Filter()))); };
        _export.Click += (_, _) => Export();
        _timer.Tick += (_, _) => _status.Text = AppLocalization.T(
            _cancellation?.IsCancellationRequested == true ? "Incident.Form.Timer.Cancel" : "Incident.Form.Timer.Work",
            _watch.Elapsed.TotalSeconds);
        FormClosing += (_, _) => _cancellation?.Cancel(); FormClosed += (_, _) => _timer.Dispose(); UpdateButtons();
        ProcessObservationLink.Attach(this, _grid, _owner, eventsMode);
    }

    private void ConfigureColumns()
    {
        void Add(string name, string caption, Type type, int width, string? format = null) => _grid.Columns.Add(new DataGridViewTextBoxColumn
        { Name = name, HeaderText = caption, ValueType = type, Width = width, SortMode = DataGridViewColumnSortMode.Automatic, DefaultCellStyle = new DataGridViewCellStyle { NullValue = "—", Format = format ?? "" } });
        if (_eventsMode)
        {
            Add("Timestamp", AppLocalization.T("Incident.Column.LocalTime"), typeof(DateTime), 165, "dd.MM.yyyy HH:mm:ss");
            Add("Log", AppLocalization.T("Incident.Column.Log"), typeof(string), 100);
            Add("EventId", "Event ID", typeof(int), 75);
            Add("Level", AppLocalization.T("Incident.Column.Level"), typeof(string), 135);
            Add("Provider", AppLocalization.T("Incident.Column.Provider"), typeof(string), 270);
            Add("EmitterPid", AppLocalization.T("Incident.Column.EmitterPid"), typeof(int), 95);
            Add("RecordId", "Record ID", typeof(long), 100);
        }
        else
        {
            Add("Pid", "PID", typeof(uint), 75);
            Add("Name", AppLocalization.T("Incident.Column.Process"), typeof(string), 185);
            Add("CreatedAt", AppLocalization.T("Incident.Column.StartedLocal"), typeof(DateTime), 165, "dd.MM.yyyy HH:mm:ss");
            Add("MemoryMiB", "RAM, MiB", typeof(double), 90, "N1");
            Add("ParentPid", "PPID", typeof(uint), 75);
            Add("Owner", AppLocalization.T("Incident.Column.OwnerState"), typeof(string), 210);
            Add("Executable", AppLocalization.T("Incident.Column.Path"), typeof(string), 300);
        }
    }

    private object Filter()
        => _eventsMode
            ? new IncidentFilter(_search.Text, _log.SelectedIndex <= 0 ? "" : _log.Text, _id.Value < 0 ? null : (int)_id.Value, _warningsOnly.Checked)
            : _search.Text;

    private IncidentWindow Window()
    {
        static DateTimeOffset Local(DateTime value)
        {
            var time = DateTime.SpecifyKind(value, DateTimeKind.Unspecified); var zone = TimeZoneInfo.Local;
            if (zone.IsInvalidTime(time) || zone.IsAmbiguousTime(time))
                throw new ArgumentException(AppLocalization.T("Incident.Form.TimeAmbiguous"));
            return new DateTimeOffset(time, zone.GetUtcOffset(time));
        }
        var result = new IncidentWindow(Local(_from.Value), Local(_to.Value)); IncidentQueries.Validate(result); return result;
    }

    private bool WindowChanged(IncidentWindow window)
    {
        try { return Window() != window; }
        catch (ArgumentException) { return true; }
    }

    private string EventSnapshotText(IncidentSnapshot events, int visibleCount)
    {
        var logs = string.Join("; ", events.Logs.Select(x => AppLocalization.T(
            "Incident.Form.LogSummary", x.Log, IncidentReport.StateText(x.State), x.Events.Count)));
        var summary = AppLocalization.T(
            "Incident.Form.EventSnapshot",
            events.StartedAt,
            IncidentReport.StateText(events.State),
            events.Window.From,
            events.Window.To,
            logs,
            visibleCount);
        return WindowChanged(events.Window)
            ? AppLocalization.T("Incident.Form.StaleWindow", events.StartedAt, events.Window.From, events.Window.To, summary)
            : summary;
    }

    private void IntervalChanged()
    {
        if (_current is IncidentSnapshot s) _snapshot.Text = EventSnapshotText(s, _grid.Rows.Count);
    }

    private async Task CollectAsync()
    {
        if (_busy) return; IncidentWindow? window = null;
        try { if (_eventsMode) window = Window(); }
        catch (ArgumentException ex) { _status.Text = ex.Message; return; }
        var started = DateTimeOffset.Now; var cancellation = Begin(); _grid.Rows.Clear(); _detail.Clear();
        _snapshot.Text = AppLocalization.T("Incident.Form.Collecting");
        try
        {
            var next = await IncidentOperations.RunAsync<object>(ct => _eventsMode ? _events.Collect(window!, ct) : _processes.Collect(1024, ct), cancellation.Token);
            if (!IsDisposed) _current = next;
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                var state = cancellation.IsCancellationRequested ? "Cancelled" : "Unavailable";
                _current = _eventsMode
                    ? new IncidentSnapshot
                    {
                        Window = window!, StartedAt = started, FinishedAt = DateTimeOffset.Now, State = state,
                        Logs = [new IncidentLogResult { Log = "Application/System", State = state, Warnings = [IncidentQueries.Error(ex)] }]
                    }
                    : new ProcessReviewSnapshot { StartedAt = started, FinishedAt = DateTimeOffset.Now, State = state, Warnings = [IncidentQueries.Error(ex)] };
            }
        }
        finally { End(cancellation); }
        if (!IsDisposed) Render();
    }

    private async Task OwnerAsync()
    {
        if (_busy || _current is not ProcessReviewSnapshot snapshot || _grid.CurrentRow?.Tag is not ProcessReviewEntry selected) return;
        var cancellation = Begin();
        ProcessOwnerEvidence evidence;
        try { evidence = await IncidentOperations.RunAsync(ct => _processes.Owner(selected, ct), cancellation.Token); }
        catch (Exception ex)
        {
            evidence = new(selected.Pid, selected.CreationKey, cancellation.IsCancellationRequested ? "Cancelled" : "Unavailable", "", IncidentQueries.Error(ex), DateTimeOffset.Now);
        }
        finally { End(cancellation); }
        if (IsDisposed) return;
        snapshot.OwnerChecks.Add(evidence); Render(selected);
    }

    private CancellationTokenSource Begin()
    {
        _busy = true; _cancellation = new(); _watch.Restart(); _timer.Start(); UpdateButtons(); return _cancellation;
    }

    private void End(CancellationTokenSource cancellation)
    {
        var cancelled = cancellation.IsCancellationRequested;
        cancellation.Dispose(); _cancellation = null; _busy = false; _watch.Stop();
        if (!IsDisposed)
        {
            _timer.Stop();
            _status.Text = AppLocalization.T(cancelled ? "Incident.Form.Finished.Cancelled" : "Incident.Form.Finished.Done", _watch.Elapsed.TotalSeconds);
            UpdateButtons();
        }
    }

    private void Render(ProcessReviewEntry? preserve = null)
    {
        if (_current is null || _busy || IsDisposed) { UpdateButtons(); return; }
        _grid.Rows.Clear(); var filter = Filter();
        if (_current is IncidentSnapshot events)
        {
            var rows = IncidentQueries.Events(events, (IncidentFilter)filter);
            foreach (var e in rows)
            {
                var i = _grid.Rows.Add(e.Timestamp?.LocalDateTime, e.Log, e.EventId, IncidentReport.LevelText(e.Level), e.Provider, e.EmitterPid, e.RecordId);
                _grid.Rows[i].Tag = e;
            }
            _snapshot.Text = EventSnapshotText(events, rows.Count);
        }
        else if (_current is ProcessReviewSnapshot processes)
        {
            var rows = IncidentQueries.Processes(processes, (string)filter);
            foreach (var p in rows)
            {
                var owner = processes.OwnerChecks.LastOrDefault(x => x.Pid == p.Pid && x.CreationKey == p.CreationKey);
                double? memory = p.WorkingSetBytes is ulong bytes ? bytes / 1048576d : null;
                var ownerText = owner is null
                    ? AppLocalization.T("Incident.Form.OwnerNotRequested")
                    : owner.State == "Verified" ? owner.Owner : IncidentReport.StateText(owner.State);
                var i = _grid.Rows.Add(p.Pid, p.Name, p.CreatedAt?.LocalDateTime, memory, p.ParentPid, ownerText, p.Executable);
                _grid.Rows[i].Tag = p;
                if (preserve is not null && p.Pid == preserve.Pid && p.CreationKey == preserve.CreationKey)
                {
                    _grid.CurrentCell = _grid.Rows[i].Cells[0]; _grid.Rows[i].Selected = true;
                }
            }
            _snapshot.Text = AppLocalization.T(
                "Incident.Form.ProcessSnapshot",
                processes.StartedAt,
                IncidentReport.StateText(processes.State),
                processes.Processes.Count,
                rows.Count,
                processes.Processes.Count(x => x.Warnings.Count > 0));
        }
        ShowDetail(); UpdateButtons();
    }

    private void ShowDetail()
    {
        if (_current is null || _busy) return;
        var text = _grid.CurrentRow?.Tag switch
        {
            IncidentEvent e => AppLocalization.T(
                "Incident.Form.EventDetail",
                e.Timestamp,
                e.Log,
                e.Provider,
                e.EventId,
                e.RecordId,
                IncidentReport.LevelText(e.Level),
                e.EmitterPid,
                e.Message,
                IncidentReport.MessageStateText(e.MessageState)),
            ProcessReviewEntry p => AppLocalization.T(
                "Incident.Form.ProcessDetail",
                p.Pid,
                p.Name,
                p.CreatedAt,
                p.ParentPid,
                p.SessionId,
                p.Threads,
                p.Handles,
                p.WorkingSetBytes?.ToString() ?? "—",
                p.Executable,
                p.CommandLine,
                string.Join("\r\n", p.Warnings)),
            _ => ""
        };
        _detail.Text = text + IncidentReport.Summary(_current, Filter()); UpdateButtons();
    }

    private void UpdateButtons()
    {
        if (IsDisposed) return;
        _run.Enabled = !_busy; _cancel.Enabled = _busy && _cancellation is { IsCancellationRequested: false };
        _copy.Enabled = _export.Enabled = !_busy && _current is not null;
        _owner.Enabled = !_busy && !_eventsMode && _grid.CurrentRow?.Tag is ProcessReviewEntry;
        _search.Enabled = _from.Enabled = _to.Enabled = _id.Enabled = _log.Enabled = _warningsOnly.Enabled = _grid.Enabled = !_busy;
        _progress.Visible = _busy;
    }

    private void Export()
    {
        if (_busy || _current is not { } snapshot) return;
        using var dialog = new FolderBrowserDialog { Description = AppLocalization.T("Incident.Form.ExportDescription"), UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { _status.Text = AppLocalization.T("Incident.Form.ExportSaved", IncidentExport.Save(snapshot, Filter(), dialog.SelectedPath)); }
        catch (Exception ex)
        {
            ApplyExportFailure(ex);
            MessageBox.Show(this, ex.Message, AppLocalization.T("Incident.Form.ActionFailed"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ApplyExportFailure(Exception ex)
        => _status.Text = AppLocalization.T("Incident.Form.ExportFailed", ex.GetType().Name, ex.HResult);

    private void TryUi(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, AppLocalization.T("Incident.Form.ActionFailed"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static DateTimePicker DatePicker() => new() { Width = 185, Format = DateTimePickerFormat.Custom, CustomFormat = "dd.MM.yyyy HH:mm:ss" };
    private static FlowLayoutPanel Flow() => new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
    private static Label Label(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(4, 6, 4, 3) };
    private static Button Button(string text, string name) => new() { Text = text, Name = name, AutoSize = true, Padding = new Padding(4, 2, 4, 2) };
}

internal static class IncidentOperations
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static async Task<T> RunAsync<T>(Func<CancellationToken, T> work, CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await Task.Run(() => work(ct), ct).ConfigureAwait(false); }
        finally { Gate.Release(); }
    }
}

internal static class IncidentReviewMenu
{
    public static void Attach(Form main)
    {
        ArgumentNullException.ThrowIfNull(main); ResourceProbeMenu.Attach(main);
        var menu = main.MainMenuStrip!; var group = (ToolStripMenuItem)menu.Items.Find("ReadOnlyInspections", false).Single();
        foreach (var eventsMode in new[] { true, false })
        {
            var name = eventsMode ? "IncidentEventReview" : "IncidentProcessReview";
            if (menu.Items.Find(name, true).Length > 0) continue;
            var item = new ToolStripMenuItem(AppLocalization.T(eventsMode ? "Incident.Menu.Events" : "Incident.Menu.Processes")) { Name = name };
            var captured = eventsMode; item.Click += (_, _) => { using var form = new IncidentReviewForm(captured); form.ShowDialog(main); };
            group.DropDownItems.Add(item);
        }
    }
}

internal static class IncidentExport
{
    public static string Save(object snapshot, object filter, string parent)
    {
        var json = IncidentReport.Json(snapshot, filter); var html = IncidentReport.Html(snapshot, filter);
        var directory = Path.Combine(parent, $"G-PC-Incident_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"); Directory.CreateDirectory(directory);
        Write(Path.Combine(directory, "snapshot.json"), json); Write(Path.Combine(directory, "report.html"), html); return directory;
    }

    private static void Write(string path, string text)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false, true)); writer.Write(text);
    }
}
