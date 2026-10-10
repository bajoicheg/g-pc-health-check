using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace G.PcHealthCheck;

internal sealed record LocalAdminPrincipalObservation(
    string? Name,
    string? Sid,
    string Type,
    string Source,
    bool NameResolved);

internal sealed record LocalAdministratorsCollectionResult(
    LocalAdminPolicy Policy,
    IReadOnlyList<LocalAdminMember> Members,
    SecurityControlObservation Control);

internal interface ILocalAdministratorsSource
{
    IReadOnlyList<LocalAdminPrincipalObservation> ReadDirectMembers();
}

internal static class LocalAdministratorsCollector
{
    public static LocalAdministratorsCollectionResult Collect(
        ILocalAdminPolicySource? policySource = null,
        ILocalAdministratorsSource? memberSource = null,
        string? computerName = null)
    {
        var policy = LocalAdministratorsPolicy.Load(policySource);
        memberSource ??= new LocalAdministratorsWindowsSource();
        computerName ??= Environment.MachineName;

        IReadOnlyList<LocalAdminPrincipalObservation> members;
        try { members = memberSource.ReadDirectMembers(); }
        catch (Exception ex)
        {
            var evidence = PolicyEvidence(policy);
            evidence.Add(new("CollectionError", ex.GetType().Name, "NetAPI32"));
            return new(policy, [], new(
                "SEC-LOCAL-ADMINS",
                SecurityControlStatus.Unknown,
                evidence,
                "SEC-LOCAL-ADMINS"));
        }

        return Evaluate(policy, members, computerName);
    }

    internal static LocalAdministratorsCollectionResult Evaluate(
        LocalAdminPolicy policy,
        IReadOnlyList<LocalAdminPrincipalObservation> observations,
        string computerName)
    {
        if (!policy.Configured || !policy.Readable)
        {
            var unresolved = observations.Select(x => new LocalAdminMember(
                NormalizeName(x.Name, x.Source, computerName),
                x.Sid,
                x.Type,
                x.Source,
                "Unknown",
                null,
                policy.Configured ? "PolicyUnreadable" : "PolicyNotConfigured")).ToList();
            return new(policy, unresolved, BuildControl(policy, unresolved, SecurityControlStatus.Unknown));
        }

        var nameRulesExist = policy.Patterns.Any(x => !x.StartsWith("SID:", StringComparison.OrdinalIgnoreCase));
        var members = new List<LocalAdminMember>(observations.Count);

        foreach (var observation in observations)
        {
            var normalizedName = observation.NameResolved
                ? NormalizeName(observation.Name, observation.Source, computerName)
                : null;
            var sidCandidate = string.IsNullOrWhiteSpace(observation.Sid) ? null : "SID:" + observation.Sid.Trim();

            var sidRule = sidCandidate is null ? null : FindMatch(policy.Patterns, sidCandidate);
            if (sidRule is not null)
            {
                members.Add(new(normalizedName, observation.Sid, observation.Type, observation.Source, "Allowed", sidRule, "SidRuleMatched"));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(normalizedName))
            {
                var nameRule = FindMatch(policy.Patterns, normalizedName);
                if (nameRule is not null)
                {
                    members.Add(new(normalizedName, observation.Sid, observation.Type, observation.Source, "Allowed", nameRule, "NameRuleMatched"));
                    continue;
                }

                members.Add(new(normalizedName, observation.Sid, observation.Type, observation.Source, "Unauthorized", null, "NoAllowListRuleMatched"));
                continue;
            }

            if (sidCandidate is not null && !nameRulesExist)
            {
                members.Add(new(null, observation.Sid, observation.Type, observation.Source, "Unauthorized", null, "NoSidRuleMatched"));
                continue;
            }

            members.Add(new(null, observation.Sid, observation.Type, observation.Source, "Unknown", null,
                sidCandidate is null ? "PrincipalIdentityUnavailable" : "NameResolutionRequired"));
        }

        var status = members.Any(x => x.Result == "Unauthorized")
            ? SecurityControlStatus.Fail
            : members.Any(x => x.Result == "Unknown")
                ? SecurityControlStatus.Unknown
                : SecurityControlStatus.Pass;

        return new(policy, members, BuildControl(policy, members, status));
    }

    private static SecurityControlObservation BuildControl(
        LocalAdminPolicy policy,
        IReadOnlyList<LocalAdminMember> members,
        SecurityControlStatus status)
    {
        var evidence = PolicyEvidence(policy);
        foreach (var member in members)
        {
            var principal = member.Name ?? (member.Sid is null ? "Unknown" : "SID:" + member.Sid);
            evidence.Add(new("Member", $"{principal}|{member.Type}|{member.Result}", member.Source));
            if (member.MatchedRule is not null)
                evidence.Add(new("MatchedRule", member.MatchedRule, policy.Source));
        }

        return new(
            "SEC-LOCAL-ADMINS",
            status,
            evidence,
            "SEC-LOCAL-ADMINS");
    }

    private static List<SecurityEvidence> PolicyEvidence(LocalAdminPolicy policy)
    {
        var evidence = new List<SecurityEvidence>
        {
            new("PolicyConfigured", policy.Configured ? "True" : "False", policy.Source),
            new("PolicyReadable", policy.Readable ? "True" : "False", policy.Source)
        };
        foreach (var pattern in policy.Patterns)
            evidence.Add(new("AllowedPattern", pattern, policy.Source));
        return evidence;
    }

    private static string? FindMatch(IEnumerable<string> patterns, string candidate)
        => patterns.FirstOrDefault(x => GlobMatcher.IsMatch(x, candidate));

    private static string? NormalizeName(string? name, string source, string computerName)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var trimmed = name.Trim();
        var separator = trimmed.IndexOf('\\');
        if (separator > 0)
        {
            var prefix = trimmed[..separator];
            var remainder = trimmed[(separator + 1)..];
            if (prefix.Equals(computerName, StringComparison.OrdinalIgnoreCase) || prefix == ".")
                return "LOCAL\\" + remainder;
            return trimmed;
        }

        if (source.Equals("Local", StringComparison.OrdinalIgnoreCase))
            return "LOCAL\\" + trimmed;
        return trimmed;
    }
}

internal sealed class LocalAdministratorsWindowsSource : ILocalAdministratorsSource
{
    internal const string AdministratorsSid = "S-1-5-32-544";
    private const int Level = 2;
    private const int MaxPreferredLength = -1;
    private const int Success = 0;
    private const int ErrorMoreData = 234;

    public IReadOnlyList<LocalAdminPrincipalObservation> ReadDirectMembers()
    {
        var groupName = ResolveAdministratorsGroupName();
        var result = new List<LocalAdminPrincipalObservation>();
        var resume = IntPtr.Zero;

        while (true)
        {
            var status = NetLocalGroupGetMembers(
                null,
                groupName,
                Level,
                out var buffer,
                MaxPreferredLength,
                out var entriesRead,
                out _,
                ref resume);

            try
            {
                if (status != Success && status != ErrorMoreData)
                    throw new Win32Exception(status, "NetLocalGroupGetMembers failed.");

                if (buffer != IntPtr.Zero && entriesRead > 0)
                    ReadEntries(buffer, entriesRead, result);
            }
            finally
            {
                if (buffer != IntPtr.Zero) NetApiBufferFree(buffer);
            }

            if (status != ErrorMoreData) break;
            if (entriesRead == 0) throw new InvalidOperationException("NetLocalGroupGetMembers returned ERROR_MORE_DATA without entries.");
        }

        return result;
    }

    private static string ResolveAdministratorsGroupName()
    {
        var sid = new SecurityIdentifier(AdministratorsSid);
        var account = (NTAccount)sid.Translate(typeof(NTAccount));
        var value = account.Value;
        var separator = value.IndexOf('\\');
        return separator >= 0 ? value[(separator + 1)..] : value;
    }

    private static void ReadEntries(IntPtr buffer, int entriesRead, List<LocalAdminPrincipalObservation> result)
    {
        var size = Marshal.SizeOf<LocalGroupMembersInfo2>();
        for (var i = 0; i < entriesRead; i++)
        {
            var current = IntPtr.Add(buffer, i * size);
            var item = Marshal.PtrToStructure<LocalGroupMembersInfo2>(current);
            var name = item.SidUsageName == IntPtr.Zero ? null : Marshal.PtrToStringUni(item.SidUsageName);
            string? sid = null;
            if (item.Sid != IntPtr.Zero)
            {
                try { sid = new SecurityIdentifier(item.Sid).Value; }
                catch { }
            }

            var type = item.SidUsage switch
            {
                1 => "User",
                2 or 4 or 5 => "Group",
                _ => "Unknown"
            };
            var source = InferSource(name);
            result.Add(new(name, sid, type, source, !string.IsNullOrWhiteSpace(name)));
        }
    }

    private static string InferSource(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Unknown";
        var separator = name.IndexOf('\\');
        if (separator <= 0) return "Unknown";
        var prefix = name[..separator];
        if (prefix.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            || prefix.Equals("BUILTIN", StringComparison.OrdinalIgnoreCase)) return "Local";
        if (prefix.Equals("MicrosoftAccount", StringComparison.OrdinalIgnoreCase)) return "MicrosoftAccount";
        if (prefix.Equals("AzureAD", StringComparison.OrdinalIgnoreCase)) return "AzureAD";
        return "ActiveDirectory";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LocalGroupMembersInfo2
    {
        public IntPtr Sid;
        public int SidUsage;
        public IntPtr SidUsageName;
    }

    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int NetLocalGroupGetMembers(
        string? serverName,
        string localGroupName,
        int level,
        out IntPtr buffer,
        int preferredMaximumLength,
        out int entriesRead,
        out int totalEntries,
        ref IntPtr resumeHandle);

    [DllImport("Netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);
}
