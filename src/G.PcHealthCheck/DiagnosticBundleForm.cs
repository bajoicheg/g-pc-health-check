using System.Diagnostics;

namespace G.PcHealthCheck;

internal sealed class DiagnosticBundleForm : Form
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly ComboBox _mode = new() { Name = "BundleMode", DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly ComboBox _duration = new() { Name = "BundlePerformanceSeconds", DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
    private readonly ComboBox _interval = new() { Name = "BundlePerformanceInterval", DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
    private readonly DataGridView _sources = new()
    {
        Name = "BundleSourceGrid", Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoGenerateColumns = false, BackgroundColor = SystemColors.Window
    };
    private readonly Button _start = Button(AppLocalization.T("Bundle.Form.Start"), "BundleStart");
    private readonly Button _stop = Button(AppLocalization.T("Bundle.Form.Stop"), "BundleStop");
    private readonly Button _copy = Button(AppLocalization.T("Bundle.Form.Copy"), "BundleCopy");
    private readonly Button _save = Button(AppLocalization.T("Bundle.Form.Save"), "BundleSave");
    private readonly Button _close = Button(AppLocalization.T("Bundle.Form.Close"), "BundleClose");
    private readonly CheckBox _zip = new() { Name = "BundleCreateZip", Text = AppLocalization.T("Bundle.Form.CreateZip"), Checked = true, AutoSize = true };
    private readonly TextBox _summary = new() { Name = "BundleSummary", Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _marker = new() { Name = "BundleMarkerText", Width = 300, MaxLength = 160, PlaceholderText = AppLocalization.T("Bundle.Form.MarkerPlaceholder") };
    private readonly Button _mark = Button(AppLocalization.T("Bundle.Form.Mark"), "BundleMarker");
    private readonly Label _context = new() { Name = "BundleContext", AutoSize = true, Dock = DockStyle.Fill };
    private readonly Label _status = new() { Name = "BundleStatus", AutoSize = true, Dock = DockStyle.Fill };
    private readonly ProgressBar _progress = new() { Name = "BundleProgress", Style = ProgressBarStyle.Marquee, Width = 110, Visible = false };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private readonly Stopwatch _elapsed = new();
    private readonly DiagnosticBundleMarkerClock _performanceMarkerClock = new();
    private readonly List<PerformanceMarker> _pendingMarkers = [];
    private CancellationTokenSource? _cancellation;
    private DiagnosticBundleSnapshot? _current;
    private DiagnosticBundleSnapshot? _lastAttempt;
    private bool _busy;
    private bool _saving;
    private bool _performancePhase;
    private string _stage = AppLocalization.T("Bundle.Form.ReadyInitial");

    public DiagnosticBundleForm()
    {
        Name = "DiagnosticBundleForm";
        Text = AppLocalization.T("Bundle.Form.Title");
        Size = new Size(1180, 820); MinimumSize = new Size(900, 650); StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 9F);

        _mode.Items.AddRange([AppLocalization.T("Bundle.Mode.Quick"), AppLocalization.T("Bundle.Mode.Extended")]); _mode.SelectedIndex = 0;
        _duration.Items.AddRange([30, 60]); _duration.SelectedItem = 60;
        _interval.Items.AddRange([1, 2, 5]); _interval.SelectedItem = 2;

        _sources.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Included", HeaderText = AppLocalization.T("Bundle.Form.Column.Include"), Width = 70 });
        _sources.Columns.Add(new DataGridViewTextBoxColumn { Name = "Category", HeaderText = AppLocalization.T("Bundle.Form.Column.Category"), Width = 145, ReadOnly = true });
        _sources.Columns.Add(new DataGridViewTextBoxColumn { Name = "Privacy", HeaderText = AppLocalization.T("Bundle.Form.Column.Privacy"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 360, ReadOnly = true });
        _sources.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = AppLocalization.T("Bundle.Form.Column.State"), Width = 150, ReadOnly = true });
        AddSourceRows();

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 8 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 8),
            Text = AppLocalization.T("Bundle.Form.Intro")
        }, 0, 0);

        var options = Flow();
        options.Controls.AddRange([
            Label(AppLocalization.T("Bundle.Form.ModeLabel")), _mode,
            Label(AppLocalization.T("Bundle.Form.DurationLabel")), _duration,
            Label(AppLocalization.T("Bundle.Form.IntervalLabel")), _interval,
            _zip
        ]);
        root.Controls.Add(options, 0, 1);
        _context.Text = AppLocalization.T("Bundle.Form.ContextPending"); root.Controls.Add(_context, 0, 2);
        root.Controls.Add(_sources, 0, 3);

        var tools = Flow(); tools.Controls.AddRange([_start, _stop, _copy, _save, _close, _progress]); root.Controls.Add(tools, 0, 4);
        var markerTools = Flow(); markerTools.Controls.AddRange([_marker, _mark]); root.Controls.Add(markerTools, 0, 5);
        root.Controls.Add(_summary, 0, 6); root.Controls.Add(_status, 0, 7);

        _summary.Text = AppLocalization.T("Bundle.Form.SummaryEmpty", AppLocalization.T("Bundle.Form.Start"));
        _mode.SelectedIndexChanged += (_, _) => ApplyModeDefaults();
        _duration.SelectedIndexChanged += (_, _) => NextRunOptionsChanged();
        _interval.SelectedIndexChanged += (_, _) => NextRunOptionsChanged();
        _sources.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_sources.IsCurrentCellDirty && _sources.CurrentCell is DataGridViewCheckBoxCell)
                _sources.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _sources.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == _sources.Columns["Included"].Index) NextRunOptionsChanged();
        };
        _start.Click += async (_, _) => await StartAsync();
        _stop.Click += (_, _) => RequestStop();
        _copy.Click += (_, _) => { if (_current is { } snapshot) TryUi(() => Clipboard.SetText(DiagnosticBundleReport.Summary(snapshot))); };
        _save.Click += async (_, _) => await SaveAsync();
        _mark.Click += (_, _) => AddMarker();
        _close.Click += (_, _) => Close();
        _timer.Tick += (_, _) => { if (_busy) _status.Text = AppLocalization.T("Bundle.Form.Elapsed", _stage, _elapsed.Elapsed.TotalSeconds); };
        FormClosing += (_, e) =>
        {
            if (_saving) { e.Cancel = true; _status.Text = AppLocalization.T("Bundle.Form.WaitSave"); return; }
            _cancellation?.Cancel();
        };
        FormClosed += (_, _) => _timer.Dispose();
        AcceptButton = _start; CancelButton = _close;
        ApplyModeDefaults(); UpdateButtons();
    }

    private void AddSourceRows()
    {
        Add(DiagnosticBundleCategory.Health, SourceName(DiagnosticBundleCategory.Health), AppLocalization.T("Bundle.Form.Privacy.Health"));
        Add(DiagnosticBundleCategory.Processes, SourceName(DiagnosticBundleCategory.Processes), AppLocalization.T("Bundle.Form.Privacy.Processes"));
        Add(DiagnosticBundleCategory.Endpoints, SourceName(DiagnosticBundleCategory.Endpoints), AppLocalization.T("Bundle.Form.Privacy.Endpoints"));
        Add(DiagnosticBundleCategory.Events, SourceName(DiagnosticBundleCategory.Events), AppLocalization.T("Bundle.Form.Privacy.Events"));
        Add(DiagnosticBundleCategory.Storage, SourceName(DiagnosticBundleCategory.Storage), AppLocalization.T("Bundle.Form.Privacy.Storage"));
        Add(DiagnosticBundleCategory.Performance, SourceName(DiagnosticBundleCategory.Performance), AppLocalization.T("Bundle.Form.Privacy.Performance"));
    }

    private void Add(DiagnosticBundleCategory category, string name, string privacy)
    {
        var index = _sources.Rows.Add(false, name, privacy, StateText("NotRequested"));
        _sources.Rows[index].Tag = category;
    }

    private void ApplyModeDefaults()
    {
        if (_busy) return;
        var mode = CurrentMode(); var defaults = DiagnosticBundleCore.DefaultOptions(mode);
        foreach (DataGridViewRow row in _sources.Rows)
        {
            if (row.Tag is not DiagnosticBundleCategory category) continue;
            var included = row.Cells["Included"];
            included.Value = defaults.Categories.Contains(category);
            included.ReadOnly = category == DiagnosticBundleCategory.Performance;
            row.Cells["State"].Value = defaults.Categories.Contains(category) ? AppLocalization.T("Bundle.State.Ready") : StateText("NotRequested");
        }
        var extended = mode == DiagnosticBundleMode.Extended;
        _duration.Enabled = _interval.Enabled = extended;
        _marker.Enabled = _mark.Enabled = false;
        if (_current is { } current)
        {
            RenderSources(current);
            NextRunOptionsChanged();
        }
        UpdateButtons();
    }

    private DiagnosticBundleMode CurrentMode() => _mode.SelectedIndex == 1 ? DiagnosticBundleMode.Extended : DiagnosticBundleMode.Quick;

    private HashSet<DiagnosticBundleCategory> SelectedCategories()
        => _sources.Rows.Cast<DataGridViewRow>()
            .Where(row => row.Tag is DiagnosticBundleCategory && Convert.ToBoolean(row.Cells["Included"].Value ?? false))
            .Select(row => (DiagnosticBundleCategory)row.Tag!)
            .ToHashSet();

    private void NextRunOptionsChanged()
    {
        if (_busy || _current is not { } current) return;
        var mode = CurrentMode();
        var reasons = new List<string>();
        if (mode != current.Options.Mode) reasons.Add(AppLocalization.T("Bundle.Form.Reason.Mode"));
        if (!SelectedCategories().SetEquals(current.Options.Categories)) reasons.Add(AppLocalization.T("Bundle.Form.Reason.Categories"));
        if (mode == DiagnosticBundleMode.Extended && current.Options.Mode == DiagnosticBundleMode.Extended)
        {
            var duration = Convert.ToInt32(_duration.SelectedItem ?? 60);
            var interval = Convert.ToInt32(_interval.SelectedItem ?? 2);
            if (duration != current.Options.PerformanceSeconds || interval != current.Options.PerformanceIntervalSeconds)
                reasons.Add(AppLocalization.T("Bundle.Form.Reason.Timing"));
        }

        _status.Text = reasons.Count == 0
            ? AppLocalization.T("Bundle.Form.OptionsSame")
            : AppLocalization.T("Bundle.Form.OptionsChanged", string.Join(", ", reasons));
    }

    private DiagnosticBundleOptions BuildOptions()
    {
        _sources.EndEdit();
        var options = new DiagnosticBundleOptions(CurrentMode(), SelectedCategories(),
            Convert.ToInt32(_duration.SelectedItem ?? 60), Convert.ToInt32(_interval.SelectedItem ?? 2));
        DiagnosticBundleCore.Validate(options);
        return options;
    }

    private async Task StartAsync()
    {
        if (_busy) return;
        DiagnosticBundleOptions options;
        try { options = BuildOptions(); }
        catch (Exception ex) { _status.Text = ex.Message; return; }

        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation; _busy = true; _stage = AppLocalization.T("Bundle.Form.Preparing"); _elapsed.Restart(); _timer.Start();
        _performanceMarkerClock.Reset(); _performancePhase = false; _pendingMarkers.Clear();
        var context = ExecutionContextService.Capture();
        _context.Text = ExecutionPolicy.Describe(context);
        FreezeInputs(true); UpdateButtons();

        var progress = new Progress<DiagnosticBundleProgress>(item => ApplyCollectionProgress(cancellation, item));

        try
        {
            await Gate.WaitAsync(cancellation.Token);
            DiagnosticBundleSnapshot result;
            try
            {
                var service = new DiagnosticBundleService();
                result = await Task.Run(() => service.CollectAsync(options, new WindowsDiagnosticBundleCollector(), context,
                    progress, null, cancellation.Token), cancellation.Token);
            }
            finally { Gate.Release(); }

            ReleaseCollectionProgressOwner(cancellation);
            if (IsDisposed) return;
            if (result.Performance.Payload is { } performance && _pendingMarkers.Count > 0)
                DiagnosticBundleCore.AttachPerformanceMarkers(performance, _pendingMarkers);
            ApplyCollectedResult(result);
        }
        catch (OperationCanceledException)
        {
            ReleaseCollectionProgressOwner(cancellation);
            if (!IsDisposed) _status.Text = AppLocalization.T("Bundle.Form.Cancelled");
        }
        catch (Exception ex)
        {
            ReleaseCollectionProgressOwner(cancellation);
            if (!IsDisposed) _status.Text = AppLocalization.T("Bundle.Form.Failed", ex.GetType().Name, ex.HResult.ToString("X8"));
        }
        finally
        {
            ReleaseCollectionProgressOwner(cancellation); _busy = false; _performancePhase = false; _performanceMarkerClock.Reset(); _elapsed.Stop();
            if (!IsDisposed) { _timer.Stop(); FreezeInputs(false); UpdateButtons(); }
        }
    }

    private void ApplyCollectionProgress(CancellationTokenSource owner, DiagnosticBundleProgress item)
    {
        if (IsDisposed || !ReferenceEquals(_cancellation, owner) || owner.IsCancellationRequested) return;
        _stage = SourceName(item.Category) + ": " + item.Message;
        SetSourceState(item.Category, item.Phase == "Finished" ? AppLocalization.T("Bundle.State.Done") : AppLocalization.T("Bundle.State.Collecting"));
        if (item.Category == DiagnosticBundleCategory.Performance)
        {
            if (item.Phase == "Starting") { _performancePhase = true; _performanceMarkerClock.Start(item.MonotonicTimestamp); }
            if (item.Phase == "Finished") { _performancePhase = false; _performanceMarkerClock.Reset(); }
            UpdateButtons();
        }
    }

    private void ReleaseCollectionProgressOwner(CancellationTokenSource owner)
    {
        if (ReferenceEquals(_cancellation, owner)) _cancellation = null;
    }

    private void RequestStop()
    {
        if (_cancellation is not { IsCancellationRequested: false }) return;
        _cancellation.Cancel(); _stage = AppLocalization.T("Bundle.Form.StopRequested"); UpdateButtons();
    }

    private void AddMarker()
    {
        if (!_busy || !_performancePhase || _pendingMarkers.Count >= 100) return;
        var note = _marker.Text.Trim();
        if (note.Length == 0) { _status.Text = AppLocalization.T("Bundle.Form.MarkerEmpty"); return; }
        if (note.Length > 160) note = note[..160];
        _pendingMarkers.Add(new PerformanceMarker(_performanceMarkerClock.ElapsedMs(Stopwatch.GetTimestamp()), note));
        _marker.Clear(); _status.Text = AppLocalization.T("Bundle.Form.MarkerAdded", _pendingMarkers.Count);
        UpdateButtons();
    }

    private void ApplyCollectedResult(DiagnosticBundleSnapshot result)
    {
        _lastAttempt = result;
        if (result.Sources.Any(source => source.PayloadAvailable))
        {
            _current = result;
            _context.Text = ExecutionPolicy.Describe(result.ExecutionContext);
            RenderSources(result);
            _summary.Text = DiagnosticBundleReport.Summary(result);
            return;
        }

        if (_current is { } current)
        {
            _context.Text = ExecutionPolicy.Describe(current.ExecutionContext);
            RenderSources(current);
            _summary.Text = DiagnosticBundleReport.Summary(current);
            _status.Text = AppLocalization.T("Bundle.Form.NoNewPayload");
            return;
        }

        _context.Text = ExecutionPolicy.Describe(result.ExecutionContext);
        RenderSources(result);
        _summary.Text = DiagnosticBundleReport.Summary(result);
    }

    private async Task SaveAsync()
    {
        if (_busy || _current is not { } snapshot) return;
        var confirm = MessageBox.Show(this,
            AppLocalization.T("Bundle.Form.PrivacyConfirm"),
            AppLocalization.T("Bundle.Form.PrivacyTitle"), MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
        if (confirm != DialogResult.OK) return;
        using var dialog = new FolderBrowserDialog
        {
            Description = AppLocalization.T("Bundle.Form.FolderDescription"),
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var request = CreateSaveRequest(snapshot, dialog.SelectedPath);
        _saving = _busy = true; _stage = AppLocalization.T("Bundle.Form.Saving"); _elapsed.Restart(); _timer.Start(); FreezeInputs(true); UpdateButtons();
        try
        {
            var saved = await request.ExecuteAsync();
            if (!IsDisposed) _status.Text = saved.Zip is null
                ? AppLocalization.T("Bundle.Form.SavedFolder", saved.Folder)
                : AppLocalization.T("Bundle.Form.SavedZip", saved.Folder, saved.Zip);
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                ApplySaveFailure(ex);
                MessageBox.Show(this,
                    AppLocalization.T("Bundle.Form.SaveErrorMessage", ex.Message),
                    AppLocalization.T("Bundle.Form.SaveTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _saving = _busy = false; _elapsed.Stop(); if (!IsDisposed) { _timer.Stop(); FreezeInputs(false); UpdateButtons(); }
        }
    }

    private void ApplySaveFailure(Exception ex)
    {
        _stage = AppLocalization.T("Bundle.Form.SaveFailed", ex.GetType().Name, ex.HResult.ToString("X8"));
        _status.Text = _stage;
    }

    private DiagnosticBundleSaveRequest CreateSaveRequest(DiagnosticBundleSnapshot snapshot, string parentDirectory)
        => new(snapshot, parentDirectory, _zip.Checked);

    private void RenderSources(DiagnosticBundleSnapshot snapshot)
    {
        foreach (var source in snapshot.Sources) SetSourceState(source.Category, StateText(source.State));
    }

    private void SetSourceState(DiagnosticBundleCategory category, string state)
    {
        var row = _sources.Rows.Cast<DataGridViewRow>().FirstOrDefault(item => Equals(item.Tag, category));
        if (row is not null) row.Cells["State"].Value = state;
    }

    private void FreezeInputs(bool frozen)
    {
        _mode.Enabled = !frozen; _zip.Enabled = !frozen;
        foreach (DataGridViewRow row in _sources.Rows)
        {
            if (row.Tag is not DiagnosticBundleCategory category) continue;
            row.Cells["Included"].ReadOnly = frozen || category == DiagnosticBundleCategory.Performance;
        }
        var extended = CurrentMode() == DiagnosticBundleMode.Extended;
        _duration.Enabled = _interval.Enabled = !frozen && extended;
    }

    private void UpdateButtons()
    {
        if (IsDisposed) return;
        _start.Enabled = !_busy; _stop.Enabled = _busy && !_saving && _cancellation is { IsCancellationRequested: false };
        _copy.Enabled = _save.Enabled = !_busy && _current is not null;
        _progress.Visible = _busy; _mark.Enabled = _marker.Enabled = _busy && _performancePhase && _pendingMarkers.Count < 100;
        if (!_busy && string.IsNullOrWhiteSpace(_status.Text)) _status.Text = _lastAttempt is null ? AppLocalization.T("Bundle.Form.ReadyInitial") : AppLocalization.T("Bundle.Form.Ready");
    }

    private static string SourceName(DiagnosticBundleCategory category) => AppLocalization.T(category switch
    {
        DiagnosticBundleCategory.Health => "Bundle.Source.Health",
        DiagnosticBundleCategory.Processes => "Bundle.Source.Processes",
        DiagnosticBundleCategory.Endpoints => "Bundle.Source.Endpoints",
        DiagnosticBundleCategory.Events => "Bundle.Source.Events",
        DiagnosticBundleCategory.Storage => "Bundle.Source.Storage",
        DiagnosticBundleCategory.Performance => "Bundle.Source.Performance",
        _ => "Bundle.Source.Health"
    });

    private static string StateText(string state) => state switch
    {
        "Complete" => AppLocalization.T("Bundle.State.Complete"),
        "Partial" => AppLocalization.T("Bundle.State.Partial"),
        "Unavailable" => AppLocalization.T("Bundle.State.Unavailable"),
        "Cancelled" => AppLocalization.T("Bundle.State.Cancelled"),
        "NotRequested" => AppLocalization.T("Bundle.State.NotRequested"),
        _ => state
    };

    private static FlowLayoutPanel Flow() => new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
    private static Label Label(string text) => new() { Text = text, AutoSize = true, Padding = new Padding(0, 6, 3, 0) };
    private static Button Button(string text, string name) => new() { Text = text, Name = name, AutoSize = true, Padding = new Padding(5, 2, 5, 2) };
    private void TryUi(Action action) { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, AppLocalization.T("Bundle.Form.ActionFailed"), MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
}

internal static class DiagnosticBundleMenu
{
    public static void Attach(Form main)
    {
        ArgumentNullException.ThrowIfNull(main); ReadOnlyReviewMenu.Attach(main);
        var group = (ToolStripMenuItem)main.MainMenuStrip!.Items.Find("ReadOnlyInspections", false).Single();
        if (group.DropDownItems.Find("DiagnosticBundleOpen", false).Length > 0) return;
        var item = new ToolStripMenuItem(AppLocalization.T("Bundle.Menu.Item")) { Name = "DiagnosticBundleOpen" };
        item.Click += (_, _) => { using var window = new DiagnosticBundleForm(); window.ShowDialog(main); };
        group.DropDownItems.Add(item);
    }
}