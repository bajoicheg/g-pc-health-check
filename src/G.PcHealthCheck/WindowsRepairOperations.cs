using System.Diagnostics;
using System.ServiceProcess;

namespace G.PcHealthCheck;

internal enum FixedCommand
{
    FlushDns,
    TimeResync,
    GpUpdateComputer,
    Dism,
    Sfc
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
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };

    private static FixedCommandSpec Spec(string id, string exe, string arguments, TimeSpan timeout)
        => new()
        {
            Id = id,
            FileName = Path.Combine(Environment.SystemDirectory, exe),
            Arguments = arguments,
            Timeout = timeout
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
                Message = $"Превышено время выполнения {effectiveTimeout.TotalMinutes:0.#} мин."
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
            Output = output
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

        if (!IsSafeSpoolDirectory(windowsRoot, queue, attributes))
            return new PrintQueueClearResult { Success = false, Message = "Фиксированный каталог очереди не прошёл проверку пути/reparse; удаление отменено." };

        var deleted = 0;
        var skipped = 0;
        var errors = 0;
        var restartSucceeded = false;
        try
        {
            if (!StopSpooler()) errors++;
            foreach (var entry in Directory.EnumerateFileSystemEntries(queue, "*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var attr = File.GetAttributes(entry);
                    if ((attr & FileAttributes.Directory) != 0 || (attr & FileAttributes.ReparsePoint) != 0)
                    {
                        skipped++;
                        continue;
                    }
                    File.Delete(entry);
                    deleted++;
                }
                catch { errors++; }
            }
        }
        catch { errors++; }
        finally
        {
            restartSucceeded = EnsureSpoolerRunning();
            if (!restartSucceeded) errors++;
        }

        return new PrintQueueClearResult
        {
            Success = errors == 0 && restartSucceeded,
            DeletedFiles = deleted,
            SkippedFiles = skipped,
            Errors = errors,
            SpoolerRestarted = restartSucceeded,
            Message = $"Очередь печати: удалено {deleted}; пропущено {skipped}; ошибок {errors}; Spooler запущен: {(restartSucceeded ? "да" : "нет")}."
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
            _ => new RemediationActionResult { Id = id, Success = false, Message = "Действие отсутствует в фиксированном non-network handler set." }
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
            Output = result.Output
        };
}
