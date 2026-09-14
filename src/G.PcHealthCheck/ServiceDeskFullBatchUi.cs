namespace G.PcHealthCheck;

internal static class ServiceDeskBatchUi
{
    internal static string BuildDoEverythingConfirmation(BatchPreflight preflight)
    {
        ArgumentNullException.ThrowIfNull(preflight);
        if (preflight.Mode != BatchMode.AllBestEffort)
            throw new InvalidOperationException("Полное подтверждение допустимо только для режима DoEverything.");

        var lines = new List<string>
        {
            "Будет запрошен полный фиксированный набор Service Desk действий:",
            ""
        };
        foreach (var action in preflight.Actions)
        {
            var state = action.State switch
            {
                PlannedActionState.Run => "выполнить",
                PlannedActionState.Superseded => "заменено эквивалентным действием",
                _ => "пропустить"
            };
            lines.Add($"• {action.Descriptor.Id} — {state}: {action.Reason}");
        }

        lines.Add("");
        lines.Add("Важные последствия полного запуска:");
        lines.Add(preflight.RequiresUac
            ? "• Будет один запрос UAC для единого повышенного worker-процесса."
            : "• Дополнительный UAC не требуется в текущем контексте.");
        lines.Add(preflight.MayBreakConnectivity
            ? "• Сеть будет временно прервана на поздней фазе; VPN/RDP/другие соединения могут оборваться."
            : "• Действия не заявляют разрыв сети.");
        lines.Add(preflight.MayDeleteUserVisibleState
            ? "• Некоторые действия удаляют пользовательское состояние: старые Temp-файлы и/или задания очереди печати."
            : "• Удаление пользовательского состояния не заявлено.");
        lines.Add(preflight.MayRequireReboot
            ? "• После Winsock/TCP-IP reset может потребоваться перезагрузка; программа сама её не выполняет."
            : "• Перезагрузка не требуется по метаданным выбранного набора.");
        lines.Add("");
        lines.Add("Продолжить полный запуск?");
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
            Busy(true, "Проверяю полный фиксированный набор действий…");
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
            MessageBox.Show(this, ex.Message, "Не удалось подготовить полный набор", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                "Сделать всё — подтвердите полный набор",
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
            MessageBox.Show(this, "Сейчас ни одно действие полного набора не доступно для запуска.", "Сделать всё", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var before = _current;
        object? activeProgressOwner = null;
        try
        {
            Busy(true, "Выполняю полный фиксированный набор…");
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
            _status.Text = "Повторная диагностика после полного remediation…";
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
                $"Выполнено: {ok}/{batch.Actions.Count}. Повторная диагностика завершена.\nИндекс: {before.Assessment.Score} → {after.Assessment.Score}.\nУстранение симптома нужно подтвердить отдельно.",
                "G PC Health Check",
                MessageBoxButtons.OK,
                batch.Actions.All(x => x.Success) ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (OperationCanceledException ex)
        {
            _applyProgressOwner = null;
            MessageBox.Show(this, ex.Message, "Операция отменена", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _applyProgressOwner = null;
            MessageBox.Show(this, ex.Message, "Ошибка полного remediation", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (activeProgressOwner is not null && ReferenceEquals(_applyProgressOwner, activeProgressOwner))
                _applyProgressOwner = null;
            Busy(false);
        }
    }
}
