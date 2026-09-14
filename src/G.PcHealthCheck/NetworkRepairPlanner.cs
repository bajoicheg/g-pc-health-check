namespace G.PcHealthCheck;

internal sealed class NetworkAdapterSnapshot
{
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Physical { get; set; }
    public bool Enabled { get; set; }
    public bool Connected { get; set; }
    public bool DhcpEnabled { get; set; }
}

internal sealed class NetworkAdapterRepairEvidence
{
    public string DeviceId { get; init; } = "";
    public string Name { get; init; } = "";
    public bool Disabled { get; init; }
    public bool Reenabled { get; init; }
    public bool Success { get; init; }
    public string Message { get; init; } = "";
}

internal static class NetworkRepairPlanner
{
    internal static IReadOnlyList<NetworkAdapterSnapshot> EligibleForRestart(IReadOnlyList<NetworkAdapterSnapshot> inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        return inventory
            .Where(x => x.Physical && x.Enabled && x.Connected && !string.IsNullOrWhiteSpace(x.DeviceId))
            .ToList();
    }

    internal static IReadOnlyList<NetworkAdapterSnapshot> EligibleForDhcp(IReadOnlyList<NetworkAdapterSnapshot> inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        return inventory
            .Where(x => x.Enabled && x.Connected && x.DhcpEnabled && !string.IsNullOrWhiteSpace(x.DeviceId))
            .ToList();
    }

    internal static IReadOnlyList<NetworkAdapterRepairEvidence> RestartEligibleAdapters(
        IReadOnlyList<NetworkAdapterSnapshot> inventory,
        Func<string, bool, bool> setEnabled,
        Action<string> afterDisable)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(setEnabled);
        ArgumentNullException.ThrowIfNull(afterDisable);
        var evidence = new List<NetworkAdapterRepairEvidence>();
        foreach (var adapter in EligibleForRestart(inventory))
        {
            var disableAttempted = false;
            var disabled = false;
            var reenabled = false;
            string? error = null;
            try
            {
                disableAttempted = true;
                disabled = setEnabled(adapter.DeviceId, false);
                if (!disabled)
                {
                    error = "Windows не подтвердил отключение адаптера.";
                    continue;
                }
                afterDisable(adapter.DeviceId);
            }
            catch (Exception ex)
            {
                error = $"После отключения: {ex.GetType().Name}, 0x{ex.HResult:X8}.";
            }
            finally
            {
                // A disable API can partially change state before reporting a failure.
                // Once disable was attempted, always make a best-effort recovery call.
                if (disableAttempted)
                {
                    try { reenabled = setEnabled(adapter.DeviceId, true); }
                    catch (Exception ex)
                    {
                        reenabled = false;
                        error = string.IsNullOrWhiteSpace(error)
                            ? $"Повторное включение: {ex.GetType().Name}, 0x{ex.HResult:X8}."
                            : error + $" Повторное включение: {ex.GetType().Name}, 0x{ex.HResult:X8}.";
                    }
                }
                evidence.Add(new NetworkAdapterRepairEvidence
                {
                    DeviceId = adapter.DeviceId,
                    Name = adapter.Name,
                    Disabled = disabled,
                    Reenabled = reenabled,
                    Success = disabled && reenabled && string.IsNullOrWhiteSpace(error),
                    Message = string.IsNullOrWhiteSpace(error)
                        ? "Адаптер отключён и повторно включён."
                        : error
                });
            }
        }
        return evidence;
    }
}

internal static class ServiceDeskNetworkHandlers
{
    private static readonly HashSet<string> Ids = new(
        ["WinsockReset", "TcpIpReset", "RestartNetworkAdapters", "DhcpReleaseRenew", "RegisterDns"],
        StringComparer.OrdinalIgnoreCase);

    internal static bool CanHandle(string id) => Ids.Contains(id);

    internal static RemediationActionResult Execute(string id, IWindowsRepairOperations operations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(operations);
        return id.ToLowerInvariant() switch
        {
            "winsockreset" => FromNative("WinsockReset", operations.RunFixedCommand(FixedCommand.WinsockReset, TimeSpan.FromMinutes(2))),
            "tcpipreset" => FromNative("TcpIpReset", operations.RunFixedCommand(FixedCommand.TcpIpReset, TimeSpan.FromMinutes(2))),
            "restartnetworkadapters" => RestartAdapters(operations),
            "dhcpreleaserenew" => RenewDhcp(operations),
            "registerdns" => FromNative("RegisterDns", operations.RunFixedCommand(FixedCommand.RegisterDns, TimeSpan.FromMinutes(2))),
            _ => new RemediationActionResult { Id = id, Success = false, Message = "Действие отсутствует в фиксированном network handler set." }
        };
    }

    private static RemediationActionResult RestartAdapters(IWindowsRepairOperations operations)
    {
        IReadOnlyList<NetworkAdapterSnapshot> inventory;
        try { inventory = operations.QueryNetworkAdapters(); }
        catch (Exception ex)
        {
            return new RemediationActionResult { Id = "RestartNetworkAdapters", Success = false, Message = $"Инвентаризация адаптеров: {ex.GetType().Name}, 0x{ex.HResult:X8}." };
        }
        var operationMessages = new List<string>();
        var evidence = NetworkRepairPlanner.RestartEligibleAdapters(
            inventory,
            (deviceId, enabled) =>
            {
                var result = operations.SetAdapterEnabled(deviceId, enabled);
                operationMessages.Add($"{deviceId}:{(enabled ? "enable" : "disable")}={result.Success} ({result.Message})");
                return result.Success;
            },
            _ => { });
        var success = evidence.All(x => x.Success);
        var names = evidence.Count == 0 ? "подходящих адаптеров нет" : string.Join("; ", evidence.Select(x => $"{x.Name} [{x.DeviceId}]: {x.Message}"));
        return new RemediationActionResult
        {
            Id = "RestartNetworkAdapters",
            Success = success,
            Message = "Перезапуск физических подключённых адаптеров: " + names,
            Output = string.Join(Environment.NewLine, operationMessages)
        };
    }

    private static RemediationActionResult RenewDhcp(IWindowsRepairOperations operations)
    {
        IReadOnlyList<NetworkAdapterSnapshot> inventory;
        try { inventory = operations.QueryNetworkAdapters(); }
        catch (Exception ex)
        {
            return new RemediationActionResult { Id = "DhcpReleaseRenew", Success = false, Message = $"Инвентаризация адаптеров: {ex.GetType().Name}, 0x{ex.HResult:X8}." };
        }
        var targets = NetworkRepairPlanner.EligibleForDhcp(inventory);
        var results = new List<NativeActionResult>();
        foreach (var adapter in targets)
        {
            try { results.Add(operations.CycleDhcpLease(adapter.DeviceId)); }
            catch (Exception ex)
            {
                results.Add(new NativeActionResult { Id = adapter.DeviceId, Success = false, Message = $"DHCP: {ex.GetType().Name}, 0x{ex.HResult:X8}." });
            }
        }
        var success = results.All(x => x.Success);
        var message = results.Count == 0
            ? "Подключённых DHCP-enabled адаптеров для release/renew нет."
            : string.Join("; ", results.Select(x => $"{x.Id}: {x.Message}"));
        return new RemediationActionResult
        {
            Id = "DhcpReleaseRenew",
            Success = success,
            Message = message,
            Output = string.Join(Environment.NewLine, results.Select(x => $"{x.Id}: exit={x.ExitCode?.ToString() ?? "—"}; success={x.Success}"))
        };
    }

    private static RemediationActionResult FromNative(string id, NativeActionResult result)
        => new()
        {
            Id = id,
            Success = result.Success,
            ExitCode = result.ExitCode,
            Message = result.Message,
            Output = result.Output,
            RebootRecommended = result.RebootRecommended
        };
}
