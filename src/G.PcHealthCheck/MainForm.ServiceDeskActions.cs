namespace G.PcHealthCheck;

public sealed partial class MainForm
{
    private readonly CheckBox _selectAllAvailable = new()
    {
        Name = "SelectAllAvailable",
        Text = AppLocalization.T("Main.ServiceDesk.SelectAll"),
        AutoSize = true,
        Padding = new Padding(0, 7, 8, 0)
    };
    private readonly Button _makeBetter = BatchButton("MakeBetter", AppLocalization.T("Main.ServiceDesk.MakeBetter"), Color.FromArgb(23, 122, 75));
    private readonly Button _doEverything = BatchButton("DoEverything", AppLocalization.T("Main.ServiceDesk.DoEverything"), Color.FromArgb(181, 54, 54));
    private bool _serviceDeskActionsUiInitialized;
    private bool _syncingSelectAll;

    protected override void OnLoad(EventArgs e)
    {
        EnsureServiceDeskActionsUi();
        base.OnLoad(e);
    }

    private void EnsureServiceDeskActionsUi()
    {
        if (_serviceDeskActionsUiInitialized) return;
        _serviceDeskActionsUiInitialized = true;

        var page = _tabs.TabPages.Count > 0 ? _tabs.TabPages[0] : null;
        var layout = page?.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
        if (layout is null)
            throw new InvalidOperationException(AppLocalization.T("Main.ServiceDesk.MissingPanel"));

        var split = layout.Controls.OfType<SplitContainer>().SingleOrDefault();
        layout.RowCount = 3;
        layout.RowStyles.Clear();
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        if (split is not null) layout.SetRow(split, 2);

        var bar = new FlowLayoutPanel
        {
            Name = "ServiceDeskActionBar",
            Dock = DockStyle.Fill,
            AutoSize = false,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(4, 3, 4, 2),
            Margin = new Padding(0, 0, 0, 4)
        };
        _makeBetter.AutoSize = true;
        _doEverything.AutoSize = true;
        // Until all fixed handlers have passed their own TDD tasks, the red batch is
        // deliberately visible but not requestable. This prevents a partial "all".
        _doEverything.Enabled = ServiceDeskActionRegistry.ExecutableHandlerIds.Count == ServiceDeskActionRegistry.All.Count;
        _doEverything.Cursor = _doEverything.Enabled ? Cursors.Hand : Cursors.Default;
        bar.Controls.AddRange([_selectAllAvailable, _makeBetter, _doEverything]);
        layout.Controls.Add(bar, 0, 1);

        _selectAllAvailable.CheckedChanged += (_, _) => ApplySelectAllAvailable();
        _actions.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == _actions.Columns["Selected"].Index && !_syncingSelectAll)
                SyncSelectAllAvailable();
        };
        _actions.RowsAdded += (_, _) => SyncSelectAllAvailable();
        _actions.RowsRemoved += (_, _) => SyncSelectAllAvailable();
        _makeBetter.Click += async (_, _) => await MakeBetterAsync();
        SyncSelectAllAvailable();
        RefreshMainLocalization();
    }

    private async Task MakeBetterAsync()
    {
        if (_isBusy || _current is null) return;
        BatchPreflight preflight;
        try
        {
            Busy(true, AppLocalization.T("Main.ServiceDesk.CheckRecommended"));
            var context = await Task.Run(ExecutionContextService.Capture);
            RenderExecutionContext(context);
            preflight = ServiceDeskBatchPlanner.Plan(
                BatchMode.RecommendedBestEffort,
                _current.Actions,
                Array.Empty<string>(),
                id => ExecutionPolicy.For(id, context));

            var runnable = preflight.Actions
                .Where(x => x.State == PlannedActionState.Run && ServiceDeskActionRegistry.IsExecutableHandler(x.Descriptor.Id))
                .Select(x => x.Descriptor.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            SelectOnlyActionRows(runnable);
            if (runnable.Count == 0)
            {
                var skipped = preflight.Actions.Where(x => x.State == PlannedActionState.Skipped).ToList();
                var detail = skipped.Count == 0
                    ? AppLocalization.T("Main.ServiceDesk.NoRecommended")
                    : AppLocalization.T(
                        "Main.ServiceDesk.RecommendedUnavailable",
                        string.Join("\n", skipped.Select(x => x.Descriptor.Id + ": " + x.Reason)));
                MessageBox.Show(this, detail, AppLocalization.T("Main.ServiceDesk.MakeBetter"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, AppLocalization.T("Main.ServiceDesk.PrepareFailed"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        finally
        {
            Busy(false);
        }

        // ApplyAsync performs a second fresh native context capture immediately before
        // confirmation/execution. The first capture above is only the planning snapshot.
        await ApplyAsync();
    }

    private void ApplySelectAllAvailable()
    {
        if (_syncingSelectAll) return;
        _syncingSelectAll = true;
        try
        {
            foreach (var row in RequestableAutomatedRows())
                row.Cells["Selected"].Value = _selectAllAvailable.Checked;
        }
        finally
        {
            _syncingSelectAll = false;
        }
        UpdateApplyState();
    }

    private void SelectOnlyActionRows(IReadOnlySet<string> ids)
    {
        _syncingSelectAll = true;
        try
        {
            foreach (DataGridViewRow row in _actions.Rows)
            {
                if (row.Tag is not ActionRecommendation action || !action.CanAutomate || row.Cells["Selected"].ReadOnly) continue;
                row.Cells["Selected"].Value = ids.Contains(action.Id);
            }
        }
        finally
        {
            _syncingSelectAll = false;
        }
        SyncSelectAllAvailable();
        UpdateApplyState();
    }

    private List<DataGridViewRow> RequestableAutomatedRows()
        => _actions.Rows.Cast<DataGridViewRow>()
            .Where(row => row.Tag is ActionRecommendation action
                && action.CanAutomate
                && action.RecommendationClass != RecommendationClass.Manual
                && !row.Cells["Selected"].ReadOnly)
            .ToList();

    private void SyncSelectAllAvailable()
    {
        if (_syncingSelectAll || !_serviceDeskActionsUiInitialized) return;
        var rows = RequestableAutomatedRows();
        var shouldCheck = rows.Count > 0 && rows.All(row => Convert.ToBoolean(row.Cells["Selected"].Value ?? false));
        if (_selectAllAvailable.Checked == shouldCheck) return;
        _syncingSelectAll = true;
        try { _selectAllAvailable.Checked = shouldCheck; }
        finally { _syncingSelectAll = false; }
    }

    private static Button BatchButton(string name, string text, Color background)
        => new()
        {
            Name = name,
            Text = text,
            BackColor = background,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            AutoSize = true,
            Padding = new Padding(10, 3, 10, 3),
            Margin = new Padding(4, 0, 0, 0),
            Cursor = Cursors.Hand
        };
}
