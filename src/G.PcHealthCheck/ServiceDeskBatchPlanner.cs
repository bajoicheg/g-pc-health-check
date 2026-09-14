namespace G.PcHealthCheck;

internal static class ServiceDeskBatchPlanner
{
    internal static BatchPreflight Plan(
        BatchMode mode,
        IReadOnlyCollection<ActionRecommendation> recommendations,
        IReadOnlyCollection<string> selectedIds,
        Func<string, ActionAvailability> availabilityFor)
    {
        ArgumentNullException.ThrowIfNull(recommendations);
        ArgumentNullException.ThrowIfNull(selectedIds);
        ArgumentNullException.ThrowIfNull(availabilityFor);

        var recommendationsById = recommendations
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var requested = mode switch
        {
            BatchMode.RecommendedBestEffort => recommendations
                .Where(x => x.CanAutomate && x.RecommendationClass == RecommendationClass.Recommended)
                .Select(x => ServiceDeskActionRegistry.Find(x.Id))
                .Where(x => x is not null)
                .Cast<ServiceDeskActionDescriptor>()
                .DistinctBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            BatchMode.AllBestEffort => ServiceDeskActionRegistry.All.ToList(),
            BatchMode.SelectedStrict => ResolveSelectedStrict(recommendationsById, selectedIds),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

        var planned = new List<ActionPreflight>();
        foreach (var descriptor in requested.OrderBy(x => x.PhaseOrder))
        {
            recommendationsById.TryGetValue(descriptor.Id, out var recommendation);
            var availability = availabilityFor(descriptor.Id);
            if (mode == BatchMode.SelectedStrict && !availability.CanRequest)
                throw new InvalidOperationException($"Доступность действия {descriptor.Id} изменилась: {availability.Reason}");

            var state = availability.CanRequest ? PlannedActionState.Run : PlannedActionState.Skipped;
            planned.Add(new(descriptor, recommendation, availability, state,
                state == PlannedActionState.Run ? "Готово к выполнению." : availability.Reason));
        }

        Coalesce(planned);
        var effective = planned.Where(x => x.State == PlannedActionState.Run).ToList();
        var highest = effective.Count == 0 ? ActionRisk.Low : effective.Max(x => x.Descriptor.Risk);
        return new BatchPreflight(
            mode,
            planned,
            highest,
            effective.Any(x => string.Equals(x.Availability.State, "NeedsUac", StringComparison.Ordinal)),
            effective.Any(x => x.Descriptor.MayBreakConnectivity),
            effective.Any(x => x.Descriptor.MayDeleteUserVisibleState),
            effective.Any(x => x.Descriptor.MayRequireReboot));
    }

    internal static async Task<bool> ExecuteAfterConfirmationAsync(
        BatchPreflight preflight,
        Func<bool> confirm,
        Func<Task> execute)
    {
        ArgumentNullException.ThrowIfNull(preflight);
        ArgumentNullException.ThrowIfNull(confirm);
        ArgumentNullException.ThrowIfNull(execute);
        if (!confirm()) return false;
        await execute();
        return true;
    }

    private static List<ServiceDeskActionDescriptor> ResolveSelectedStrict(
        IReadOnlyDictionary<string, ActionRecommendation> recommendations,
        IReadOnlyCollection<string> selectedIds)
    {
        var result = new List<ServiceDeskActionDescriptor>();
        foreach (var id in selectedIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!recommendations.TryGetValue(id, out var recommendation) || !recommendation.CanAutomate)
                throw new InvalidOperationException($"Выбранное действие {id} больше не является автоматизируемой рекомендацией.");
            var descriptor = ServiceDeskActionRegistry.Find(id)
                ?? throw new InvalidOperationException($"Действие {id} отсутствует в разрешённом реестре.");
            result.Add(descriptor);
        }
        if (result.Count == 0) throw new InvalidOperationException("Не выбрано ни одного автоматизируемого действия.");
        return result;
    }

    private static void Coalesce(List<ActionPreflight> actions)
    {
        var clear = actions.FirstOrDefault(x =>
            x.State == PlannedActionState.Run && x.Descriptor.Id.Equals("ClearPrintQueue", StringComparison.OrdinalIgnoreCase));
        if (clear is null) return;
        var index = actions.FindIndex(x =>
            x.State == PlannedActionState.Run && x.Descriptor.Id.Equals("RestartSpooler", StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;
        var existing = actions[index];
        actions[index] = existing with
        {
            State = PlannedActionState.Superseded,
            Reason = "Отдельный RestartSpooler не нужен: ClearPrintQueue включает контролируемую перезагрузку Spooler."
        };
    }
}
