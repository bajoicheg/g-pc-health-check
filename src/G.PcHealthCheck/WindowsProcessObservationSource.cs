using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace G.PcHealthCheck;

// One query-only handle for the whole session. No re-open by PID after exit,
// debug privileges, remote process access, memory-content reads or modification.
internal sealed class WindowsProcessObservationSource(ProcessObservationTarget target) : IProcessObservationSource
{
    private SafeProcessHandle? _process;
    private bool _attempted;
    private bool _disposed;
    private ulong _created;
    private string _image = "";
    private ProcessCounters? _terminal;
    public ProcessCounters Read(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ct.ThrowIfCancellationRequested();
        if (_terminal is not null) return _terminal;
        if (!_attempted)
        {
            _attempted = true;
            if (target.Pid == 0 || !ProcessObservationCore.ValidCreatedAt(target.CreatedAt)) return Stop("Unavailable", "Не определён экземпляр процесса.");
            _process = OpenProcess(0x1000 | 0x100000, false, target.Pid); // QUERY_LIMITED_INFORMATION | SYNCHRONIZE
            if (_process.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                return Stop(error == 5 ? "AccessDenied" : "Unavailable", $"OpenProcess, Win32 {error}. Повышение прав автоматически не запрашивается.");
            }
        }
        var handle = _process!;
        var wait = WaitForSingleObject(handle, 0);
        if (wait == 0) return Stop("Exited", "Выбранный процесс завершён; наблюдение за компьютером продолжается.");
        if (wait != 258) return Missing($"Проверка состояния процесса: Win32 {Marshal.GetLastWin32Error()}.");
        if (!GetProcessTimes(handle, out var creation, out _, out var kernel, out var user)) return Missing($"GetProcessTimes, Win32 {Marshal.GetLastWin32Error()}.");
        var created = creation.Value;
        if (!ProcessObservationCore.Matches(target, new() { Pid = target.Pid, CreatedFileTime = created }) || (_created != 0 && _created != created))
            return Stop("IdentityChanged", "Время создания не совпало с выбранным экземпляром процесса.");
        _created = created;
        ct.ThrowIfCancellationRequested();
        var warnings = new List<string>();
        var memory = new MemoryCounters { Size = (uint)Marshal.SizeOf<MemoryCounters>() };
        ulong? working = null, committed = null;
        if (K32GetProcessMemoryInfo(handle, ref memory, memory.Size)) { working = memory.WorkingSetSize.ToUInt64(); committed = memory.PrivateUsage.ToUInt64(); }
        else warnings.Add($"Память: GetProcessMemoryInfo, Win32 {Marshal.GetLastWin32Error()}.");
        ulong? read = null, write = null;
        if (GetProcessIoCounters(handle, out var io)) { read = io.ReadTransferCount; write = io.WriteTransferCount; }
        else warnings.Add($"I/O: GetProcessIoCounters, Win32 {Marshal.GetLastWin32Error()}.");
        var count = GetActiveProcessorCount(ushort.MaxValue);
        var logical = count is > 0 and <= int.MaxValue ? (int)count : 0;
        if (logical == 0) warnings.Add($"Число процессоров системы недоступно: Win32 {Marshal.GetLastWin32Error()}.");
        if (_image.Length == 0)
        {
            var buffer = new StringBuilder(32768); var length = (uint)buffer.Capacity;
            if (QueryFullProcessImageNameW(handle, 0, buffer, ref length)) _image = buffer.ToString();
        }
        ct.ThrowIfCancellationRequested();
        wait = WaitForSingleObject(handle, 0);
        if (wait == 0) return Stop("Exited", "Процесс завершился во время чтения; его незавершённый замер не используется.");
        if (wait != 258) return Missing($"Не удалось подтвердить состояние процесса после чтения, Win32 {Marshal.GetLastWin32Error()}.");
        return new()
        {
            Pid = target.Pid, CreatedFileTime = created, State = "Live", Kernel100ns = kernel.Value, User100ns = user.Value,
            WorkingSetBytes = working, PrivateBytes = committed, ReadBytes = read, WriteBytes = write,
            LogicalProcessors = logical, ImagePath = _image, Warnings = warnings
        };
    }
    private ProcessCounters Missing(string warning) => new() { Pid = target.Pid, CreatedFileTime = _created, State = "Unavailable", Warnings = [warning] };
    private ProcessCounters Stop(string state, string warning) => _terminal = new() { Pid = target.Pid, CreatedFileTime = _created, State = state, Warnings = [warning] };
    public void Dispose() { if (_disposed) return; _disposed = true; _process?.Dispose(); }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeTime { public uint Low; public uint High; public readonly ulong Value => ((ulong)High << 32) | Low; }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    { public ulong ReadOperationCount; public ulong WriteOperationCount; public ulong OtherOperationCount; public ulong ReadTransferCount; public ulong WriteTransferCount; public ulong OtherTransferCount; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryCounters
    {
        public uint Size; public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize; public UIntPtr WorkingSetSize; public UIntPtr QuotaPeakPagedPoolUsage;
        public UIntPtr QuotaPagedPoolUsage; public UIntPtr QuotaPeakNonPagedPoolUsage; public UIntPtr QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage; public UIntPtr PeakPagefileUsage; public UIntPtr PrivateUsage;
    }
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out NativeTime creation, out NativeTime exit, out NativeTime kernel, out NativeTime user);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(SafeProcessHandle process, out IoCounters counters);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool K32GetProcessMemoryInfo(SafeProcessHandle process, ref MemoryCounters counters, uint size);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetActiveProcessorCount(ushort group);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder path, ref uint size);
}
