using System.Text;

namespace G.PcHealthCheck;

internal sealed class CommonProblemsForm : Form
{
    private readonly CommonProblemsCollector _collector = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _detail = new();
    private readonly Label _status = new();
    private readonly ProgressBar _progress = new() { Style = ProgressBarStyle.Marquee, Width = 120, Visible = false };
    private readonly Button _scan = Button(AppLocalization.T("CommonProblems.Button.Scan"));
    private readonly Button _cancel = Button(AppLocalization.T("CommonProblems.Button.Cancel"));
    private readonly Button _settings = Button(AppLocalization.T("CommonProblems.Button.Settings"));
    private readonly Button _copy = Button(AppLocalization.T("CommonProblems.Button.Copy"));
    private readonly Button _export = Button(AppLocalization.T("CommonProblems.Button.Export"));
    private readonly Button _dns = Button(AppLocalization.T("CommonProblems.Button.Dns"));
    private CommonProblemSnapshot? _current;
    private CommonProblemSnapshot? _previous;
    private RemediationBatchResult? _lastCommand;
    private CancellationTokenSource? _scanCancellation;
    private object? _progressOwner;
    private bool _busy;
    private bool _commandRunning;

    public CommonProblemsForm()
    {
        Text = AppLocalization.T("CommonProblems.Form.Title");
        Size = new Size(1180, 780); MinimumSize = new Size(880, 620);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 5 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);
        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 0, 0, 10),
            Text = AppLocalization.T("CommonProblems.Form.Intro")
        }, 0, 0);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        toolbar.Controls.AddRange([_scan, _cancel, _settings, _copy, _export, _dns, _progress]);
        root.Controls.Add(toolbar, 0, 1);
        _grid.Dock = DockStyle.Fill; _grid.ReadOnly = true; _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false; _grid.AllowUserToDeleteRows = false; _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; _grid.MultiSelect = false;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(CommonProblemFinding.Status), HeaderText = AppLocalization.T("CommonProblems.Column.Status"), Width = 85 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(CommonProblemFinding.Title), HeaderText = AppLocalization.T("CommonProblems.Column.Check"), Width = 320 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(CommonProblemFinding.Evidence), HeaderText = AppLocalization.T("CommonProblems.Column.Evidence"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 220 });
        // SelectionChanged can fire while CurrentRow still points at the previous cell.
        _grid.CurrentCellChanged += (_, _) => ShowDetail();
        root.Controls.Add(_grid, 0, 2);
        _detail.Dock = DockStyle.Fill; _detail.Multiline = true; _detail.ReadOnly = true; _detail.ScrollBars = ScrollBars.Vertical;
        root.Controls.Add(_detail, 0, 3);
        _status.Dock = DockStyle.Fill; _status.AutoSize = true; _status.Text = AppLocalization.T("CommonProblems.Status.Ready");
        root.Controls.Add(_status, 0, 4);
        _scan.Click += async (_, _) => await ScanAsync();
        _cancel.Click += (_, _) =>
        {
            _scanCancellation?.Cancel();
            _cancel.Enabled = false;
            _status.Text = AppLocalization.T("CommonProblems.Status.CancelRequested");
        };
        _settings.Click += (_, _) =>
        {
            if (Selected() is not { } row) return;
            TryUi(() => CommonProblemTools.Open(row.Topic));
        };
        _copy.Click += (_, _) =>
        {
            var current = _current;
            if (current is not null) TryUi(() => Clipboard.SetText(CommonProblemsReport.Summary(current, _previous, _lastCommand)));
        };
        _export.Click += (_, _) => Export();
        _dns.Click += async (_, _) => await FlushDnsAsync();
        Shown += async (_, _) => await ScanAsync();
        FormClosing += (_, e) =>
        {
            if (_commandRunning)
            {
                e.Cancel = true;
                _status.Text = AppLocalization.T("CommonProblems.Status.CommandRunning");
            }
            else _scanCancellation?.Cancel();
        };
        UpdateButtons();
    }

    private async Task ScanAsync()
    {
        if (_busy || IsDisposed) return;
        _scanCancellation = new CancellationTokenSource();
        var cancellation = _scanCancellation;
        var progressOwner = new object(); _progressOwner = progressOwner;
        _busy = true; UpdateButtons();
        try
        {
            var data = await _collector.CollectAsync(Progress(progressOwner, cancellation.Token), cancellation.Token);
            if (IsDisposed || cancellation.IsCancellationRequested) return;
            ReleaseProgressOwner(progressOwner);
            _previous = _current; _current = data;
            _grid.DataSource = CommonProblemsAssessment.Assess(data);
            _status.Text = AppLocalization.T("CommonProblems.Status.Snapshot", data.CollectedAt) + CommandStatus();
            ShowDetail();
        }
        catch (OperationCanceledException)
        {
            ReleaseProgressOwner(progressOwner);
            if (!IsDisposed) _status.Text = AppLocalization.T("CommonProblems.Status.Cancelled") + CommandStatus();
        }
        catch (Exception ex)
        {
            ReleaseProgressOwner(progressOwner);
            if (!IsDisposed) _status.Text = AppLocalization.T("CommonProblems.Status.CollectionFailed", ex.Message) + CommandStatus();
        }
        finally
        {
            ReleaseProgressOwner(progressOwner);
            cancellation.Dispose(); _scanCancellation = null; _busy = false;
            if (!IsDisposed) UpdateButtons();
        }
    }

    private async Task FlushDnsAsync()
    {
        var current = _current;
        if (_busy || current is null || !CommonProblemsAssessment.CanOfferDnsFlush(current)) return;
        if (MessageBox.Show(
                this,
                AppLocalization.T("CommonProblems.Dns.Confirm"),
                AppLocalization.T("CommonProblems.Dns.ConfirmTitle"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        _busy = true; _commandRunning = true; _lastCommand = null;
        var progressOwner = new object(); _progressOwner = progressOwner;
        UpdateButtons();
        var startedAt = DateTime.Now;
        try
        {
            _lastCommand = await RemediationWorker.ExecuteFromGuiAsync(
                [new ActionRecommendation { Id = "FlushDns", CanAutomate = true, RequiresAdmin = false }], 3, Progress(progressOwner));
        }
        catch (Exception ex)
        {
            _lastCommand = CommonProblemCommandResult.UnconfirmedDnsFlush(startedAt, DiagnosticsService.IsAdministrator(), ex);
        }
        finally
        {
            ReleaseProgressOwner(progressOwner);
            _busy = false; _commandRunning = false;
            if (!IsDisposed) { _status.Text = CommandStatus(); UpdateButtons(); }
        }
        if (!IsDisposed) await ScanAsync();
    }

    private string CommandStatus()
    {
        if (_lastCommand is null) return "";
        var items = string.Join("; ", _lastCommand.Actions.Select(x =>
            AppLocalization.T(
                "CommonProblems.Status.CommandItem",
                x.Id,
                AppLocalization.T(x.Success ? "CommonProblems.Status.CommandSuccess" : "CommonProblems.Status.CommandFailed"),
                x.Message)));
        return AppLocalization.T("CommonProblems.Status.LastCommand", items);
    }

    private IProgress<string> Progress(object owner, CancellationToken cancellationToken = default)
        => new Progress<string>(message => ApplyProgress(owner, cancellationToken, message));

    private void ApplyProgress(object owner, CancellationToken cancellationToken, string message)
    {
        if (!IsDisposed && ReferenceEquals(_progressOwner, owner) && !cancellationToken.IsCancellationRequested) _status.Text = message;
    }

    private void ReleaseProgressOwner(object owner)
    {
        if (ReferenceEquals(_progressOwner, owner)) _progressOwner = null;
    }

    private CommonProblemFinding? Selected() => _grid.CurrentRow?.DataBoundItem as CommonProblemFinding;

    private void ShowDetail()
    {
        if (Selected() is { } row)
            _detail.Text = AppLocalization.T(
                "CommonProblems.Detail",
                row.Title,
                row.Evidence.Replace("\n", "\r\n"),
                row.Resolution);
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        _scan.Enabled = !_busy;
        _cancel.Enabled = _busy && !_commandRunning && _scanCancellation is { IsCancellationRequested: false };
        _copy.Enabled = _export.Enabled = !_busy && _current is not null;
        _settings.Enabled = !_busy && Selected() is { } row && CommonProblemTools.UriForTopic(row.Topic) is not null;
        _dns.Enabled = !_busy && _current is not null && CommonProblemsAssessment.CanOfferDnsFlush(_current);
        _progress.Visible = _busy;
    }

    private void Export()
    {
        var current = _current;
        if (current is null || _busy) return;
        using var dialog = new FolderBrowserDialog
        {
            Description = AppLocalization.T("CommonProblems.Export.Description"),
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        TryUi(() =>
        {
            var stem = Path.Combine(dialog.SelectedPath, $"CommonProblems_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");
            WriteNew(stem + ".json", CommonProblemsReport.Json(current, _previous, _lastCommand));
            WriteNew(stem + ".html", CommonProblemsReport.Html(current, _previous, _lastCommand));
            _status.Text = AppLocalization.T("CommonProblems.Export.Saved", stem);
        });
    }

    private static void WriteNew(string path, string text)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text);
    }

    private void TryUi(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, AppLocalization.T("CommonProblems.ActionFailed"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static Button Button(string text) => new() { Text = text, AutoSize = true, Padding = new Padding(5, 2, 5, 2) };
}
