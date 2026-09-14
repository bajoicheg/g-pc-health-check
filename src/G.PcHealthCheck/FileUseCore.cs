using System.Globalization;

namespace G.PcHealthCheck;

internal static class FileUseCore
{
    public static string NormalizeTarget(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        var path = target.Trim();
        if (path.Length >= 2 && path[0] == '"' && path[^1] == '"') path = path[1..^1];
        path = path.Replace('/', '\\');
        if (path.Length is < 4 or > 32760 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\'
            || path.AsSpan(2).Contains(':') || path.Any(char.IsControl) || path.IndexOfAny(['<', '>', '"', '|', '*', '?']) >= 0)
            throw new ArgumentException(AppLocalization.T("FileUse.Core.TargetAbsolute"), nameof(target));
        // Validate input components before normalizing away dot/parent segments.
        foreach (var part in path[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part is "." or "..") continue;
            if (part.EndsWith(' ') || part.EndsWith('.') || ReservedComponent(part))
                throw new ArgumentException(AppLocalization.T("FileUse.Core.TargetReserved"), nameof(target));
        }
        var full = Path.GetFullPath(path);
        if (full.Length <= 3) throw new ArgumentException(AppLocalization.T("FileUse.Core.TargetFile"), nameof(target));
        return full;
    }
    private static bool ReservedComponent(string part)
    {
        var stem = part.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$") return true;
        return stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
            && "123456789¹²³".Contains(stem[3]);
    }
    public static DateTimeOffset? StartTime(ulong fileTime)
    {
        if (fileTime == 0 || fileTime > long.MaxValue) return null;
        try { return new DateTimeOffset(DateTime.FromFileTimeUtc((long)fileTime)); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
    public static FileUseProcess Associate(FileUseProcess row, FileUseIdentity? identity)
    {
        ArgumentNullException.ThrowIfNull(row);
        var clean = row with { IdentityState = "Unavailable", ProcessName = "", ImagePath = "", IdentityError = identity?.ErrorCode };
        if (row.Pid == 0 || StartTime(row.StartFileTime) is null) return clean with { IdentityState = "Invalid" };
        if (identity is null || identity.ErrorCode is not null || StartTime(identity.StartFileTime) is null) return clean;
        if (row.Pid != identity.Pid || row.StartFileTime != identity.StartFileTime) return clean with { IdentityState = "Changed" };
        if (string.IsNullOrWhiteSpace(identity.ImagePath)) return clean;
        return clean with { IdentityState = "Matched", ImagePath = identity.ImagePath, ProcessName = Path.GetFileName(identity.ImagePath) };
    }
    public static bool Matches(FileUseProcess row, string query)
    {
        var q = query.Trim(); if (q.Length == 0) return true;
        return new[] { row.ApplicationName, row.ServiceName, row.ProcessName, row.ImagePath, row.Pid.ToString(CultureInfo.InvariantCulture), TypeText(row.ApplicationType), IdentityText(row.IdentityState) }
            .Any(x => x.Contains(q, StringComparison.OrdinalIgnoreCase));
    }
    public static string Verdict(FileUseSnapshot snapshot)
    {
        if (!snapshot.ListCompleted)
            return AppLocalization.T(snapshot.State == "Cancelled" ? "FileUse.Core.Verdict.CancelledNoList" : "FileUse.Core.Verdict.NoList");
        var prefix = snapshot.State == "Cancelled"
            ? AppLocalization.T("FileUse.Core.Verdict.CancelledPrefix")
            : snapshot.State == "Partial" ? AppLocalization.T("FileUse.Core.Verdict.PartialPrefix") : "";
        return prefix + (snapshot.Processes.Count > 0
            ? AppLocalization.T("FileUse.Core.Verdict.Found", snapshot.Processes.Count)
            : AppLocalization.T("FileUse.Core.Verdict.Empty"));
    }
    public static string Session(FileUseProcess row) => row.ApplicationType is 3 or 1000 || row.SessionId == uint.MaxValue ? "—" : row.SessionId.ToString(CultureInfo.InvariantCulture);
    public static string TypeText(uint type) => type switch
    {
        0 => AppLocalization.T("FileUse.Core.Type.Undefined"),
        1 => AppLocalization.T("FileUse.Core.Type.Application"),
        2 => AppLocalization.T("FileUse.Core.Type.Window"),
        3 => AppLocalization.T("FileUse.Core.Type.Service"),
        4 => AppLocalization.T("FileUse.Core.Type.Explorer"),
        5 => AppLocalization.T("FileUse.Core.Type.Console"),
        1000 => AppLocalization.T("FileUse.Core.Type.Critical"),
        _ => AppLocalization.T("FileUse.Core.Type.Unknown", type)
    };
    public static string IdentityText(string state) => state switch
    {
        "Matched" => AppLocalization.T("FileUse.Core.Identity.Matched"),
        "Changed" => AppLocalization.T("FileUse.Core.Identity.Changed"),
        "Invalid" => AppLocalization.T("FileUse.Core.Identity.Invalid"),
        "NotChecked" => AppLocalization.T("FileUse.Core.Identity.NotChecked"),
        _ => AppLocalization.T("FileUse.Core.Identity.Unavailable")
    };
    public static string StateText(string state) => state switch
    {
        "Complete" => AppLocalization.T("FileUse.Core.State.Complete"),
        "Partial" => AppLocalization.T("FileUse.Core.State.Partial"),
        "Cancelled" => AppLocalization.T("FileUse.Core.State.Cancelled"),
        "Unavailable" => AppLocalization.T("FileUse.Core.State.Unavailable"),
        _ => AppLocalization.T("FileUse.Core.State.NotCollected")
    };
}
