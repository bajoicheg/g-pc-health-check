namespace G.PcHealthCheck;

internal static class ServiceDeskBatchUi
{
    internal static string BuildDoEverythingConfirmation(BatchPreflight preflight)
    {
        ArgumentNullException.ThrowIfNull(preflight);
        if (preflight.Mode != BatchMode.AllBestEffort)
            throw new InvalidOperationException(AppLocalization.T("ServiceDeskBatch.InvalidMode"));

        var lines = new List<string>
        {
            AppLocalization.T("ServiceDeskBatch.Confirmation.Intro"),
            ""
        };
        foreach (var action in preflight.Actions)
        {
            var state = action.State switch
            {
                PlannedActionState.Run => AppLocalization.T("ServiceDeskBatch.State.Run"),
                PlannedActionState.Superseded => AppLocalization.T("ServiceDeskBatch.State.Superseded"),
                _ => AppLocalization.T("ServiceDeskBatch.State.Skip")
            };
            lines.Add(AppLocalization.T("ServiceDeskBatch.Confirmation.ActionLine", action.Descriptor.Id, state, action.Reason));
        }

        lines.Add("");
        lines.Add(AppLocalization.T("ServiceDeskBatch.Impact.Title"));
        lines.Add(AppLocalization.T(preflight.RequiresUac
            ? "ServiceDeskBatch.Impact.Uac.Required"
            : "ServiceDeskBatch.Impact.Uac.NotRequired"));
        lines.Add(AppLocalization.T(preflight.MayBreakConnectivity
            ? "ServiceDeskBatch.Impact.Network.Break"
            : "ServiceDeskBatch.Impact.Network.None"));
        lines.Add(AppLocalization.T(preflight.MayDeleteUserVisibleState
            ? "ServiceDeskBatch.Impact.State.Delete"
            : "ServiceDeskBatch.Impact.State.None"));
        lines.Add(AppLocalization.T(preflight.MayRequireReboot
            ? "ServiceDeskBatch.Impact.Reboot.Possible"
            : "ServiceDeskBatch.Impact.Reboot.None"));
        lines.Add("");
        lines.Add(AppLocalization.T("ServiceDeskBatch.Confirmation.Question"));
        return string.Join(Environment.NewLine, lines);
    }
}

public sealed partial class MainForm
{
    private bool _doEverythingHandlerAttached;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_doEverythingHandlerAttached) return;
        _doEverythingHandlerAttached = true;
        _doEverything.Click += async (_, _) => await DoEverythingAsync();
    }

    private async Task DoEverythingAsync()
    {
        if (_isBusy || _current is null) return;

        BatchPreflight preflight;
        try
        {
            Busy(true, AppLocalization.T("ServiceDeskBatch.Full.Checking"));
            var context = await Task.Run(ExecutionContextService.Capture);
            RenderExecutionContext(context);
            RefreshActionAvailability();
            preflight = ServiceDeskBatchPlanner.Plan(
                BatchMode.AllBestEffort,
                _current.Actions,
                Array.Empty<string>(),
                id => ExecutionPolicy.For(id, context));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, AppLocalization.T("ServiceDeskBatch.Full.PrepareFailed"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        finally
        {
            Busy(false);
        }

        var confirmation = ServiceDeskBatchUi.BuildDoEverythingConfirmation(preflight);
        if (MessageBox.Show(
                this,
                confirmation,
                AppLocalization.T("ServiceDeskBatch.Full.ConfirmTitle"),
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.OK)
            return;

        var runnable = preflight.Actions
            .Where(x => x.State == PlannedActionState.Run && ServiceDeskActionRegistry.IsExecutableHandler(x.Descriptor.Id))
            .Select(x => x.Descriptor.Id)
            .ToList();
        if (runnable.Count == 0)
        {
            MessageBox.Show(this, AppLocalization.T("ServiceDeskBatch.Full.NoneRunnable"), AppLocalization.T("Main.ServiceDesk.DoEverything"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var before = _current;
        object? activeProgressOwner = null;
        try
        {
            Busy(true, AppLocalization.T("ServiceDeskBatch.Full.Executing"));
            var remediationOwner = new object();
            activeProgressOwner = remediationOwner;
            _applyProgressOwner = remediationOwner;
            var remediationProgress = new Progress<string>(s => ApplyOperationProgress(remediationOwner, s));
            var batch = await RemediationWorker.ExecuteActionIdsAsync(
                runnable,
                _assessment.Thresholds.TempOlderThanDays,
                remediationProgress);

            var verificationOwner = new object();
            activeProgressOwner = verificationOwner;
            _applyProgressOwner = verificationOwner;
            _status.Text = AppLocalization.T("Main.Status.Verifying");
            var verificationProgress = new Progress<string>(s => ApplyOperationProgress(verificationOwner, s));
            var context = await Task.Run(ExecutionContextService.Capture);
            var afterData = await _diagnostics.CollectAsync(verificationProgress);
            if (ReferenceEquals(_applyProgressOwner, verificationOwner)) _applyProgressOwner = null;
            StampExecutionContext(afterData, context);
            var after = _assessment.Assess(afterData);
            var verification = new VerificationResult { Before = before, After = after, Remediation = batch };
            var saved = _reports.SaveVerification(verification);
            _latestReport = saved.Html;
            _current = after;
            Populate(after);
            PopulateVerification(verification);
            _tabs.SelectedIndex = 4;
            var ok = batch.Actions.Count(x => x.Success);
            MessageBox.Show(
                this,
                AppLocalization.T("Main.Message.Completed", ok, batch.Actions.Count, before.Assessment.Score, after.Assessment.Score),
                "G PC Health Check",
                MessageBoxButtons.OK,
                batch.Actions.All(x => x.Success) ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (OperationCanceledException ex)
        {
            _applyProgressOwner = null;
            MessageBox.Show(this, ex.Message, AppLocalization.T("Main.Message.Cancelled"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _applyProgressOwner = null;
            MessageBox.Show(this, ex.Message, AppLocalization.T("ServiceDeskBatch.Full.ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (activeProgressOwner is not null && ReferenceEquals(_applyProgressOwner, activeProgressOwner))
                _applyProgressOwner = null;
            Busy(false);
        }
    }
}
