using System.Diagnostics;
using System.Globalization;

namespace G.PcHealthCheck;

internal static class KasperskySecurityReader
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(10);

    public static KasperskySecurityObservation Read()
    {
        var path = FindKesCli();
        if (path is null) return new(null, null, null, "KESCLI unavailable");

        bool? rtp = null;
        DateTime? definitions = null;
        try { rtp = ParseRtpOutput(Run(path, "--opswat GetRealTimeProtectionState")); } catch { }
        try { definitions = ParseDefinitionOutput(Run(path, "--opswat GetDefinitionState")); } catch { }
        string? version = null;
        try { version = FileVersionInfo.GetVersionInfo(path).ProductVersion; } catch { }
        return new(rtp, definitions, version, "KESCLI OPSWAT");
    }

    internal static bool? ParseRtpOutput(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        foreach (var token in output.Split(['\r', '\n', ' ', '\t', ':', '='], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token == "1") return true;
            if (token == "0") return false;
        }
        return null;
    }

    internal static DateTime? ParseDefinitionOutput(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        var candidates = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var candidate in candidates.Reverse())
        {
            if (DateTime.TryParse(candidate, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var invariant))
                return invariant;
            var separator = candidate.IndexOfAny([':', '=']);
            if (separator >= 0)
            {
                var value = candidate[(separator + 1)..].Trim();
                if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out invariant))
                    return invariant;
                if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var local))
                    return local;
            }
        }
        return null;
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

    private static string Run(string path, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = path,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("KESCLI did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit((int)QueryTimeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("KESCLI read-only query timed out.");
        }
        Task.WaitAll(stdout, stderr);
        var text = (stdout.Result + Environment.NewLine + stderr.Result).Trim();
        return text.Length <= 8000 ? text : text[^8000..];
    }
}
