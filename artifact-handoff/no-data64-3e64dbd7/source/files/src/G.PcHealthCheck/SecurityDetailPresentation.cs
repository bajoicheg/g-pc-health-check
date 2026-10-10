namespace G.PcHealthCheck;

public sealed record SecuritySubjectDetail(string ControlId, string Subject, SecurityControlStatus Status,
    IReadOnlyList<SecurityEvidence> Evidence, EvidenceExplanation? Explanation)
{
    public string DisplayEvidence(string language) => string.Join("; ", Evidence.Select(x => $"{x.Key}={x.Value}"))
        + (Explanation is null ? "" : "; " + MissingEvidenceExplanation.Format(Explanation, language));
}

internal static class SecurityDetailPresentation
{
    internal static IReadOnlyList<SecuritySubjectDetail> BitLocker(SecurityControlResult control)
    {
        var result = new List<SecuritySubjectDetail>();
        List<SecurityEvidence>? group = null;
        void Add()
        {
            if (group is null || group.Count == 0 || string.IsNullOrWhiteSpace(group[0].Value)) return;
            var status = SecurityStatusFromBitLockerEvidence(group);
            result.Add(new(control.Id, group[0].Value, status, group.ToArray(),
                status == SecurityControlStatus.Unknown ? MissingEvidenceExplanation.For(group) : null));
        }
        foreach (var item in control.Evidence)
        {
            if (item.Key == "Volume") { Add(); group = []; }
            group?.Add(item);
        }
        Add(); return result;
    }
    internal static IReadOnlyList<SecuritySubjectDetail> LocalAdministrators(LocalAdministratorsCollectionResult collection)
        => collection.Members.Select(member =>
        {
            var status = member.Result switch { "Allowed" => SecurityControlStatus.Pass, "Unauthorized" => SecurityControlStatus.Fail, _ => SecurityControlStatus.Unknown };
            var facts = new List<SecurityEvidence>();
            if (member.Sid is not null) facts.Add(new("SID", member.Sid, member.Source));
            facts.Add(new("Type", member.Type, member.Source));
            facts.Add(new("Result", member.Result, member.Source));
            facts.Add(new("Reason", member.Reason, member.Source));
            if (member.MatchedRule is not null) facts.Add(new("MatchedRule", member.MatchedRule, member.Source));
            facts.Add(new("PolicyConfigured", collection.Policy.Configured.ToString(), collection.Policy.Source));
            facts.Add(new("PolicyReadable", collection.Policy.Readable.ToString(), collection.Policy.Source));
            return new SecuritySubjectDetail("SEC-LOCAL-ADMINS", member.Name ?? (member.Sid is null ? "Unknown" : "SID:" + member.Sid),
                status, facts, status == SecurityControlStatus.Unknown ? MissingEvidenceExplanation.For(facts) : null);
        }).ToArray();
    internal static IReadOnlyList<SecuritySubjectDetail> ForScan(ScanResult scan)
    {
        var result = new List<SecuritySubjectDetail>();
        if (scan.Security is not null)
            foreach (var control in scan.Security.Controls.Where(x => x.Id is "SEC-BITLOCKER-OS" or "SEC-BITLOCKER-DATA"))
                result.AddRange(BitLocker(control));
        if (scan.SecuritySnapshot?.LocalAdministrators is { } members) result.AddRange(LocalAdministrators(members));
        return result;
    }
    internal static SecurityControlStatus SecurityStatusFromBitLockerEvidence(IReadOnlyList<SecurityEvidence> evidence)
    {
        var protection = evidence.FirstOrDefault(x => x.Key == "ProtectionStatus")?.Value;
        var conversion = evidence.FirstOrDefault(x => x.Key == "ConversionStatus")?.Value;
        if (protection is null || conversion is null) return SecurityControlStatus.Unknown;
        if (conversion.Equals("FullyDecrypted", StringComparison.OrdinalIgnoreCase)) return SecurityControlStatus.Fail;
        if (conversion is "EncryptionInProgress" or "EncryptionPaused" or "DecryptionInProgress" or "DecryptionPaused") return SecurityControlStatus.Warn;
        if (conversion.Equals("FullyEncrypted", StringComparison.OrdinalIgnoreCase))
        {
            if (protection.Equals("On", StringComparison.OrdinalIgnoreCase)) return SecurityControlStatus.Pass;
            if (protection.Equals("Off", StringComparison.OrdinalIgnoreCase)) return SecurityControlStatus.Warn;
        }
        return SecurityControlStatus.Unknown;
    }

}
