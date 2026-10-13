namespace G.PcHealthCheck;

public sealed partial class MainForm
{
    private TableLayoutPanel? _mainLocalizationRoot;
    private bool _mainLocalizationInitialized;
    private bool _refreshingMainLocalization;

    private void InitializeMainLocalization(TableLayoutPanel root)
    {
        if (_mainLocalizationInitialized) return;
        _mainLocalizationInitialized = true;
        _mainLocalizationRoot = root;
        InitializeSecurityPostureUi(root);
        AppLocalization.CultureChanged += OnMainCultureChanged;
        Disposed += (_, _) => AppLocalization.CultureChanged -= OnMainCultureChanged;

        // UpdateApplyState() runs once after BuildUi() and still owns the selected-count
        // semantics. This hook keeps its caption localized without using translated text
        // as program logic.
        _apply.TextChanged += (_, _) => LocalizeApplyButton();
        RefreshMainLocalization();
    }

    private void OnMainCultureChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (IsHandleCreated && InvokeRequired)
        {
            BeginInvoke((Action)RefreshMainLocalization);
            return;
        }
        RefreshMainLocalization();
    }

    private void RefreshMainLocalization()
    {
        if (_refreshingMainLocalization || _mainLocalizationRoot is null) return;
        _refreshingMainLocalization = true;
        try
        {
            LocalizeHeader();
            LocalizeMetrics();
            LocalizeTabsAndColumns();
            RefreshSecurityLocalization();

            _scan.Text = AppLocalization.T("Main.Button.Rescan");
            _openReport.Text = AppLocalization.T("Main.Button.OpenReport");
            _openFolder.Text = AppLocalization.T("Main.Button.ReportsFolder");
            _copySummary.Text = AppLocalization.T("Main.Button.CopySummary");
            LocalizeApplyButton();

            _contextDetails.Text = AppLocalization.T("Main.ExecutionContext.Details");
            _selectAllAvailable.Text = AppLocalization.T("Main.ServiceDesk.SelectAll");
            _makeBetter.Text = AppLocalization.T("Main.ServiceDesk.MakeBetter");
            _doEverything.Text = AppLocalization.T("Main.ServiceDesk.DoEverything");
            RenderExecutionContext(_executionContext);
            RefreshActionAvailability();

            if (_current is { } scan)
            {
                _state.Text = scan.Assessment.Status switch
                {
                    "OK" => AppLocalization.T("Main.State.Ok"),
                    "WARN" => AppLocalization.T("Main.State.Warn"),
                    _ => AppLocalization.T("Main.State.Critical")
                };
                _uptime.Text = AppLocalization.T("Main.Uptime.Days", scan.Data.System.UptimeDays);
                var triage = TriageSummary.Build(scan);
                _triageTitle.Text = triage.Title;
                _triageDetail.Text = triage.Detail;
                _tabs.TabPages["RecommendationsTab"]!.Text = triage.SignificantCount > 0
                    ? AppLocalization.T("Main.Tab.Recommendations.Count", triage.SignificantCount)
                    : AppLocalization.T("Main.Tab.Recommendations");
                RefreshRecommendationDisplayRows();
                RefreshSystemDisplayRows(scan);
            }

            if (!string.IsNullOrWhiteSpace(_status.Text))
                _status.Text = _isBusy ? AppLocalization.T("Main.Status.Working") : AppLocalization.T("Main.Status.ReadySimple");
        }
        finally
        {
            _refreshingMainLocalization = false;
        }
    }

    private void LocalizeHeader()
    {
        if (_mainLocalizationRoot?.GetControlFromPosition(0, 0) is not Control header) return;
        var subtitle = header.Controls.OfType<Label>()
            .Where(x => !ReferenceEquals(x, _host))
            .OrderBy(x => x.Font.Size)
            .FirstOrDefault();
        if (subtitle is not null) subtitle.Text = AppLocalization.T("Main.Header.Subtitle");
    }

    private void LocalizeMetrics()
    {
        if (_mainLocalizationRoot?.GetControlFromPosition(0, 2) is not TableLayoutPanel metrics) return;
        SetMetric(metrics, 0, "Main.Metric.Score", null);
        SetMetric(metrics, 1, "Main.Metric.Coverage", "Main.Metric.Coverage.Sub");
        SetMetric(metrics, 2, null, "Main.Metric.Cpu.Sub");
        SetMetric(metrics, 3, null, "Main.Metric.Ram.Sub");
        SetMetric(metrics, 4, "Main.Metric.SystemDisk", "Main.Metric.SystemDisk.Sub");
        SetMetric(metrics, 5, null, "Main.Metric.Uptime.Sub");
        SetMetric(metrics, 6, "Security.Metric.Caption", null);
    }

    private static IEnumerable<Control> DescendantsForLocalization(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in DescendantsForLocalization(child)) yield return descendant;
        }
    }

    private static void SetMetric(TableLayoutPanel metrics, int column, string? captionKey, string? subKey)
    {
        if (metrics.Controls.OfType<Panel>().ElementAtOrDefault(column) is not Panel card || card.Controls.Count < 3) return;
        if (captionKey is not null && card.Controls[0] is Label caption) caption.Text = AppLocalization.T(captionKey);
        if (subKey is not null) card.Controls[2].Text = AppLocalization.T(subKey);
    }

    private void LocalizeTabsAndColumns()
    {
        _symptomButton.Text = AppLocalization.T("Evidence.Symptom.Title");
        foreach (var (name, key) in new[] {
            ("OverviewTab", "Main.Tab.Overview"), ("RecommendationsTab", "Main.Tab.Recommendations"),
            ("ActionsTab", "Main.Tab.Actions"), ("ProcessesTab", "Main.Tab.Processes"),
            ("EventsTab", "Main.Tab.Events"), ("SystemTab", "Main.Tab.System"),
            ("CompareTab", "Main.Tab.Compare"), ("SecurityPostureTab", "Security.Tab.Title") })
            if (_tabs.TabPages[name] is TabPage page) page.Text = AppLocalization.T(key);
        foreach (var toggle in DescendantsForLocalization(this).OfType<Button>().Where(x => x.Name is "FindingDetailsToggle" or "ActionDetailsToggle" or "SecurityDetailsToggle"))
        {
            toggle.Text = AppLocalization.T("Main.Details.Toggle");
            toggle.AccessibleName = toggle.Text;
        }

        SetColumn(_actions, "Kind", "Main.Column.Type");
        SetColumn(_actions, "Title", "Main.Column.Action");
        SetColumn(_actions, "Reason", "Main.Column.Why");
        SetColumn(_actions, "Risk", "Main.Column.Risk");
        SetColumn(_actions, "Verify", "Main.Column.AutoVerify");
        SetColumn(_actions, "Availability", "Main.Column.Availability");

        SetColumn(_findings, "Severity", "Main.Column.Status");
        SetColumn(_findings, "Category", "Main.Column.Category");
        SetColumn(_findings, "Title", "Main.Column.Observation");
        SetColumn(_findings, "Value", "Main.Column.Value");
        SetColumn(_findings, "Recommendation", "Main.Column.Recommendation");

        SetColumn(_events, "Log", "Main.Column.Log");
        SetColumn(_events, "Count", "Main.Column.Count");
        SetColumn(_events, "Last", "Main.Column.Last");
        SetColumn(_system, "Property", "Main.Column.Parameter");
        SetColumn(_system, "Value", "Main.Column.Value");
        SetColumn(_compare, "Metric", "Main.Column.Metric");
        SetColumn(_compare, "Before", "Main.Column.Before");
        SetColumn(_compare, "After", "Main.Column.After");
        SetColumn(_compare, "Delta", "Main.Column.Change");
        SetColumn(_remediation, "Action", "Main.Column.Action");
        SetColumn(_remediation, "Result", "Main.Column.Result");
        SetColumn(_remediation, "Details", "Main.Column.DetailsContext");
        SetColumn(_topCpu, "Process", "Main.Column.Process");
        SetColumn(_topRam, "Process", "Main.Column.Process");
        SetColumn(_topIo, "Process", "Main.Column.Process");
    }

    private static void SetColumn(DataGridView grid, string name, string key)
    {
        if (grid.Columns.Contains(name)) grid.Columns[name].HeaderText = AppLocalization.T(key);
    }

    private void LocalizeApplyButton()
    {
        if (_refreshingMainLocalization && _apply.IsDisposed) return;
        var count = SelectedAutomatableActions().Count;
        var text = count > 0
            ? AppLocalization.T("Main.Button.ApplySelected.Count", count)
            : AppLocalization.T("Main.Button.ApplySelected");
        if (!string.Equals(_apply.Text, text, StringComparison.Ordinal)) _apply.Text = text;
    }

    private static string RecommendationClassText(RecommendationClass value) => value switch
    {
        RecommendationClass.Recommended => AppLocalization.T("Main.Recommendation.Recommended"),
        RecommendationClass.Optional => AppLocalization.T("Main.Recommendation.Optional"),
        _ => AppLocalization.T("Main.Recommendation.Manual")
    };

    private void RefreshRecommendationDisplayRows()
    {
        foreach (DataGridViewRow row in _actions.Rows)
        {
            if (row.Tag is not ActionRecommendation action) continue;
            row.Cells["Kind"].Value = RecommendationClassText(action.RecommendationClass);
            row.Cells["Admin"].Value = action.RequiresAdmin ? AppLocalization.T("Common.Yes") : AppLocalization.T("Common.No");
            var availability = AvailabilityFor(action);
            row.Cells["Availability"].Value = ExecutionPolicy.StateText(availability.State);
            row.Cells["Availability"].ToolTipText = AppLocalization.T("Main.ExecutionContext.ScopeTooltip", availability.Reason, availability.Scope);
        }
    }

    private void RefreshSystemDisplayRows(ScanResult scan)
    {
        var d = scan.Data;
        var labels = new[]
        {
            "Main.System.Computer", "Main.System.SessionUser", "Main.System.ManufacturerModel", null, null, null,
            "Main.System.WindowsVolume", "Main.System.LastBoot", "Main.System.ProcessRights", "Main.System.ProcessAccountSession",
            "Main.System.SessionProfile", "Main.System.ContextLimits", "Main.System.Coverage", "Main.System.MissingSignals",
            "Main.System.PendingReboot", null, "Main.System.Protection", "Main.System.PhysicalDisks", "Main.System.Startup", "Main.System.CollectionWarnings"
        };
        for (var i = 0; i < _system.Rows.Count && i < labels.Length; i++)
            if (labels[i] is { } key) _system.Rows[i].Cells[0].Value = AppLocalization.T(key);

        if (_system.Rows.Count > 6 && string.IsNullOrWhiteSpace(SystemDiskSelection.CurrentDriveId))
            _system.Rows[6].Cells[1].Value = AppLocalization.T("Common.NotDetermined");
        if (_system.Rows.Count > 9 && d.System.ExecutionContext is null)
            _system.Rows[9].Cells[1].Value = ExecutionPolicy.Value(d.System.ExecutionContext?.ProcessAccount) + " / " + AppLocalization.T("Main.System.ContextNotSaved");
        if (_system.Rows.Count > 11 && d.System.ExecutionContext is null)
            _system.Rows[11].Cells[1].Value = AppLocalization.T("Main.System.ContextNotSaved");
        if (_system.Rows.Count > 13)
            _system.Rows[13].Cells[1].Value = scan.Assessment.MissingSignals.Count == 0
                ? AppLocalization.T("Common.No")
                : TechnicalEvidence.Format(scan.Assessment, AppLocalization.Language);
        if (_system.Rows.Count > 14)
            _system.Rows[14].Cells[1].Value = d.PendingReboot.Pending
                ? AppLocalization.T("Common.Yes") + " — " + string.Join("; ", d.PendingReboot.Reasons)
                : AppLocalization.T("Common.No");
        if (_system.Rows.Count > 16 && d.SecurityProducts.Count == 0)
            _system.Rows[16].Cells[1].Value = AppLocalization.T("Common.CouldNotDetermine");
        if (_system.Rows.Count > 17 && d.PhysicalDisks.Count == 0)
            _system.Rows[17].Cells[1].Value = AppLocalization.T("Common.CouldNotDetermine");
        if (_system.Rows.Count > 18)
            _system.Rows[18].Cells[1].Value = AppLocalization.T("Main.System.StartupItems", d.StartupItems.Count);
    }
}
