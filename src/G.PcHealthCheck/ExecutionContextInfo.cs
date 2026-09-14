using System.Text;

namespace G.PcHealthCheck;

// Evidence, not an authorization credential. Runtime actions recapture native facts.
public sealed record ExecutionContextInfo
{
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;
    public string ProcessAccount { get; init; } = "";
    public string ProcessSid { get; init; } = "";
    public string ProcessProfile { get; init; } = "";
    public int SessionId { get; init; } = -1;
    public bool? AdministratorMember { get; init; }
    public bool? HasAdministratorToken { get; init; }
    public bool? IsElevated { get; init; }
    public int? ElevationType { get; init; }
    public string SessionAccount { get; init; } = "";
    public string SessionSid { get; init; } = "";
    public string SessionProfile { get; init; } = "";
    public string ProfileSource { get; init; } = "";
    public List<string> Warnings { get; init; } = [];
}

internal sealed record ActionAvailability(string State, string Reason, string Scope)
{
    public bool CanRequest => State is "Ready" or "NeedsUac";
}

internal static class ExecutionPolicy
{
    public static string Mode(ExecutionContextInfo context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.HasAdministratorToken == true)
            return context.ElevationType == 1 ? "AdministratorFullToken" : context.IsElevated == true ? "AdministratorElevated" : "Unknown";
        if (context.HasAdministratorToken != false || context.IsElevated != false) return "Unknown";
        return context.AdministratorMember switch { true => "AdministratorLimited", false => "Standard", _ => "Unknown" };
    }
    public static string ModeText(ExecutionContextInfo? context) => context is null ? AppLocalization.T("ExecutionContext.Mode.Missing") : Mode(context) switch
    {
        "Standard" => AppLocalization.T("ExecutionContext.Mode.Standard"),
        "AdministratorLimited" => AppLocalization.T("ExecutionContext.Mode.AdministratorLimited"),
        "AdministratorElevated" => AppLocalization.T("ExecutionContext.Mode.AdministratorElevated"),
        "AdministratorFullToken" => AppLocalization.T("ExecutionContext.Mode.AdministratorFullToken"),
        _ => AppLocalization.T("ExecutionContext.Mode.Unknown")
    };
    public static bool SameUser(ExecutionContextInfo context)
        => !string.IsNullOrWhiteSpace(context.ProcessSid) && !string.IsNullOrWhiteSpace(context.SessionSid)
           && string.Equals(context.ProcessSid, context.SessionSid, StringComparison.Ordinal);

    internal static string? NormalizeProfile(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile) || profile.Contains('%') || !Path.IsPathFullyQualified(profile)
            || profile.StartsWith(@"\\?\", StringComparison.Ordinal) || profile.StartsWith(@"\\.\", StringComparison.Ordinal)) return null;
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(profile)); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { return null; }
    }
    public static string? PreviewRoot(ExecutionContextInfo context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.SessionId <= 0 || string.IsNullOrWhiteSpace(context.SessionSid)) return null;
        var profile = NormalizeProfile(context.SessionProfile);
        return profile is null ? null : Path.Combine(profile, "AppData", "Local", "Temp");
    }
    public static string? CleanupRoot(ExecutionContextInfo context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.IsElevated != false || context.HasAdministratorToken != false || !SameUser(context)) return null;
        var own = NormalizeProfile(context.ProcessProfile); var subject = NormalizeProfile(context.SessionProfile);
        if (own is null || subject is null || !string.Equals(own, subject, StringComparison.OrdinalIgnoreCase)) return null;
        return PreviewRoot(context);
    }
    public static ActionAvailability For(string actionId, ExecutionContextInfo context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var id = actionId.ToLowerInvariant();
        if (id == "temppreview")
        {
            var root = PreviewRoot(context);
            return root is null ? new("Unavailable", AppLocalization.T("ExecutionContext.Availability.TempPreview.Unavailable"), AppLocalization.T("ExecutionContext.Scope.Undefined"))
                : new("Ready", AppLocalization.T("ExecutionContext.Availability.TempPreview.Ready"), root);
        }
        if (id == "cleantemp")
        {
            var root = CleanupRoot(context);
            return root is not null ? new("Ready", AppLocalization.T("ExecutionContext.Availability.CleanTemp.Ready"), root)
                : new("Unavailable", AppLocalization.T("ExecutionContext.Availability.CleanTemp.Unavailable"), PreviewRoot(context) ?? AppLocalization.T("ExecutionContext.Scope.Undefined"));
        }
        if (id == "flushdns") return new("Ready", AppLocalization.T("ExecutionContext.Availability.FlushDns.Ready"), AppLocalization.T("ExecutionContext.Scope.DnsCache"));
        if (id == "diagnostics") return new("Ready", AppLocalization.T("ExecutionContext.Availability.Diagnostics.Ready"), AppLocalization.T("ExecutionContext.Scope.Diagnostics"));
        if (id == "startupreview") return new("Ready", AppLocalization.T("ExecutionContext.Availability.StartupReview.Ready"), context.ProcessAccount);

        var descriptor = ServiceDeskActionRegistry.Find(actionId);
        if (descriptor is not null)
        {
            if (!ServiceDeskActionRegistry.IsExecutableHandler(actionId))
                return new("Unavailable", AppLocalization.T("ExecutionContext.Availability.HandlerUnavailable"), Scope(descriptor.Id));
            if (descriptor.RequiresAdministrator)
                return context.HasAdministratorToken switch
                {
                    true => new("Ready", AppLocalization.T("ExecutionContext.Availability.AdminAlreadyActive"), Scope(descriptor.Id)),
                    false => new("NeedsUac", AppLocalization.T("ExecutionContext.Availability.NeedsUac"), Scope(descriptor.Id)),
                    _ => new("Unavailable", AppLocalization.T("ExecutionContext.Availability.RightsUnknown"), Scope(descriptor.Id))
                };
            return new("Ready", AppLocalization.T("ExecutionContext.Availability.Ready"), Scope(descriptor.Id));
        }
        return new("Unavailable", AppLocalization.T("ExecutionContext.Availability.UnknownAction"), "—");
    }
    private static string Scope(string actionId) => actionId.ToLowerInvariant() switch
    {
        "restartspooler" => AppLocalization.T("ExecutionContext.Scope.RestartSpooler"),
        "clearprintqueue" => AppLocalization.T("ExecutionContext.Scope.ClearPrintQueue"),
        "restartupdateservices" => AppLocalization.T("ExecutionContext.Scope.RestartUpdateServices"),
        "timeresync" => AppLocalization.T("ExecutionContext.Scope.TimeResync"),
        "dism" or "sfc" => AppLocalization.T("ExecutionContext.Scope.Windows"),
        "gpupdate" => "Computer/User Group Policy",
        _ => AppLocalization.T("ExecutionContext.Scope.Windows")
    };
    public static string StateText(string state) => state switch
    {
        "Ready" => AppLocalization.T("ExecutionContext.State.Ready"),
        "NeedsUac" => AppLocalization.T("ExecutionContext.State.NeedsUac"),
        "Manual" => AppLocalization.T("ExecutionContext.State.Manual"),
        _ => AppLocalization.T("ExecutionContext.State.Unavailable")
    };
    public static string Describe(ExecutionContextInfo? context)
    {
        if (context is null) return AppLocalization.T("ExecutionContext.Describe.Missing");
        var s = new StringBuilder();
        s.AppendLine(ModeText(context));
        s.AppendLine(AppLocalization.T("ExecutionContext.Describe.ProcessAccount", Value(context.ProcessAccount), Value(context.ProcessSid)));
        s.AppendLine(AppLocalization.T("ExecutionContext.Describe.SessionUser", context.SessionId, Value(context.SessionAccount), Value(context.SessionSid)));
        s.AppendLine(AppLocalization.T("ExecutionContext.Describe.Elevation", Flag(context.IsElevated), Flag(context.HasAdministratorToken), Flag(context.AdministratorMember)));
        s.AppendLine(AppLocalization.T("ExecutionContext.Describe.SessionProfile", Value(context.SessionProfile), Value(context.ProfileSource)));
        s.AppendLine(AppLocalization.T("ExecutionContext.Describe.ProcessProfile", Value(context.ProcessProfile)));
        if (!SameUser(context)) s.AppendLine(AppLocalization.T("ExecutionContext.Describe.DifferentUsers"));
        s.AppendLine(AppLocalization.T("ExecutionContext.Describe.NoAutoElevation"));
        s.AppendLine(AppLocalization.T("ExecutionContext.Describe.CapturedAt", context.CapturedAt));
        foreach (var warning in context.Warnings) s.AppendLine("! " + warning);
        return s.ToString().TrimEnd();
    }
    internal static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? AppLocalization.T("ExecutionContext.Value.Undefined") : value;
    internal static string Flag(bool? value) => value is null ? AppLocalization.T("ExecutionContext.Flag.Unknown") : value.Value ? AppLocalization.T("ExecutionContext.Flag.Yes") : AppLocalization.T("ExecutionContext.Flag.No");
}
