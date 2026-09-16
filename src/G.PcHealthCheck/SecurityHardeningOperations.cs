using System.Diagnostics;

namespace G.PcHealthCheck;

internal sealed class SecurityActionResult
{
    public string Id { get; init; } = "";
    public bool Success { get; init; }
    public bool BlockedByPolicy { get; init; }
    public int? ExitCode { get; init; }
    public string Message { get; init; } = "";
    public string Output { get; init; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public ExecutionContextInfo? ExecutionContext { get; set; }
    public string TargetScope { get; set; } = "";

    public static SecurityActionResult Succeeded(string id, string message)
        => new() { Id = id, Success = true, Message = message };
}

internal sealed class SecurityHardeningBatchResult
{
    public string SessionId { get; init; } = "";
    public DateTime StartedAt { get; init; }
    public DateTime FinishedAt { get; set; }
    public bool Elevated { get; init; }
    public List<SecurityActionResult> Actions { get; init; } = [];
}

internal interface ISecurityHardeningOperations
{
    SecurityActionResult UpdateDefinitions(SecurityPrimaryProvider provider);
    SecurityActionResult EnableDefenderRealtimeProtection();
    SecurityActionResult EnableWindowsFirewall(IReadOnlyList<string> fixedProfiles);
}

internal sealed class WindowsSecurityHardeningOperations : ISecurityHardeningOperations
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(10);
    private static readonly string[] FirewallProfiles = ["Domain", "Private", "Public"];

    public SecurityActionResult UpdateDefinitions(SecurityPrimaryProvider provider)
        => provider switch
        {
            SecurityPrimaryProvider.Defender => RunPowerShell(
                "SecurityUpdateAvDefinitions",
                FixedPowerShellOperation.DefenderUpdateDefinitions),
            SecurityPrimaryProvider.Kaspersky => RunKasperskyDefinitionsUpdate(),
            _ => Failure("SecurityUpdateAvDefinitions", "Primary AV provider is not supported for automatic definitions update.")
        };

    public SecurityActionResult EnableDefenderRealtimeProtection()
        => RunPowerShell("SecurityEnablePrimaryRtp", FixedPowerShellOperation.DefenderEnableRealtimeProtection);

    public SecurityActionResult EnableWindowsFirewall(IReadOnlyList<string> fixedProfiles)
    {
        ArgumentNullException.ThrowIfNull(fixedProfiles);
        if (!fixedProfiles.SequenceEqual(FirewallProfiles, StringComparer.OrdinalIgnoreCase))
            return Failure("SecurityEnableWindowsFirewall", "Firewall profile set is not the fixed approved Domain/Private/Public set.");
        return RunPowerShell("SecurityEnableWindowsFirewall", FixedPowerShellOperation.EnableWindowsFirewall);
    }

    private static SecurityActionResult RunKasperskyDefinitionsUpdate()
    {
        var path = FindKesCli();
        if (path is null)
            return Failure("SecurityUpdateAvDefinitions", "KESCLI is unavailable; Kaspersky definitions update was not started.");

        var start = new ProcessStartInfo
        {
            FileName = path,
            WorkingDirectory = Path.GetDirectoryName(path) ?? Environment.SystemDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("--opswat");
        start.ArgumentList.Add("UpdateDefinitions");
        return RunFixedProcess("SecurityUpdateAvDefinitions", start);
    }

    private static SecurityActionResult RunPowerShell(string id, FixedPowerShellOperation operation)
    {
        var script = operation switch
        {
            FixedPowerShellOperation.DefenderUpdateDefinitions =>
                "Update-MpSignature -ErrorAction Stop",
            FixedPowerShellOperation.DefenderEnableRealtimeProtection =>
                "Set-MpPreference -DisableRealtimeMonitoring $false -ErrorAction Stop",
            FixedPowerShellOperation.EnableWindowsFirewall =>
                "Set-NetFirewallProfile -Profile Domain,Private,Public -Enabled True -ErrorAction Stop",
            _ => throw new InvalidOperationException("Unknown fixed Security PowerShell operation.")
        };
        var path = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(path))
            return Failure(id, "Windows PowerShell is unavailable; fixed Security operation was not started.");

        var start = new ProcessStartInfo
        {
            FileName = path,
            WorkingDirectory = Environment.SystemDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(script);
        return RunFixedProcess(id, start);
    }

    private static SecurityActionResult RunFixedProcess(string id, ProcessStartInfo start)
    {
        try
        {
            using var process = Process.Start(start);
            if (process is null) return Failure(id, "Fixed Security operation could not be started.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)OperationTimeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return Failure(id, "Fixed Security operation timed out.");
            }
            Task.WaitAll(stdout, stderr);
            var output = Bound((stdout.Result + Environment.NewLine + stderr.Result).Trim());
            var blocked = LooksPolicyBlocked(output);
            return new SecurityActionResult
            {
                Id = id,
                Success = process.ExitCode == 0,
                BlockedByPolicy = blocked,
                ExitCode = process.ExitCode,
                Message = process.ExitCode == 0
                    ? "Fixed Security operation completed; post-action security recollection is still required."
                    : blocked
                        ? "Fixed Security operation was refused by security/management policy."
                        : "Fixed Security operation failed without bypassing policy.",
                Output = output
            };
        }
        catch (Exception ex)
        {
            return new SecurityActionResult
            {
                Id = id,
                Success = false,
                BlockedByPolicy = ex is UnauthorizedAccessException,
                Message = $"Fixed Security operation failed: {ex.GetType().Name}, 0x{ex.HResult:X8}."
            };
        }
    }

    private static string? FindKesCli()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            var candidate = Path.Combine(root, "Kaspersky Lab", "Kaspersky Endpoint Security for Windows", "kescli.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static bool LooksPolicyBlocked(string output)
        => !string.IsNullOrWhiteSpace(output)
           && new[] { "policy", "polic", "tamper", "access denied", "access is denied", "local task", "local tasks", "password" }
               .Any(x => output.Contains(x, StringComparison.OrdinalIgnoreCase));

    private static string Bound(string value)
        => value.Length <= 8000 ? value : value[^8000..];

    private static SecurityActionResult Failure(string id, string message)
        => new() { Id = id, Success = false, Message = message };

    private enum FixedPowerShellOperation
    {
        DefenderUpdateDefinitions,
        DefenderEnableRealtimeProtection,
        EnableWindowsFirewall
    }
}
