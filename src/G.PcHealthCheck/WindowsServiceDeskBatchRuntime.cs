using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;

namespace G.PcHealthCheck;

internal sealed record WorkerLaunchRequest(
    IReadOnlyList<string> ActionIds,
    string PipeName,
    string Arguments);

internal sealed class WindowsServiceDeskBatchRuntime : IServiceDeskBatchRuntime
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromMinutes(2);
    private readonly Func<ExecutionContextInfo> _captureContext;
    private readonly IWindowsRepairOperations _operations;
    private readonly Func<ExecutionContextInfo, int, RemediationActionResult> _cleanTemp;

    public WindowsServiceDeskBatchRuntime()
        : this(
            ExecutionContextService.Capture,
            new WindowsRepairOperations(),
            ServiceDeskParentTempCleanup.Execute)
    {
    }

    internal WindowsServiceDeskBatchRuntime(
        Func<ExecutionContextInfo> captureContext,
        IWindowsRepairOperations operations,
        Func<ExecutionContextInfo, int, RemediationActionResult> cleanTemp)
    {
        _captureContext = captureContext ?? throw new ArgumentNullException(nameof(captureContext));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _cleanTemp = cleanTemp ?? throw new ArgumentNullException(nameof(cleanTemp));
    }

    public ExecutionContextInfo CaptureParentContext()
        => _captureContext()
            ?? throw new InvalidOperationException("Не удалось получить текущий контекст родительского процесса.");

    public IServiceDeskWorkerSession StartWorker(
        ServiceDeskPhasePlan plan,
        string sessionId,
        string nonce,
        bool requestElevation)
    {
        var request = BuildWorkerRequest(plan, sessionId, nonce);
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Не удалось определить путь к текущему EXE.");
        var pipe = RemediationWorker.CreatePipeServer(request.PipeName);
        Process? child = null;
        try
        {
            var startInfo = RemediationWorker.CreateWorkerStartInfo(executable, request.Arguments, requestElevation);
            try
            {
                child = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Не удалось запустить phased worker.");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                throw new OperationCanceledException("Запрос UAC отменён.", ex);
            }

            var connection = pipe.WaitForConnectionAsync();
            var exit = child.WaitForExitAsync();
            var timeout = Task.Delay(ConnectionTimeout);
            _ = Task.WhenAny(connection, exit, timeout).GetAwaiter().GetResult();

            if (connection.IsCompletedSuccessfully)
                return new WindowsServiceDeskWorkerSession(pipe, child);

            if (timeout.IsCompleted)
                throw new TimeoutException("Phased worker не подключился к защищённому duplex-каналу за 2 минуты.");

            if (connection.IsFaulted)
                connection.GetAwaiter().GetResult();

            if (exit.IsCompleted)
            {
                exit.GetAwaiter().GetResult();
                throw new InvalidOperationException($"Phased worker завершился до подключения к каналу. Код: {child.ExitCode}.");
            }

            throw new InvalidOperationException("Не удалось подтвердить подключение phased worker.");
        }
        catch
        {
            TryKill(child);
            child?.Dispose();
            pipe.Dispose();
            throw;
        }
    }

    public RemediationActionResult ExecuteParentAction(
        string actionId,
        int tempDays,
        ExecutionContextInfo parentContext)
    {
        ArgumentNullException.ThrowIfNull(parentContext);
        var currentContext = CaptureParentContext();
        return ExecuteParentActionCore(
            actionId,
            tempDays,
            parentContext,
            currentContext,
            _operations,
            _cleanTemp);
    }

    internal static RemediationActionResult ExecuteParentActionCore(
        string actionId,
        int tempDays,
        ExecutionContextInfo originalContext,
        ExecutionContextInfo currentContext,
        IWindowsRepairOperations operations,
        Func<ExecutionContextInfo, int, RemediationActionResult> cleanTemp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        ArgumentNullException.ThrowIfNull(originalContext);
        ArgumentNullException.ThrowIfNull(currentContext);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(cleanTemp);
        tempDays = Math.Clamp(tempDays, 1, 30);

        var started = DateTime.Now;
        RemediationActionResult result;
        string scope;
        switch (actionId.ToLowerInvariant())
        {
            case "gpupdate":
                RequireStableOriginalUser(originalContext, currentContext, requireCleanupRoot: false);
                result = FromNative(
                    "GpUpdate",
                    operations.RunFixedCommand(FixedCommand.GpUpdateUser, TimeSpan.FromMinutes(3)));
                scope = "original-user";
                break;

            case "cleantemp":
                RequireStableOriginalUser(originalContext, currentContext, requireCleanupRoot: true);
                result = cleanTemp(currentContext, tempDays)
                    ?? throw new InvalidOperationException("CleanTemp не вернул результат.");
                if (!result.Id.Equals("CleanTemp", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("CleanTemp вернул результат другого действия.");
                scope = "original-user";
                break;

            case "flushdns":
                result = FromNative(
                    "FlushDns",
                    operations.RunFixedCommand(FixedCommand.FlushDns, TimeSpan.FromMinutes(2)));
                scope = "machine-parent";
                break;

            default:
                throw new InvalidOperationException($"Действие {actionId} не разрешено на parent-side boundary.");
        }

        result.Id = actionId.Equals("GpUpdate", StringComparison.OrdinalIgnoreCase)
            ? "GpUpdate"
            : actionId.Equals("CleanTemp", StringComparison.OrdinalIgnoreCase)
                ? "CleanTemp"
                : "FlushDns";
        result.StartedAt = started;
        result.FinishedAt = DateTime.Now;
        result.ExecutionContext = currentContext;
        result.TargetScope = scope;
        return result;
    }

    internal static WorkerLaunchRequest BuildWorkerRequest(
        ServiceDeskPhasePlan plan,
        string sessionId,
        string nonce)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!Guid.TryParse(sessionId, out _))
            throw new InvalidOperationException("Phased worker session ID имеет некорректный формат.");
        if (nonce is not { Length: 64 } || !nonce.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Phased worker nonce имеет некорректный формат.");

        var actionIds = plan.WorkerBeforeNetwork
            .Concat(plan.WorkerNetwork)
            .ToList();
        if (actionIds.Count == 0)
            throw new InvalidOperationException("Phased worker request не содержит worker-действий.");

        var csv = string.Join(',', actionIds);
        if (!RemediationWorker.TryBuildWorkerPhasePlan(csv, out var parsed))
            throw new InvalidOperationException("Phased worker request не прошёл фиксированный worker allow-list.");
        if (!parsed.WorkerBeforeNetwork.SequenceEqual(plan.WorkerBeforeNetwork, StringComparer.OrdinalIgnoreCase)
            || !parsed.WorkerNetwork.SequenceEqual(plan.WorkerNetwork, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Phased worker request не совпадает с утверждённым phase plan.");

        var pipeName = "GPcHealthCheck-" + sessionId;
        var arguments = $"--phased-worker --session {Quote(sessionId)} --actions {Quote(csv)} --pipe {Quote(pipeName)} --nonce {Quote(nonce)}";
        return new WorkerLaunchRequest(actionIds, pipeName, arguments);
    }

    private static void RequireStableOriginalUser(
        ExecutionContextInfo originalContext,
        ExecutionContextInfo currentContext,
        bool requireCleanupRoot)
    {
        if (!ExecutionPolicy.SameUser(originalContext)
            || !ExecutionPolicy.SameUser(currentContext)
            || originalContext.SessionId != currentContext.SessionId
            || !SameSid(originalContext.ProcessSid, currentContext.ProcessSid)
            || !SameSid(originalContext.SessionSid, currentContext.SessionSid))
            throw new InvalidOperationException("Контекст исходного пользователя изменился; parent-side действие отменено до мутации.");

        if (!requireCleanupRoot) return;
        var originalRoot = ExecutionPolicy.CleanupRoot(originalContext);
        var currentRoot = ExecutionPolicy.CleanupRoot(currentContext);
        if (originalRoot is null || currentRoot is null)
            throw new InvalidOperationException("CleanTemp требует подтверждённый неповышенный контекст того же пользователя.");
        try
        {
            var originalFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(originalRoot));
            var currentFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(currentRoot));
            if (!string.Equals(originalFull, currentFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Путь пользовательского Temp изменился после подтверждения; очистка отменена.");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            throw new InvalidOperationException("Не удалось подтвердить стабильный путь пользовательского Temp.", ex);
        }
    }

    private static bool SameSid(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static RemediationActionResult FromNative(string id, NativeActionResult native)
        => new()
        {
            Id = id,
            Success = native.Success,
            ExitCode = native.ExitCode,
            Message = native.Message,
            Output = native.Output,
            RebootRecommended = native.RebootRecommended
        };

    private static string Quote(string value)
        => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static void TryKill(Process? process)
    {
        try { if (process is not null && !process.HasExited) process.Kill(true); }
        catch { }
    }
}

internal sealed class WindowsServiceDeskWorkerSession : IServiceDeskWorkerSession
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(125);
    private readonly NamedPipeServerStream _pipe;
    private readonly Process _child;
    private readonly JsonWorkerMessageChannel _channel;
    private bool _completed;
    private bool _disposed;

    internal WindowsServiceDeskWorkerSession(NamedPipeServerStream pipe, Process child)
    {
        _pipe = pipe ?? throw new ArgumentNullException(nameof(pipe));
        _child = child ?? throw new ArgumentNullException(nameof(child));
        _channel = new JsonWorkerMessageChannel(pipe, leaveOpen: true);
    }

    public WorkerMessage Receive()
    {
        ThrowIfDisposed();
        var read = Task.Run(_channel.Receive);
        if (Task.WhenAny(read, Task.Delay(OperationTimeout)).GetAwaiter().GetResult() != read)
        {
            TryKill();
            throw new TimeoutException("Превышено время ожидания сообщения phased worker.");
        }
        return read.GetAwaiter().GetResult();
    }

    public void Send(WorkerMessage message)
    {
        ThrowIfDisposed();
        _channel.Send(message);
    }

    public void WaitForExit()
    {
        ThrowIfDisposed();
        var exit = _child.WaitForExitAsync();
        if (Task.WhenAny(exit, Task.Delay(OperationTimeout)).GetAwaiter().GetResult() != exit)
        {
            TryKill();
            throw new TimeoutException("Превышено общее время завершения phased worker.");
        }
        exit.GetAwaiter().GetResult();
        _completed = true;
        if (_child.ExitCode is not 0 and not 2)
            throw new InvalidOperationException($"Phased worker завершился с кодом {_child.ExitCode} после protocol exchange.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_completed) TryKill();
        try { _channel.Dispose(); } catch { }
        try { _pipe.Dispose(); } catch { }
        _child.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WindowsServiceDeskWorkerSession));
    }

    private void TryKill()
    {
        try { if (!_child.HasExited) _child.Kill(true); }
        catch { }
    }
}

internal static class ServiceDeskParentTempCleanup
{
    internal static RemediationActionResult Execute(ExecutionContextInfo context, int olderThanDays)
    {
        ArgumentNullException.ThrowIfNull(context);
        var root = ExecutionPolicy.CleanupRoot(context);
        if (root is null)
            return new RemediationActionResult
            {
                Id = "CleanTemp",
                Success = false,
                Message = "Очистка не запущена: нужен неповышенный процесс того же пользователя и подтверждённый профиль текущего сеанса."
            };

        var cutoff = DateTime.Now.AddDays(-Math.Clamp(olderThanDays, 1, 30));
        long files = 0;
        long bytes = 0;
        var errors = 0;
        try
        {
            var attributes = File.GetAttributes(root);
            if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
                return new RemediationActionResult { Id = "CleanTemp", Success = false, Message = "Корень Temp не является обычной папкой; очистка не начата." };
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new RemediationActionResult { Id = "CleanTemp", Success = true, Message = "Каталог Temp отсутствует; файлы не удалялись." };
        }
        catch (Exception ex)
        {
            return new RemediationActionResult
            {
                Id = "CleanTemp",
                Success = false,
                Message = $"Корень Temp недоступен: {ex.GetType().Name}, 0x{ex.HResult:X8}. Файлы не удалялись."
            };
        }

        string rootFull;
        try { rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return new RemediationActionResult { Id = "CleanTemp", Success = false, Message = "Не удалось нормализовать путь пользовательского Temp; очистка отменена." };
        }
        if (IsReparsePoint(rootFull))
            return new RemediationActionResult { Id = "CleanTemp", Success = false, Message = "Корень пользовательского Temp является reparse point или недоступен; очистка отменена." };

        var stack = new Stack<string>();
        stack.Push(rootFull);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (IsReparsePoint(current)) { errors++; continue; }
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(current).ToArray(); }
            catch { errors++; continue; }
            foreach (var entry in entries)
            {
                try
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        stack.Push(entry);
                        continue;
                    }
                    var info = new FileInfo(entry);
                    if (info.LastWriteTime >= cutoff) continue;
                    var length = info.Length;
                    File.Delete(entry);
                    files++;
                    bytes += length;
                }
                catch { errors++; }
            }
        }

        return new RemediationActionResult
        {
            Id = "CleanTemp",
            Success = true,
            FreedMB = Math.Round(bytes / 1024d / 1024d, 1),
            DeletedFiles = files,
            Message = $"Temp текущего пользователя: удалено файлов {files}; освобождено {bytes / 1024d / 1024d:0.0} MB; пропущено/ошибок: {errors}."
        };
    }

    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch { return true; }
    }
}
