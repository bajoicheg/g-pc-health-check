using System.Diagnostics;
using System.Management;
using System.ServiceProcess;

namespace G.PcHealthCheck;

internal enum FixedCommand
{
    FlushDns,
    TimeResync,
    GpUpdateComputer,
    Dism,
    Sfc,
    WinsockReset,
    TcpIpReset,
    RegisterDns
}

internal enum FixedService
{
    Spooler,
    WindowsUpdate,
    Bits
}

internal sealed class FixedCommandSpec
{
    public string Id { get; init; } = "";
    public string FileName { get; init; } = "";
    public string Arguments { get; init; } = "";
    public TimeSpan Timeout { get; init; }
    public bool RebootRecommended { get; init; }
}

internal sealed class NativeActionResult
{
    public string Id { get; set; } = "";
    public bool Success { get; set; }
    public int? ExitCode { get; set; }
    public string Message { get; set; } = "";
    public string Output { get; set; } = "";
    public bool RebootRecommended { get; set; }
}

internal sealed class ServiceStateSnapshot
{
    public FixedService Service { get; set; }
    public string State { get; set; } = "Unknown";
    public string StartMode { get; set; } = "Unknown";
}

internal sealed class PrintQueueClearResult
{
    public bool Success { get; set; }
    public int DeletedFiles { get; set; }
    public int SkippedFiles { get; set; }
    public int Errors { get; set; }
    public bool SpoolerRestarted { get; set; }
    public string Message { get; set; } = "";
}

internal interface IWindowsRepairOperations
{
    NativeActionResult RunFixedCommand(FixedCommand command, TimeSpan timeout);
    ServiceStateSnapshot QueryService(FixedService service);
    NativeActionResult RestartService(FixedService service, bool startIfStopped);
    PrintQueueClearResult ClearPrintQueue();
    IReadOnlyList<NetworkAdapterSnapshot> QueryNetworkAdapters();
    NativeActionResult SetAdapterEnabled(string stableDeviceId, bool enabled);
    NativeActionResult CycleDhcpLease(string stableDeviceId);
}

internal sealed class WindowsRepairOperations : IWindowsRepairOperations
{
    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(30);
    private const int MaxOutputChars = 12000;

    internal static FixedCommandSpec GetCommandSpec(FixedCommand command)
        => command switch
        {
            FixedCommand.FlushDns => Spec("FlushDns", "ipconfig.exe", "/flushdns", TimeSpan.FromMinutes(2)),
            FixedCommand.TimeResync => Spec("TimeResync", "w32tm.exe", "/resync /rediscover", TimeSpan.FromMinutes(2)),
            FixedCommand.GpUpdateComputer => Spec("GpUpdate", "gpupdate.exe", "/target:computer /force /wait:60", TimeSpan.FromMinutes(3)),
            FixedCommand.Dism => Spec("Dism", "dism.exe", "/Online /Cleanup-Image /RestoreHealth", TimeSpan.FromMinutes(60)),
            FixedCommand.Sfc => Spec("Sfc", "sfc.exe", "/scannow", TimeSpan.FromMinutes(60)),
            FixedCommand.WinsockReset => Spec("WinsockReset", "netsh.exe", "winsock reset", TimeSpan.FromMinutes(2), reboot: true),
            FixedCommand.TcpIpReset => Spec("TcpIpReset", "netsh.exe", "int ip reset", TimeSpan.FromMinutes(2), reboot: true),
            FixedCommand.RegisterDns => Spec("RegisterDns", "ipconfig.exe", "/registerdns", TimeSpan.FromMinutes(2)),
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };

    private static FixedCommandSpec Spec(string id, string exe, string arguments, TimeSpan timeout, bool reboot = false)
        => new()
        {
            Id = id,
            FileName = Path.Combine(Environment.SystemDirectory, exe),
            Arguments = arguments,
            Timeout = timeout,
            RebootRecommended = reboot
        };

    public NativeActionResult RunFixedCommand(FixedCommand command, TimeSpan timeout)
    {
        var spec = GetCommandSpec(command);
        var effectiveTimeout = timeout <= TimeSpan.Zero || timeout > spec.Timeout ? spec.Timeout : timeout;
        var psi = new ProcessStartInfo
        {
            FileName = spec.FileName,
            Arguments = spec.Arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Не удалось запустить {spec.FileName}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit((int)Math.Min(int.MaxValue, effectiveTimeout.TotalMilliseconds)))
        {
            TryKill(process);
            return new NativeActionResult
            {
                Id = spec.Id,
                Success = false,
                Message = $"Превышено время выполнения {effectiveTimeout.TotalMinutes:0.#} мин.",
                RebootRecommended = spec.RebootRecommended
            };
        }
        Task.WaitAll(stdout, stderr);
        var output = (stdout.Result + Environment.NewLine + stderr.Result).Trim();
        if (output.Length > MaxOutputChars) output = output[^MaxOutputChars..];
        return new NativeActionResult
        {
            Id = spec.Id,
            Success = process.ExitCode == 0,
            ExitCode = process.ExitCode,
            Message = process.ExitCode == 0 ? "Команда завершена успешно." : $"Команда завершена с кодом {process.ExitCode}.",
            Output = output,
            RebootRecommended = spec.RebootRecommended
        };
    }

    public ServiceStateSnapshot QueryService(FixedService service)
    {
        using var controller = new ServiceController(ServiceName(service));
        controller.Refresh();
        return new ServiceStateSnapshot
        {
            Service = service,
            State = controller.Status.ToString(),
            StartMode = controller.StartType.ToString()
        };
    }

    public NativeActionResult RestartService(FixedService service, bool startIfStopped)
    {
        var snapshot = QueryService(service);
        if (snapshot.StartMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
            return new NativeActionResult { Id = service.ToString(), Success = true, Message = "Служба Disabled; startup type не изменялся, действие пропущено." };

        using var controller = new ServiceController(ServiceName(service));
        controller.Refresh();
        try
        {
            if (controller.Status != ServiceControllerStatus.Stopped)
            {
                if (!controller.CanStop)
                    return new NativeActionResult { Id = service.ToString(), Success = false, Message = "Служба не разрешает остановку; startup type не изменялся." };
                controller.Stop();
                controller.WaitForStatus(ServiceControllerStatus.Stopped, ServiceTimeout);
            }
            if (startIfStopped)
            {
                controller.Refresh();
                if (controller.Status == ServiceControllerStatus.Stopped) controller.Start();
                controller.WaitForStatus(ServiceControllerStatus.Running, ServiceTimeout);
            }
            return new NativeActionResult { Id = service.ToString(), Success = true, Message = startIfStopped ? "Служба перезапущена/запущена." : "Служба остановлена." };
        }
        catch (Exception ex)
        {
            return new NativeActionResult { Id = service.ToString(), Success = false, Message = $"Служба: {ex.GetType().Name}, 0x{ex.HResult:X8}." };
        }
    }

    public PrintQueueClearResult ClearPrintQueue()
    {
        var windowsRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsRoot)) windowsRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? "";
        var queue = Path.Combine(windowsRoot, "System32", "spool", "PRINTERS");
        FileAttributes attributes;
        try { attributes = File.GetAttributes(queue); }
        catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException)
        {
            var restarted = EnsureSpoolerRunning();
            return new PrintQueueClearResult { Success = restarted, SpoolerRestarted = restarted, Message = "Каталог очереди отсутствует; файлы не удалялись. Spooler проверен." };
        }
        catch (Exception ex)
        {
            return new PrintQueueClearResult { Success = false, Message = $"Каталог очереди недоступен: {ex.GetType().Name}, 0x{ex.HResult:X8}." };
        }

        return ClearPrintQueueCore(
            windowsRoot,
            queue,
            attributes,
            StopSpooler,
            EnsureSpoolerRunning,
            () => Directory.EnumerateFileSystemEntries(queue, "*", SearchOption.TopDirectoryOnly).ToArray(),
            File.GetAttributes,
            File.Delete);
    }

    internal static PrintQueueClearResult ClearPrintQueueCore(
        string windowsRoot,
        string queue,
        FileAttributes attributes,
        Func<bool> stopSpooler,
        Func<bool> ensureSpoolerRunning,
        Func<IEnumerable<string>> enumerateEntries,
        Func<string, FileAttributes> getAttributes,
        Action<string> deleteFile)
    {
        ArgumentNullException.ThrowIfNull(stopSpooler);
        ArgumentNullException.ThrowIfNull(ensureSpoolerRunning);
        ArgumentNullException.ThrowIfNull(enumerateEntries);
        ArgumentNullException.ThrowIfNull(getAttributes);
        ArgumentNullException.ThrowIfNull(deleteFile);
        if (!IsSafeSpoolDirectory(windowsRoot, queue, attributes))
            return new PrintQueueClearResult { Success = false, Message = "Фиксированный каталог очереди не прошёл проверку пути/reparse; удаление отменено." };

        var deleted = 0;
        var skipped = 0;
        var errors = 0;
        var stopped = false;
        var restartSucceeded = false;
        try
        {
            stopped = stopSpooler();
            if (!stopped)
            {
                errors++;
            }
            else
            {
                foreach (var entry in enumerateEntries())
                {
                    try
                    {
                        var attr = getAttributes(entry);
                        if ((attr & FileAttributes.Directory) != 0 || (attr & FileAttributes.ReparsePoint) != 0)
                        {
                            skipped++;
                            continue;
                        }
                        deleteFile(entry);
                        deleted++;
                    }
                    catch { errors++; }
                }
            }
        }
        catch { errors++; }
        finally
        {
            restartSucceeded = ensureSpoolerRunning();
            if (!restartSucceeded) errors++;
        }

        return new PrintQueueClearResult
        {
            Success = stopped && errors == 0 && restartSucceeded,
            DeletedFiles = deleted,
            SkippedFiles = skipped,
            Errors = errors,
            SpoolerRestarted = restartSucceeded,
            Message = $"Очередь печати: Spooler остановлен: {(stopped ? "да" : "нет")}; удалено {deleted}; пропущено {skipped}; ошибок {errors}; Spooler запущен: {(restartSucceeded ? "да" : "нет")}."
        };
    }

    internal static bool IsSafeSpoolDirectory(string windowsRoot, string candidate, FileAttributes attributes)
    {
        if (string.IsNullOrWhiteSpace(windowsRoot) || string.IsNullOrWhiteSpace(candidate)) return false;
        if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0) return false;
        try
        {
            var expected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(windowsRoot, "System32", "spool", "PRINTERS")));
            var actual = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { return false; }
    }

    public IReadOnlyList<NetworkAdapterSnapshot> QueryNetworkAdapters()
    {
        var dhcpByIndex = new Dictionary<uint, bool>();
        using (var configSearcher = new ManagementObjectSearcher("SELECT Index, DHCPEnabled, IPEnabled FROM Win32_NetworkAdapterConfiguration"))
        using (var configs = configSearcher.Get())
        {
            foreach (ManagementObject config in configs)
            {
                using (config)
                {
                    var index = WmiUInt(config["Index"]);
                    if (index is not uint key) continue;
                    dhcpByIndex[key] = WmiBool(config["DHCPEnabled"]) && WmiBool(config["IPEnabled"]);
                }
            }
        }

        var result = new List<NetworkAdapterSnapshot>();
        using var adapterSearcher = new ManagementObjectSearcher("SELECT DeviceID, Index, Name, PhysicalAdapter, NetEnabled, NetConnectionStatus FROM Win32_NetworkAdapter");
        using var adapters = adapterSearcher.Get();
        foreach (ManagementObject adapter in adapters)
        {
            using (adapter)
            {
                var deviceId = Convert.ToString(adapter["DeviceID"]) ?? "";
                if (!uint.TryParse(deviceId, out _)) continue;
                var index = WmiUInt(adapter["Index"]);
                var enabled = WmiBool(adapter["NetEnabled"]);
                var connected = enabled && WmiUInt(adapter["NetConnectionStatus"]) == 2;
                result.Add(new NetworkAdapterSnapshot
                {
                    DeviceId = deviceId,
                    Name = Convert.ToString(adapter["Name"]) ?? "",
                    Physical = WmiBool(adapter["PhysicalAdapter"]),
                    Enabled = enabled,
                    Connected = connected,
                    DhcpEnabled = index is uint key && dhcpByIndex.TryGetValue(key, out var dhcp) && dhcp
                });
            }
        }
        return result;
    }

    public NativeActionResult SetAdapterEnabled(string stableDeviceId, bool enabled)
    {
        if (!uint.TryParse(stableDeviceId, out var expectedId))
            return new NativeActionResult { Id = stableDeviceId, Success = false, Message = "Некорректный OS-derived DeviceID адаптера." };
        using var searcher = new ManagementObjectSearcher("SELECT DeviceID, PhysicalAdapter, NetEnabled, NetConnectionStatus FROM Win32_NetworkAdapter");
        using var adapters = searcher.Get();
        foreach (ManagementObject adapter in adapters)
        {
            using (adapter)
            {
                if (WmiUInt(adapter["DeviceID"]) != expectedId) continue;
                if (!WmiBool(adapter["PhysicalAdapter"]))
                    return new NativeActionResult { Id = stableDeviceId, Success = false, Message = "Адаптер больше не подтверждён как PhysicalAdapter." };
                if (!enabled && (!WmiBool(adapter["NetEnabled"]) || WmiUInt(adapter["NetConnectionStatus"]) != 2))
                    return new NativeActionResult { Id = stableDeviceId, Success = false, Message = "Адаптер больше не является активным подключённым физическим путём; отключение не выполнено." };
                var method = enabled ? "Enable" : "Disable";
                var code = InvokeReturnCode(adapter, method);
                return new NativeActionResult
                {
                    Id = stableDeviceId,
                    Success = code == 0,
                    ExitCode = code is null ? null : unchecked((int)code.Value),
                    Message = code == 0 ? $"Адаптер {method} завершён успешно." : $"Win32_NetworkAdapter.{method} вернул код {(code?.ToString() ?? "—")}."
                };
            }
        }
        return new NativeActionResult { Id = stableDeviceId, Success = false, Message = "Адаптер с подтверждённым DeviceID больше не найден." };
    }

    public NativeActionResult CycleDhcpLease(string stableDeviceId)
    {
        if (!uint.TryParse(stableDeviceId, out var expectedId))
            return new NativeActionResult { Id = stableDeviceId, Success = false, Message = "Некорректный OS-derived DeviceID адаптера." };
        uint? adapterIndex = null;
        using (var adapterSearcher = new ManagementObjectSearcher("SELECT DeviceID, Index, NetEnabled, NetConnectionStatus FROM Win32_NetworkAdapter"))
        using (var adapters = adapterSearcher.Get())
        {
            foreach (ManagementObject adapter in adapters)
            {
                using (adapter)
                {
                    if (WmiUInt(adapter["DeviceID"]) != expectedId) continue;
                    if (!WmiBool(adapter["NetEnabled"]) || WmiUInt(adapter["NetConnectionStatus"]) != 2)
                        return new NativeActionResult { Id = stableDeviceId, Success = false, Message = "Адаптер больше не подключён; DHCP release/renew не выполнялся." };
                    adapterIndex = WmiUInt(adapter["Index"]);
                    break;
                }
            }
        }
        if (adapterIndex is not uint expectedIndex)
            return new NativeActionResult { Id = stableDeviceId, Success = false, Message = "Не удалось подтвердить Index адаптера для DHCP." };

        using var configSearcher = new ManagementObjectSearcher("SELECT Index, DHCPEnabled, IPEnabled FROM Win32_NetworkAdapterConfiguration");
        using var configs = configSearcher.Get();
        foreach (ManagementObject config in configs)
        {
            using (config)
            {
                if (WmiUInt(config["Index"]) != expectedIndex) continue;
                if (!WmiBool(config["IPEnabled"]) || !WmiBool(config["DHCPEnabled"]))
                    return new NativeActionResult { Id = stableDeviceId, Success = false, Message = "Адаптер больше не подтверждён как DHCP-enabled; статическая конфигурация не менялась." };
                var release = InvokeReturnCode(config, "ReleaseDHCPLease");
                var renew = InvokeReturnCode(config, "RenewDHCPLease");
                var ok = release == 0 && renew == 0;
                return new NativeActionResult
                {
                    Id = stableDeviceId,
                    Success = ok,
                    ExitCode = renew is null ? null : unchecked((int)renew.Value),
                    Message = $"DHCP release={release?.ToString() ?? "—"}; renew={renew?.ToString() ?? "—"}.",
                    Output = $"release={release?.ToString() ?? "null"}; renew={renew?.ToString() ?? "null"}"
                };
            }
        }
        return new NativeActionResult { Id = stableDeviceId, Success = false, Message = "DHCP-конфигурация подтверждённого адаптера не найдена." };
    }

    private static bool StopSpooler()
    {
        var state = new WindowsRepairOperations().QueryService(FixedService.Spooler);
        if (state.StartMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase)) return false;
        using var controller = new ServiceController(ServiceName(FixedService.Spooler));
        controller.Refresh();
        if (controller.Status == ServiceControllerStatus.Stopped) return true;
        if (!controller.CanStop) return false;
        controller.Stop();
        controller.WaitForStatus(ServiceControllerStatus.Stopped, ServiceTimeout);
        return true;
    }

    private static bool EnsureSpoolerRunning()
    {
        try
        {
            var state = new WindowsRepairOperations().QueryService(FixedService.Spooler);
            if (state.StartMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase)) return false;
            using var controller = new ServiceController(ServiceName(FixedService.Spooler));
            controller.Refresh();
            if (controller.Status != ServiceControllerStatus.Running)
            {
                if (controller.Status != ServiceControllerStatus.Stopped)
                    controller.WaitForStatus(ServiceControllerStatus.Stopped, ServiceTimeout);
                controller.Start();
                controller.WaitForStatus(ServiceControllerStatus.Running, ServiceTimeout);
            }
            return true;
        }
        catch { return false; }
    }

    private static string ServiceName(FixedService service) => service switch
    {
        FixedService.Spooler => "Spooler",
        FixedService.WindowsUpdate => "wuauserv",
        FixedService.Bits => "BITS",
        _ => throw new ArgumentOutOfRangeException(nameof(service))
    };

    private static bool WmiBool(object? value) => value is bool flag && flag;
    private static uint? WmiUInt(object? value)
    {
        if (value is null) return null;
        try { return Convert.ToUInt32(value); }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return null; }
    }
    private static uint? InvokeReturnCode(ManagementObject target, string method)
    {
        try
        {
            var value = target.InvokeMethod(method, Array.Empty<object>());
            return WmiUInt(value);
        }
        catch { return null; }
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); } catch { }
    }
}

internal static class ServiceDeskNonNetworkHandlers
{
    internal static RemediationActionResult Execute(string id, IWindowsRepairOperations operations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(operations);
        return id.ToLowerInvariant() switch
        {
            "flushdns" => FromNative("FlushDns", operations.RunFixedCommand(FixedCommand.FlushDns, TimeSpan.FromMinutes(2))),
            "restartspooler" => FromNative("RestartSpooler", operations.RestartService(FixedService.Spooler, true)),
            "clearprintqueue" => FromQueue(operations.ClearPrintQueue()),
            "restartupdateservices" => RestartUpdateServices(operations),
            "timeresync" => FromNative("TimeResync", operations.RunFixedCommand(FixedCommand.TimeResync, TimeSpan.FromMinutes(2))),
            "dism" => FromNative("Dism", operations.RunFixedCommand(FixedCommand.Dism, TimeSpan.FromMinutes(60))),
            "sfc" => FromNative("Sfc", operations.RunFixedCommand(FixedCommand.Sfc, TimeSpan.FromMinutes(60))),
            _ when ServiceDeskNetworkHandlers.CanHandle(id) => ServiceDeskNetworkHandlers.Execute(id, operations),
            _ => new RemediationActionResult { Id = id, Success = false, Message = "Действие отсутствует в фиксированном handler set." }
        };
    }

    internal static RemediationActionResult ExecuteMachineGpUpdate(IWindowsRepairOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        return FromNative("GpUpdate", operations.RunFixedCommand(FixedCommand.GpUpdateComputer, TimeSpan.FromMinutes(3)));
    }

    private static RemediationActionResult RestartUpdateServices(IWindowsRepairOperations operations)
    {
        var notes = new List<string>();
        var success = true;
        foreach (var service in new[] { FixedService.WindowsUpdate, FixedService.Bits })
        {
            try
            {
                var state = operations.QueryService(service);
                if (state.StartMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
                {
                    notes.Add($"{service}: Disabled — пропущено, startup type не менялся.");
                    continue;
                }
                var result = operations.RestartService(service, true);
                success &= result.Success;
                notes.Add($"{service}: {result.Message}");
            }
            catch (Exception ex)
            {
                success = false;
                notes.Add($"{service}: {ex.GetType().Name}, 0x{ex.HResult:X8}.");
            }
        }
        return new RemediationActionResult { Id = "RestartUpdateServices", Success = success, Message = string.Join(" ", notes) };
    }

    private static RemediationActionResult FromQueue(PrintQueueClearResult result)
        => new()
        {
            Id = "ClearPrintQueue",
            Success = result.Success,
            DeletedFiles = result.DeletedFiles,
            Message = result.Message,
            Output = $"deleted={result.DeletedFiles}; skipped={result.SkippedFiles}; errors={result.Errors}; spoolerRestarted={result.SpoolerRestarted}"
        };

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
