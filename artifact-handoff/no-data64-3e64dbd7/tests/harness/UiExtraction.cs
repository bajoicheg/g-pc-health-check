namespace G.PcHealthCheck;
internal sealed class UiExtraction
{
 internal readonly FakeGrid _securityGrid = new();
 internal bool _isBusy = false;
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
            foreach (var detail in SecurityDetailPresentation.BitLocker(control)) AddSecuritySubjectRow(detail, control.GuidanceCode);
    }
    internal void AddLocalAdministratorDetailRows(LocalAdministratorsCollectionResult? localAdministrators)
    {
        if (localAdministrators is not null)
            foreach (var detail in SecurityDetailPresentation.LocalAdministrators(localAdministrators)) AddSecuritySubjectRow(detail, "SEC-LOCAL-ADMINS");
    }
    internal void AddSecuritySubjectRow(SecuritySubjectDetail detail, string guidanceCode)
    {
        var index = _securityGrid.Rows.Add(detail.ControlId + " · " + detail.Subject,
            SecurityStatusText(detail.Status), "—", detail.DisplayEvidence(AppLocalization.Language),
            detail.Status is SecurityControlStatus.Pass or SecurityControlStatus.NotApplicable ? "—" : SecurityGuidance(guidanceCode),
            DistinctSources(detail.Evidence));
        StyleSecurityRow(_securityGrid.Rows[index], detail.Status);
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
        if (_current is null) _symptomNotes = SymptomRoutes.Select(_symptomNotes, route.Id);
        else
        {
            try
            {
                var saved = _reports.SaveSymptomSelection(_current, route.Id);
                _current = saved.Snapshot;
                _symptomNotes = saved.Snapshot.SymptomNotes.ToList();
                _latestReport = saved.Html;
            }
            catch (Exception)
            {
                MessageBox.Show(this, AppLocalization.T("Evidence.Symptom.SaveFailed"), "G PC Health", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

 }
 internal async Task<VerificationResult> Verify(ScanResult before, DiagnosticData afterData)
 {
 object? verificationOwner = null; IProgress<string>? verificationProgress = null; var batch = new RemediationBatchResult();
            var after = _assessment.Assess(afterData);
            after.SymptomNotes = before.SymptomNotes.ToList();
            await AttachSecurityPostureAsync(after, verificationProgress);
            if (ReferenceEquals(_applyProgressOwner, verificationOwner)) _applyProgressOwner = null;
            var verification = new VerificationResult { Before = before, After = after, Remediation = batch };
            var saved = _reports.SaveVerification(verification);
            _latestReport = saved.Html;
            _current = after;

 return verification;
 }
 internal VerificationResult VerifyFullBatch(ScanResult before, DiagnosticData afterData)
 {
 object? verificationOwner = null; var batch = new RemediationBatchResult();
            var after = _assessment.Assess(afterData);
            after.SymptomNotes = before.SymptomNotes.ToList();
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
internal sealed class FakeRows : List<object?[]> { internal new int Add(params object?[] row) { base.Add(row); return Count-1; } }
internal enum MessageBoxButtons { OK }
internal enum MessageBoxIcon { Error, Warning }
internal static class MessageBox { internal static string? Last; internal static void Show(object owner,string message,string title,MessageBoxButtons buttons,MessageBoxIcon icon) { Last=message; } }

