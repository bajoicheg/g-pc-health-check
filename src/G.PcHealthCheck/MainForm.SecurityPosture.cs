namespace G.PcHealthCheck;

public sealed partial class MainForm
{
    private Panel? _securityMetricCard;
    private TabPage? _securityTabPage;
    private readonly DataGridView _securityGrid = new();
    private readonly Label _securityScore = new();
    private readonly Label _securityMetricDetail = new();
    private readonly Label _securitySummary = new();
    private readonly Label _securityOverrides = new();
    private bool _securityUiInitialized;

    private void InitializeSecurityPostureUi(TableLayoutPanel root)
    {
        if (_securityUiInitialized) return;
        if (root.GetControlFromPosition(0, 2) is not TableLayoutPanel metrics) return;

        _securityMetricDetail.AutoSize = true;
        _securityMetricDetail.ForeColor = Muted;
        _securityMetricDetail.Font = new Font("Segoe UI", 8.5F);
        _securityMetricCard = (Panel)Metric(AppLocalization.T("Security.Metric.Caption"), _securityScore, _securityMetricDetail);
        _securityMetricCard.Name = "SecurityPostureMetricCard";
        _securityMetricCard.Cursor = Cursors.Hand;
        HookSecurityNavigation(_securityMetricCard);

        metrics.ColumnCount = 7;
        metrics.ColumnStyles.Clear();
        for (var i = 0; i < 7; i++) metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 7F));
        metrics.Controls.Add(_securityMetricCard, 6, 0);

        _securityTabPage = BuildSecurityPostureTab();
        _securityTabPage.Name = "SecurityPostureTab";
        _tabs.TabPages.Add(_securityTabPage);

        _securityUiInitialized = true;
        RefreshSecurityLocalization();
        PopulateSecurity(_current);
    }

    private void HookSecurityNavigation(Control control)
    {
        control.Click += (_, _) =>
        {
            if (_securityTabPage is not null) _tabs.SelectedTab = _securityTabPage;
        };
        foreach (Control child in control.Controls)
        {
            child.Cursor = Cursors.Hand;
            HookSecurityNavigation(child);
        }
    }

    private TabPage BuildSecurityPostureTab()
    {
        var page = Page(AppLocalization.T("Security.Tab.Title"));
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = Card();
        header.Margin = new Padding(0, 0, 0, 8);
        _securitySummary.AutoSize = false;
        _securitySummary.Font = new Font("Segoe UI Semibold", 11F);
        _securitySummary.ForeColor = Navy;
        _securitySummary.Location = new Point(14, 12);
        _securitySummary.Size = new Size(1000, 24);
        _securitySummary.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        _securityOverrides.AutoSize = false;
        _securityOverrides.ForeColor = Muted;
        _securityOverrides.Location = new Point(14, 40);
        _securityOverrides.Size = new Size(1000, 22);
        _securityOverrides.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        header.Controls.Add(_securitySummary);
        header.Controls.Add(_securityOverrides);
        header.Resize += (_, _) =>
        {
            _securitySummary.Width = Math.Max(100, header.ClientSize.Width - 28);
            _securityOverrides.Width = Math.Max(100, header.ClientSize.Width - 28);
        };
        layout.Controls.Add(header, 0, 0);

        ConfigureGrid(_securityGrid);
        _securityGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _securityGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Control", HeaderText = AppLocalization.T("Security.Column.Control"), Width = 210 });
        _securityGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = AppLocalization.T("Security.Column.Status"), Width = 115 });
        _securityGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Points", HeaderText = AppLocalization.T("Security.Column.Points"), Width = 78 });
        _securityGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Evidence", HeaderText = AppLocalization.T("Security.Column.Evidence"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 260 });
        _securityGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Recommendation", HeaderText = AppLocalization.T("Security.Column.Recommendation"), Width = 300 });
        _securityGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Source", HeaderText = AppLocalization.T("Security.Column.Source"), Width = 180 });
        foreach (DataGridViewColumn column in _securityGrid.Columns)
            column.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        layout.Controls.Add(_securityGrid, 0, 1);

        page.Controls.Add(layout);
        return page;
    }

    private void PopulateSecurity(ScanResult? scan)
    {
        if (!_securityUiInitialized || _securityMetricCard is null || _securityTabPage is null) return;

        _securityGrid.Rows.Clear();
        if (scan?.Security is not { } assessment)
        {
            _securityScore.Text = "—";
            _securityScore.ForeColor = Muted;
            _securityMetricDetail.Text = AppLocalization.T("Security.Summary.Empty");
            _securityMetricDetail.ForeColor = Muted;
            _securitySummary.Text = AppLocalization.T("Security.Summary.Empty");
            _securityOverrides.Text = "";
            return;
        }

        _securityScore.Text = assessment.Score?.ToString() ?? "—";
        _securityScore.ForeColor = SecurityBandColor(assessment.DisplayBand);
        _securityMetricDetail.Text = AppLocalization.T(
            "Security.Metric.BandCoverage",
            SecurityBandText(assessment.DisplayBand),
            assessment.CoveragePercent);
        _securityMetricDetail.ForeColor = SecurityBandColor(assessment.DisplayBand);
        _securitySummary.Text = AppLocalization.T(
            "Security.Summary.Value",
            assessment.Score?.ToString() ?? "—",
            SecurityBandText(assessment.DisplayBand),
            assessment.CoveragePercent,
            assessment.ModelVersion);
        _securityOverrides.Text = assessment.CriticalOverrides.Count == 0
            ? ""
            : AppLocalization.T("Security.Summary.Overrides", string.Join(", ", assessment.CriticalOverrides));

        foreach (var control in assessment.Controls)
            AddSecurityControlRow(control);
        foreach (var supplemental in assessment.Supplemental)
            AddSecurityControlRow(supplemental);

        AddBitLockerDetailRows(assessment);
        AddLocalAdministratorDetailRows(scan.SecuritySnapshot?.LocalAdministrators);
    }

    private void AddSecurityControlRow(SecurityControlResult control)
    {
        var evidence = control.Evidence.Count == 0
            ? AppLocalization.T("Security.Evidence.None")
            : string.Join("; ", control.Evidence.Select(x => $"{x.Key}={x.Value}"));
        var sources = DistinctSources(control.Evidence);
        var recommendation = control.Status is SecurityControlStatus.Pass or SecurityControlStatus.NotApplicable
            ? "—"
            : SecurityGuidance(control.GuidanceCode);
        var index = _securityGrid.Rows.Add(
            control.Id,
            SecurityStatusText(control.Status),
            SecurityPointsText(control),
            evidence,
            recommendation,
            sources);
        StyleSecurityRow(_securityGrid.Rows[index], control.Status);
    }

    private void AddBitLockerDetailRows(SecurityPostureAssessment assessment)
    {
        foreach (var control in assessment.Controls.Where(x => x.Id is "SEC-BITLOCKER-OS" or "SEC-BITLOCKER-DATA"))
        {
            var groups = SplitEvidenceGroups(control.Evidence, "Volume");
            foreach (var group in groups)
            {
                var volume = group.FirstOrDefault(x => x.Key == "Volume")?.Value;
                if (string.IsNullOrWhiteSpace(volume)) continue;
                var status = SecurityStatusFromBitLockerEvidence(group);
                var evidence = string.Join("; ", group.Select(x => $"{x.Key}={x.Value}"));
                var index = _securityGrid.Rows.Add(
                    control.Id + " · " + volume,
                    SecurityStatusText(status),
                    "—",
                    evidence,
                    status is SecurityControlStatus.Pass or SecurityControlStatus.NotApplicable ? "—" : SecurityGuidance(control.GuidanceCode),
                    DistinctSources(group));
                StyleSecurityRow(_securityGrid.Rows[index], status);
            }
        }
    }

    private void AddLocalAdministratorDetailRows(LocalAdministratorsCollectionResult? localAdministrators)
    {
        if (localAdministrators is null) return;
        foreach (var member in localAdministrators.Members)
        {
            var status = member.Result switch
            {
                "Allowed" => SecurityControlStatus.Pass,
                "Unauthorized" => SecurityControlStatus.Fail,
                _ => SecurityControlStatus.Unknown
            };
            var identity = member.Name ?? (member.Sid is null ? "Unknown" : "SID:" + member.Sid);
            var facts = new List<string>();
            if (member.Sid is not null) facts.Add("SID=" + member.Sid);
            facts.Add("Type=" + member.Type);
            facts.Add("Result=" + member.Result);
            facts.Add("Reason=" + member.Reason);
            if (member.MatchedRule is not null) facts.Add("MatchedRule=" + member.MatchedRule);
            var index = _securityGrid.Rows.Add(
                "SEC-LOCAL-ADMINS · " + identity,
                SecurityStatusText(status),
                "—",
                string.Join("; ", facts),
                status == SecurityControlStatus.Pass ? "—" : SecurityGuidance("SEC-LOCAL-ADMINS"),
                member.Source);
            StyleSecurityRow(_securityGrid.Rows[index], status);
        }
    }

    private static IReadOnlyList<IReadOnlyList<SecurityEvidence>> SplitEvidenceGroups(
        IReadOnlyList<SecurityEvidence> evidence,
        string firstKey)
    {
        var result = new List<IReadOnlyList<SecurityEvidence>>();
        List<SecurityEvidence>? current = null;
        foreach (var item in evidence)
        {
            if (item.Key == firstKey)
            {
                if (current is { Count: > 0 }) result.Add(current);
                current = [];
            }
            current?.Add(item);
        }
        if (current is { Count: > 0 }) result.Add(current);
        return result;
    }

    private static SecurityControlStatus SecurityStatusFromBitLockerEvidence(IReadOnlyList<SecurityEvidence> evidence)
    {
        var protection = evidence.FirstOrDefault(x => x.Key == "ProtectionStatus")?.Value;
        var conversion = evidence.FirstOrDefault(x => x.Key == "ConversionStatus")?.Value;
        if (protection is null || conversion is null) return SecurityControlStatus.Unknown;
        if (conversion.Equals("FullyDecrypted", StringComparison.OrdinalIgnoreCase)) return SecurityControlStatus.Fail;
        if (conversion is "EncryptionInProgress" or "EncryptionPaused" or "DecryptionInProgress" or "DecryptionPaused") return SecurityControlStatus.Warn;
        if (conversion.Equals("FullyEncrypted", StringComparison.OrdinalIgnoreCase))
        {
            if (protection.Equals("On", StringComparison.OrdinalIgnoreCase)) return SecurityControlStatus.Pass;
            if (protection.Equals("Off", StringComparison.OrdinalIgnoreCase)) return SecurityControlStatus.Warn;
        }
        return SecurityControlStatus.Unknown;
    }

    private static string DistinctSources(IEnumerable<SecurityEvidence> evidence)
        => string.Join(", ", evidence.Select(x => x.Source).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase));

    private static string SecurityGuidance(string guidanceCode)
    {
        var key = "Security.Guidance." + guidanceCode;
        var localized = AppLocalization.T(key);
        return localized == key ? guidanceCode : localized;
    }

    private static string SecurityStatusText(SecurityControlStatus status) => status switch
    {
        SecurityControlStatus.Pass => AppLocalization.T("Security.Status.Pass"),
        SecurityControlStatus.Warn => AppLocalization.T("Security.Status.Warn"),
        SecurityControlStatus.Fail => AppLocalization.T("Security.Status.Fail"),
        SecurityControlStatus.NotApplicable => AppLocalization.T("Security.Status.NotApplicable"),
        _ => AppLocalization.T("Security.Status.Unknown")
    };

    private static string SecurityBandText(SecurityBand band) => band switch
    {
        SecurityBand.High => AppLocalization.T("Security.Band.High"),
        SecurityBand.Good => AppLocalization.T("Security.Band.Good"),
        SecurityBand.NeedsAttention => AppLocalization.T("Security.Band.NeedsAttention"),
        SecurityBand.Low => AppLocalization.T("Security.Band.Low"),
        _ => AppLocalization.T("Security.Band.AssessmentIncomplete")
    };

    private static Color SecurityBandColor(SecurityBand band) => band switch
    {
        SecurityBand.High => Color.FromArgb(23, 122, 75),
        SecurityBand.Good => Color.FromArgb(35, 134, 192),
        SecurityBand.NeedsAttention => Color.FromArgb(165, 106, 0),
        SecurityBand.Low => Color.FromArgb(181, 54, 54),
        _ => Color.FromArgb(165, 106, 0)
    };

    private static string SecurityPointsText(SecurityControlResult result)
    {
        if (result.Status is SecurityControlStatus.Unknown or SecurityControlStatus.NotApplicable) return "—";
        var earned = result.Weight * result.EarnedFraction;
        return $"{earned:0.#}/{result.Weight}";
    }

    private static Color SecurityStatusColor(SecurityControlStatus status) => status switch
    {
        SecurityControlStatus.Pass => Color.FromArgb(23, 122, 75),
        SecurityControlStatus.Warn => Color.FromArgb(165, 106, 0),
        SecurityControlStatus.Fail => Color.FromArgb(181, 54, 54),
        _ => Color.FromArgb(104, 122, 139)
    };

    private void StyleSecurityRow(DataGridViewRow row, SecurityControlStatus status)
    {
        row.Cells["Status"].Style.ForeColor = SecurityStatusColor(status);
        row.Cells["Status"].Style.Font = new Font(Font, FontStyle.Bold);
        if (status == SecurityControlStatus.Fail)
            row.DefaultCellStyle.BackColor = Color.FromArgb(255, 242, 242);
        else if (status == SecurityControlStatus.Warn)
            row.DefaultCellStyle.BackColor = Color.FromArgb(255, 249, 235);
    }

    private void RefreshSecurityLocalization()
    {
        if (!_securityUiInitialized) return;
        if (_securityMetricCard?.Controls.Count >= 1 && _securityMetricCard.Controls[0] is Label caption)
            caption.Text = AppLocalization.T("Security.Metric.Caption");
        if (_securityTabPage is not null) _securityTabPage.Text = AppLocalization.T("Security.Tab.Title");
        SetSecurityColumn("Control", "Security.Column.Control");
        SetSecurityColumn("Status", "Security.Column.Status");
        SetSecurityColumn("Points", "Security.Column.Points");
        SetSecurityColumn("Evidence", "Security.Column.Evidence");
        SetSecurityColumn("Recommendation", "Security.Column.Recommendation");
        SetSecurityColumn("Source", "Security.Column.Source");
        PopulateSecurity(_current);
    }

    private void SetSecurityColumn(string name, string key)
    {
        if (_securityGrid.Columns.Contains(name)) _securityGrid.Columns[name].HeaderText = AppLocalization.T(key);
    }
}
