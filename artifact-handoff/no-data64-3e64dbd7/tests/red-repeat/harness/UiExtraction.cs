namespace G.PcHealthCheck;
internal sealed class UiExtraction
{
 internal readonly FakeGrid _securityGrid = new();
 internal bool _isBusy;
 internal List<SymptomNote> _symptomNotes = [];
 internal ScanResult? _current;
 internal readonly ReportService _reports = new();
 internal string _latestReport = "";
 private readonly AssessmentService _assessment = new();
 private object? _applyProgressOwner;
    internal void AddSecurityControlRow(SecurityControlResult control)
    {
        var evidence = control.Evidence.Count == 0
            ? AppLocalization.T("Security.Evidence.None")
            : string.Join("; ", control.Evidence.Select(x => $"{x.Key}={x.Value}"));
        if (control.Explanation is { } explanation)
            evidence += "; " + MissingEvidenceExplanation.Format(explanation, AppLocalization.Language);
        var sources = DistinctSources(control.Evidence);
        var recommendation = control.Status is SecurityControlStatus.Pass or SecurityControlStatus.NotApplicable
            ? "—"
            : SecurityGuidance(control.GuidanceCode);
        var index = _securityGrid.Rows.Add(
            control.Id,
            SecurityStatusText(control.Status),
            SecurityPointsText(control),
            evidence,
            recommendation,
            sources);
        StyleSecurityRow(_securityGrid.Rows[index], control.Status);
    }
    internal void AddBitLockerDetailRows(SecurityPostureAssessment assessment)
    {
        foreach (var control in assessment.Controls.Where(x => x.Id is "SEC-BITLOCKER-OS" or "SEC-BITLOCKER-DATA"))
        {
            var groups = SplitEvidenceGroups(control.Evidence, "Volume");
            foreach (var group in groups)
            {
                var volume = group.FirstOrDefault(x => x.Key == "Volume")?.Value;
                if (string.IsNullOrWhiteSpace(volume)) continue;
                var status = SecurityStatusFromBitLockerEvidence(group);
                var evidence = string.Join("; ", group.Select(x => $"{x.Key}={x.Value}"));
                var index = _securityGrid.Rows.Add(
                    control.Id + " · " + volume,
                    SecurityStatusText(status),
                    "—",
                    evidence,
                    status is SecurityControlStatus.Pass or SecurityControlStatus.NotApplicable ? "—" : SecurityGuidance(control.GuidanceCode),
                    DistinctSources(group));
                StyleSecurityRow(_securityGrid.Rows[index], status);
            }
        }
    }
    internal void AddLocalAdministratorDetailRows(LocalAdministratorsCollectionResult? localAdministrators)
    {
        if (localAdministrators is null) return;
        foreach (var member in localAdministrators.Members)
        {
            var status = member.Result switch
            {
                "Allowed" => SecurityControlStatus.Pass,
                "Unauthorized" => SecurityControlStatus.Fail,
                _ => SecurityControlStatus.Unknown
            };
            var identity = member.Name ?? (member.Sid is null ? "Unknown" : "SID:" + member.Sid);
            var facts = new List<string>();
            if (member.Sid is not null) facts.Add("SID=" + member.Sid);
            facts.Add("Type=" + member.Type);
            facts.Add("Result=" + member.Result);
            facts.Add("Reason=" + member.Reason);
            if (member.MatchedRule is not null) facts.Add("MatchedRule=" + member.MatchedRule);
            var index = _securityGrid.Rows.Add(
                "SEC-LOCAL-ADMINS · " + identity,
                SecurityStatusText(status),
                "—",
                string.Join("; ", facts),
                status == SecurityControlStatus.Pass ? "—" : SecurityGuidance("SEC-LOCAL-ADMINS"),
                member.Source);
            StyleSecurityRow(_securityGrid.Rows[index], status);
        }
    }
    internal static IReadOnlyList<IReadOnlyList<SecurityEvidence>> SplitEvidenceGroups(
        IReadOnlyList<SecurityEvidence> evidence,
        string firstKey)
    {
        var result = new List<IReadOnlyList<SecurityEvidence>>();
        List<SecurityEvidence>? current = null;
        foreach (var item in evidence)
        {
            if (item.Key == firstKey)
            {
                if (current is { Count: > 0 }) result.Add(current);
                current = [];
            }
            current?.Add(item);
        }
        if (current is { Count: > 0 }) result.Add(current);
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
    internal static string DistinctSources(IEnumerable<SecurityEvidence> evidence)
        => string.Join(", ", evidence.Select(x => x.Source).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase));
    internal static string SecurityGuidance(string guidanceCode)
    {
        var key = "Security.Guidance." + guidanceCode;
        var localized = AppLocalization.T(key);
        return localized == key ? guidanceCode : localized;
    }
    internal static string SecurityStatusText(SecurityControlStatus status) => status switch
    {
        SecurityControlStatus.Pass => AppLocalization.T("Security.Status.Pass"),
        SecurityControlStatus.Warn => AppLocalization.T("Security.Status.Warn"),
        SecurityControlStatus.Fail => AppLocalization.T("Security.Status.Fail"),
        SecurityControlStatus.NotApplicable => AppLocalization.T("Security.Status.NotApplicable"),
        _ => AppLocalization.T("Security.Status.Unknown")
    };
    internal static string SecurityPointsText(SecurityControlResult result)
    {
        if (result.Status is SecurityControlStatus.Unknown or SecurityControlStatus.NotApplicable) return "—";
        var earned = result.Weight * result.EarnedFraction;
        return $"{earned:0.#}/{result.Weight}";
    }
 internal void SelectSymptom(SymptomRoute route)
 {
        if (_isBusy) return;
        _symptomNotes = SymptomRoutes.Select(_symptomNotes, route.Id);
        if (_current is not null) _current.SymptomNotes = _symptomNotes.ToList();

 }
 internal async Task<VerificationResult> Verify(ScanResult before, DiagnosticData afterData)
 {
 object? verificationOwner = null; IProgress<string>? verificationProgress = null; var batch = new RemediationBatchResult();
            var after = _assessment.Assess(afterData);
            await AttachSecurityPostureAsync(after, verificationProgress);
            if (ReferenceEquals(_applyProgressOwner, verificationOwner)) _applyProgressOwner = null;
            var verification = new VerificationResult { Before = before, After = after, Remediation = batch };
            var saved = _reports.SaveVerification(verification);
            _latestReport = saved.Html;
            _current = after;

 return verification;
 }
 private static Task AttachSecurityPostureAsync(ScanResult after, IProgress<string>? progress) => Task.CompletedTask;
 private static void StyleSecurityRow(object?[] row, SecurityControlStatus status) { }
}
internal sealed class FakeGrid { internal readonly FakeRows Rows = new(); }
internal sealed class FakeRows : List<object?[]> { internal int Add(params object?[] row) { base.Add(row); return Count-1; } }
internal enum MessageBoxButtons { OK }
internal enum MessageBoxIcon { Error, Warning }
internal static class MessageBox { internal static string? Last; internal static void Show(object owner,string message,string title,MessageBoxButtons buttons,MessageBoxIcon icon) { Last=message; } }

