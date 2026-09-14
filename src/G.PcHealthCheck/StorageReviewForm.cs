using System.Diagnostics;

namespace G.PcHealthCheck;

internal sealed class StorageReviewForm : Form
{
    // Closing/reopening must not accumulate background provider work.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly bool _folder;
    private readonly Button _scan = Button(AppLocalization.T("Storage.Form.Scan"), "StorageStart");
    private readonly Button _cancel = Button(AppLocalization.T("Storage.Form.Cancel"), "StorageCancel");
    private readonly Button _copy = Button(AppLocalization.T("Storage.Form.Copy"), "StorageCopy");
    private readonly Button _export = Button(AppLocalization.T("Storage.Form.Export"), "StorageExport");
    private readonly Button _browse = Button(AppLocalization.T("Storage.Form.Browse"), "StorageBrowse");
    private readonly Button _close = Button(AppLocalization.T("Storage.Form.Close"), "StorageClose");
    private readonly TextBox _root = new() { Width = 660, Name = "StorageRoot" };
    private readonly TextBox _search = new() { Width = 360, Name = "StorageSearch", PlaceholderText = AppLocalization.T("Storage.Form.SearchPlaceholder") };
    private readonly ComboBox _view = new() { Width = 255, Name = "StorageView", DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _overview = new() { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly TextBox _detail = new() { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill };
    private readonly DataGridView _grid = new() { Name = "StorageEvidence" };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly ProgressBar _progress = new() { Style = ProgressBarStyle.Marquee, Width = 110, Visible = false };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private CancellationTokenSource? _cancellation;
    private Stopwatch? _elapsed;
    private object? _snapshot;
    private string _stage = AppLocalization.T("Storage.Form.Ready");
    private string _rowsStatus = "";

    public StorageReviewForm(bool folder)
    {
        _folder = folder;
        Text = AppLocalization.T(folder ? "Storage.Form.Title.Folder" : "Storage.Form.Title.Disk");
        Name = folder ? "FolderUsage" : "DiskDetails";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F);
        Size = new Size(1260, 850); MinimumSize = new Size(960, 700);
        StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 8 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 115));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 63));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 37));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);
        layout.Controls.Add(new Label
        {
            AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 8),
            Text = AppLocalization.T(folder ? "Storage.Form.Intro.Folder" : "Storage.Form.Intro.Disk")
        }, 0, 0);
        var target = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Visible = folder };
        _root.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        target.Controls.AddRange([new Label { Text = AppLocalization.T("Storage.Form.FolderLabel"), AutoSize = true, Padding = new Padding(0, 5, 0, 0) }, _root, _browse]);
        layout.Controls.Add(target, 0, 1);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        tools.Controls.AddRange([_scan, _cancel, _copy, _export, _close, _progress]);
        layout.Controls.Add(tools, 0, 2);
        var filter = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        _view.Items.AddRange([AppLocalization.T("Storage.Form.View.All"), AppLocalization.T("Storage.Form.View.First"), AppLocalization.T("Storage.Form.View.Largest")]);
        _view.SelectedIndex = 0; _view.Visible = folder;
        filter.Controls.AddRange([_view, _search]); layout.Controls.Add(filter, 0, 3);
        layout.Controls.Add(_overview, 0, 4);
        _grid.Dock = DockStyle.Fill; _grid.ReadOnly = true; _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false; _grid.AllowUserToDeleteRows = false; _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; _grid.MultiSelect = false;
        _grid.BackgroundColor = SystemColors.Window; _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None; _grid.RowTemplate.Height = 27;
        layout.Controls.Add(_grid, 0, 5); layout.Controls.Add(_detail, 0, 6); layout.Controls.Add(_status, 0, 7);
        _overview.Text = AppLocalization.T("Storage.Form.NotCollected");
        _detail.Text = AppLocalization.T("Storage.Form.SelectRow");
        _status.Text = _stage;
        _scan.Click += async (_, _) => await ScanAsync();
        _cancel.Click += (_, _) =>
        {
            _cancellation?.Cancel();
            _stage = AppLocalization.T("Storage.Form.CancelRequested");
            UpdateButtons();
        };
        _browse.Click += (_, _) => Browse();
        _copy.Click += (_, _) =>
        {
            if (_snapshot is { } snapshot)
                TryUi(() => { Clipboard.SetText(StorageReviewReport.Summary(snapshot)); _status.Text = AppLocalization.T("Storage.Form.Copied"); });
        };
        _export.Click += (_, _) => Export(); _close.Click += (_, _) => Close();
        _view.SelectedIndexChanged += (_, _) => RenderRows(); _search.TextChanged += (_, _) => RenderRows();
        _root.TextChanged += (_, _) => FolderRootChanged();
        _grid.CurrentCellChanged += (_, _) => RenderDetail();
        _timer.Tick += (_, _) =>
        {
            if (_cancellation is not null && !IsDisposed) _status.Text = $"{_stage} · {_elapsed?.Elapsed.TotalSeconds:0.0} s";
        };
        FormClosing += (_, _) => _cancellation?.Cancel();
        AcceptButton = _scan; CancelButton = _close;
        UpdateButtons();
    }

    private async Task ScanAsync()
    {
        if (_cancellation is not null || IsDisposed) return;
        var root = _root.Text; var options = new FolderUsageOptions();
        if (_folder)
        {
            try { root = FolderUsageService.Validate(root, options); }
            catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, AppLocalization.T("Storage.Form.PathTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        }
        var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        _elapsed = Stopwatch.StartNew();
        _stage = AppLocalization.T("Storage.Form.WaitCollector");
        _timer.Start(); UpdateButtons();
        var progress = new Progress<string>(text =>
        {
            if (!IsDisposed && ReferenceEquals(_cancellation, cancellation) && !cancellation.IsCancellationRequested) _stage = text;
        });
        try
        {
            await Gate.WaitAsync(cancellation.Token);
            object snapshot;
            try
            {
                _stage = AppLocalization.T(_folder ? "Storage.Form.Stage.Folder" : "Storage.Form.Stage.Disk");
                snapshot = await Task.Run<object>(() => _folder
                    ? FolderUsageService.Collect(root, options, new WindowsFolderUsageSource(), cancellation.Token, progress)
                    : DiskDetailsService.Collect(new WindowsDiskDetailsSource(), cancellation.Token), cancellation.Token);
            }
            finally { Gate.Release(); }
            if (IsDisposed || Disposing) return;
            // Stopped snapshots keep completed records and explicitly describe incomplete data.
            DisplaySnapshot(snapshot);
            var outcome = snapshot is FolderUsageSnapshot f ? f.Outcome : ((DiskDetailsSnapshot)snapshot).Outcome;
            _status.Text = AppLocalization.T("Storage.Form.Done", StorageReviewReport.OutcomeText(outcome), _elapsed.Elapsed.TotalSeconds, AppLocalization.T("Storage.Form.Export"));
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed) _status.Text = AppLocalization.T("Storage.Form.Cancelled");
        }
        catch (Exception ex)
        {
            if (!IsDisposed) _status.Text = AppLocalization.T("Storage.Form.Failed", ex.GetType().Name, ex.HResult.ToString("X8"));
        }
        finally
        {
            _cancellation = null; cancellation.Dispose();
            if (!IsDisposed && !Disposing) { _timer.Stop(); UpdateButtons(); }
        }
    }

    internal void DisplaySnapshot(object snapshot)
    {
        if ((_folder && snapshot is not FolderUsageSnapshot) || (!_folder && snapshot is not DiskDetailsSnapshot))
            throw new ArgumentException(AppLocalization.T("Storage.Form.SnapshotTypeMismatch"), nameof(snapshot));
        _snapshot = snapshot;
        _overview.Text = StorageReviewReport.Summary(snapshot).ReplaceLineEndings("\r\n");
        RenderRows(); UpdateButtons();
    }
    private void RenderRows()
    {
        if (IsDisposed) return;
        _grid.Rows.Clear(); _grid.Columns.Clear();
        var query = _search.Text.Trim();
        bool Matches(string text) => query.Length == 0 || text.Contains(query, StringComparison.OrdinalIgnoreCase);
        var matched = 0;
        if (_snapshot is FolderUsageSnapshot f)
        {
            var total = f.Folders.FirstOrDefault()?.Bytes ?? 0;
            if (_view.SelectedIndex == 2)
            {
                Column("Bytes", AppLocalization.T("Storage.Column.SizeMb"), 135, typeof(double), "N1");
                Column("Modified", AppLocalization.T("Storage.Column.Modified"), 185, typeof(DateTimeOffset), "dd.MM.yyyy HH:mm:ss zzz");
                Column("Path", AppLocalization.T("Storage.Column.FullPath"), 600, typeof(string), fill: true);
                var rows = f.LargestFiles.Where(x => Matches(x.Path)).ToList(); matched = rows.Count;
                foreach (var row in rows) _grid.Rows[_grid.Rows.Add(HumanSize.MegabytesValue(row.Bytes), row.Modified, row.Path)].Tag = row;
            }
            else
            {
                Column("Bytes", AppLocalization.T("Storage.Column.TotalMb"), 155, typeof(double), "N1");
                Column("Files", AppLocalization.T("Storage.Column.Files"), 95, typeof(int), "N0");
                Column("Percent", AppLocalization.T("Storage.Column.Percent"), 120, typeof(decimal), "0.0");
                Column("Scope", AppLocalization.T("Storage.Column.Scope"), 130, typeof(string));
                Column("Path", AppLocalization.T("Storage.Column.Folder"), 550, typeof(string), fill: true);
                var rows = f.Folders.Where(x => (_view.SelectedIndex != 1 || x.ParentIndex == 0) && Matches(x.Path))
                    .OrderByDescending(x => x.Bytes).ThenBy(x => x.Path, StringComparer.Ordinal).ToList(); matched = rows.Count;
                foreach (var row in rows.Take(2000))
                {
                    var percent = total > 0 ? row.Bytes / total * 100m : 0m;
                    _grid.Rows[_grid.Rows.Add(HumanSize.MegabytesDecimalValue(row.Bytes), row.Files, percent,
                        AppLocalization.T(row.Incomplete ? "Storage.Column.Incomplete" : "Storage.Column.InScope"), row.Path)].Tag = row;
                }
            }
        }
        else if (_snapshot is DiskDetailsSnapshot d)
        {
            Column("Attention", AppLocalization.T("Storage.Column.Attention"), 95, typeof(string)); Column("Id", "DeviceId", 85, typeof(string));
            Column("Name", AppLocalization.T("Storage.Column.Drive"), 240, typeof(string), fill: true); Column("Health", "HealthStatus Windows", 195, typeof(string));
            Column("Size", AppLocalization.T("Storage.Column.SizeGiB"), 110, typeof(decimal), "0.0"); Column("Temperature", "°C", 65, typeof(int));
            Column("Wear", AppLocalization.T("Storage.Column.Wear"), 90, typeof(int)); Column("Hours", AppLocalization.T("Storage.Column.Hours"), 100, typeof(ulong), "N0");
            Column("Counters", AppLocalization.T("Storage.Column.Related"), 120, typeof(string));
            var rows = d.Disks.Where(x => Matches(x.Name + " " + x.DeviceId + " " + x.Firmware + " " + DiskDetailsService.BusText(x.BusType)))
                .OrderBy(x => StorageReviewReport.Rank(DiskDetailsService.Attention(x))).ToList(); matched = rows.Count;
            foreach (var disk in rows)
            {
                var attention = DiskDetailsService.Attention(disk);
                var i = _grid.Rows.Add(attention, disk.DeviceId, disk.Name, DiskDetailsService.HealthText(disk.Health),
                    disk.Size is ulong size ? (decimal)size / 1073741824m : null,
                    disk.Reliability?.Temperature, disk.Reliability?.Wear, disk.Reliability?.PowerOnHours, disk.CounterState);
                _grid.Rows[i].Tag = disk;
                if (attention == "CRIT") _grid.Rows[i].DefaultCellStyle.BackColor = Color.MistyRose;
                else if (attention == "WARN") _grid.Rows[i].DefaultCellStyle.BackColor = Color.LemonChiffon;
            }
        }
        if (_snapshot is not null && _cancellation is null)
        {
            _rowsStatus = AppLocalization.T("Storage.Form.RowsStatus", _grid.Rows.Count, matched);
            RenderRowsStatus();
        }
        RenderDetail();
    }
    private bool FolderRootMatches(FolderUsageSnapshot snapshot)
    {
        try
        {
            var current = FolderUsageService.Validate(_root.Text, snapshot.Options);
            return string.Equals(current, snapshot.Root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }
    private void FolderRootChanged()
    {
        if (_cancellation is null && _snapshot is FolderUsageSnapshot) RenderRowsStatus();
    }
    private void RenderRowsStatus()
    {
        if (_snapshot is null || _cancellation is not null) return;
        _status.Text = _snapshot is FolderUsageSnapshot folder && !FolderRootMatches(folder)
            ? AppLocalization.T("Storage.Form.RootChanged", folder.Root, _rowsStatus)
            : _rowsStatus;
    }
    private void RenderDetail()
    {
        _detail.Text = _grid.CurrentRow?.Tag switch
        {
            FolderUsageRow f => AppLocalization.T("Storage.Form.Detail.Folder", f.Path, StorageReviewReport.Bytes(f.Bytes), f.Files,
                StorageReviewReport.Bytes(f.OwnBytes), f.OwnFiles,
                AppLocalization.T(f.Incomplete ? "Storage.Form.Detail.Incomplete" : "Storage.Form.Detail.Complete"), StorageReviewReport.FolderScope),
            LargeFolderFile f => AppLocalization.T("Storage.Form.Detail.File", f.Path, StorageReviewReport.Bytes(f.Bytes), f.Modified?.ToString("O") ?? "—"),
            PhysicalDiskDetail d => DiskDetailsService.Describe(d).ReplaceLineEndings("\r\n"),
            _ => AppLocalization.T("Storage.Form.Detail.Empty")
        };
    }
    private void Column(string name, string caption, int width, Type type, string format = "", bool fill = false)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name, HeaderText = caption, Width = width, ValueType = type,
            SortMode = DataGridViewColumnSortMode.Automatic, MinimumWidth = 60,
            AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
            DefaultCellStyle = new DataGridViewCellStyle { Format = format, NullValue = "—" }
        });
    }
    private void Browse()
    {
        if (_cancellation is not null) return;
        using var dialog = new FolderBrowserDialog { Description = AppLocalization.T("Storage.Form.BrowseDescription"), UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) _root.Text = dialog.SelectedPath;
    }
    private void Export()
    {
        if (_snapshot is not { } snapshot || _cancellation is not null) return;
        using var dialog = new FolderBrowserDialog { Description = AppLocalization.T("Storage.Form.ExportDescription"), UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { _status.Text = AppLocalization.T("Storage.Form.Saved", StorageReviewReport.Save(snapshot, dialog.SelectedPath)); }
        catch (Exception ex)
        {
            ApplyExportFailure(ex);
            MessageBox.Show(this, ex.Message, AppLocalization.T("Storage.Form.ActionFailedTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    private void ApplyExportFailure(Exception ex)
    {
        if (!IsDisposed) _status.Text = AppLocalization.T("Storage.Form.ExportFailed", ex.GetType().Name, ex.HResult.ToString("X8"));
    }
    private void UpdateButtons()
    {
        var busy = _cancellation is not null;
        _scan.Enabled = !busy; _cancel.Enabled = busy && !_cancellation!.IsCancellationRequested;
        _copy.Enabled = _export.Enabled = !busy && _snapshot is not null;
        _root.Enabled = _browse.Enabled = !busy; _progress.Visible = busy;
    }
    private void TryUi(Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, AppLocalization.T("Storage.Form.ActionFailedTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _cancellation?.Cancel(); _timer.Dispose(); }
        base.Dispose(disposing);
    }
    private static Button Button(string title, string name) => new() { Text = title, Name = name, AutoSize = true, Padding = new Padding(5, 2, 5, 2) };
}

internal static class StorageReviewMenu
{
    public static void Attach(Form main)
    {
        ArgumentNullException.ThrowIfNull(main);
        ReadOnlyReviewMenu.Attach(main);
        var group = (ToolStripMenuItem)main.MainMenuStrip!.Items.Find("ReadOnlyInspections", false).Single();
        if (group.DropDownItems.Find("StorageFolderAnalysis", false).Length > 0) return;
        var folder = new ToolStripMenuItem(AppLocalization.T("Storage.Menu.Folder")) { Name = "StorageFolderAnalysis" };
        folder.Click += (_, _) => { using var window = new StorageReviewForm(true); window.ShowDialog(main); };
        var disk = new ToolStripMenuItem(AppLocalization.T("Storage.Menu.Disk")) { Name = "StorageDiskDetails" };
        disk.Click += (_, _) => { using var window = new StorageReviewForm(false); window.ShowDialog(main); };
        group.DropDownItems.Add(folder); group.DropDownItems.Add(disk);
    }
}
