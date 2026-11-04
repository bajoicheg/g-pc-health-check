namespace G.PcHealthCheck;

public sealed partial class MainForm
{
    private readonly Button _hardenSecurity = BatchButton(
        "HardenSecurity",
        AppLocalization.T("Security.Hardening.Button"),
        Color.FromArgb(35, 134, 192));
    private readonly ToolTip _securityHardeningToolTip = new();

    private async Task HardenSecurityAsync()
    {
        if (_isBusy || _current is null) return;
        var runtime = new WindowsSecurityHardeningUiRuntime();
        SecurityHardeningPreflight preflight;
        var preflightOwner = new object();
        _applyProgressOwner = preflightOwner;
        try
        {
            Busy(true, AppLocalization.T("Security.Hardening.Checking"));
            var progress = new Progress<string>(s => ApplyOperationProgress(preflightOwner, s));
            preflight = await SecurityHardeningWorkflow.PreflightAsync(
                _current.Data.System,
                runtime,
                CancellationToken.None,
                progress);
            RenderExecutionContext(preflight.Context);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                AppLocalization.T("Security.Hardening.Failed") + Environment.NewLine + Environment.NewLine + ex.Message,
                AppLocalization.T("Security.Hardening.Title"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }
        finally
        {
            if (ReferenceEquals(_applyProgressOwner, preflightOwner)) _applyProgressOwner = null;
            Busy(false);
            RefreshSecurityHardeningButtonState();
        }

        if (!preflight.Plan.HasRunnableActions)
        {
            MessageBox.Show(
                this,
                AppLocalization.T("Security.Hardening.NoRunnable", preflight.Plan.NoRunnableReasonCode),
                AppLocalization.T("Security.Hardening.Title"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var confirmation = SecurityHardeningPresentation.BuildConfirmation(preflight.Plan, AppLocalization.Language);
        if (MessageBox.Show(
                this,
                confirmation,
                AppLocalization.T("Security.Hardening.Title"),
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning) != DialogResult.OK)
            return;

        var executionOwner = new object();
        _applyProgressOwner = executionOwner;
        try
        {
            Busy(true, AppLocalization.T("Security.Hardening.Executing"));
            var progress = new Progress<string>(s => ApplyOperationProgress(executionOwner, s));
            var verification = await SecurityHardeningWorkflow.ExecuteConfirmedAsync(
                preflight,
                _current.Data.System,
                runtime,
                CancellationToken.None,
                progress);

            _current.SecurityHardening = SecurityHardeningEvidence.From(verification);
            _current.Security = verification.After.Assessment;
            _current.SecuritySnapshot = verification.After.Snapshot;
            PopulateSecurity(_current);
            RefreshSecurityHardeningButtonState();
            if (_securityTabPage is not null) _tabs.SelectedTab = _securityTabPage;

            var saved = _reports.SaveScan(_current);
            _latestReport = saved.Html;
            MessageBox.Show(
                this,
                SecurityHardeningPresentation.BuildResult(verification, AppLocalization.Language),
                AppLocalization.T("Security.Hardening.Result.Title"),
                MessageBoxButtons.OK,
                verification.Batch.Actions.All(x => x.Success) ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (OperationCanceledException)
        {
            MessageBox.Show(
                this,
                AppLocalization.T("Security.Hardening.Cancelled"),
                AppLocalization.T("Security.Hardening.Title"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                AppLocalization.T("Security.Hardening.Failed") + Environment.NewLine + Environment.NewLine + ex.Message,
                AppLocalization.T("Security.Hardening.Title"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            if (ReferenceEquals(_applyProgressOwner, executionOwner)) _applyProgressOwner = null;
            Busy(false);
            RefreshSecurityHardeningButtonState();
        }
    }

    private void RefreshSecurityHardeningButtonState()
    {
        if (!_serviceDeskActionsUiInitialized || _hardenSecurity.IsDisposed) return;
        _hardenSecurity.Text = AppLocalization.T("Security.Hardening.Button");
        if (_isBusy || _current?.SecuritySnapshot is not { } snapshot || _current.Data.System.ExecutionContext is not { } context)
        {
            _hardenSecurity.Enabled = false;
            _hardenSecurity.Cursor = Cursors.Default;
            _securityHardeningToolTip.SetToolTip(
                _hardenSecurity,
                AppLocalization.T("Security.Hardening.UnavailableHint", "SecurityPostureUnavailable"));
            return;
        }

        SecurityHardeningPlan plan;
        try { plan = SecurityHardeningPlanner.Plan(snapshot, context); }
        catch
        {
            _hardenSecurity.Enabled = false;
            _hardenSecurity.Cursor = Cursors.Default;
            _securityHardeningToolTip.SetToolTip(
                _hardenSecurity,
                AppLocalization.T("Security.Hardening.UnavailableHint", "PreflightUnavailable"));
            return;
        }

        _hardenSecurity.Enabled = plan.HasRunnableActions;
        _hardenSecurity.Cursor = plan.HasRunnableActions ? Cursors.Hand : Cursors.Default;
        _securityHardeningToolTip.SetToolTip(
            _hardenSecurity,
            plan.HasRunnableActions
                ? AppLocalization.T("Security.Hardening.ReadyHint")
                : AppLocalization.T("Security.Hardening.UnavailableHint", plan.NoRunnableReasonCode));
    }
}
