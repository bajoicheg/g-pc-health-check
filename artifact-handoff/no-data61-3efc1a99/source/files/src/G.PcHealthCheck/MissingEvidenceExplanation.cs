namespace G.PcHealthCheck;

public sealed record EvidenceExplanation(string ReasonCode, IReadOnlyList<string> Sources)
{
    public string Reason => MissingEvidenceExplanation.Reason(ReasonCode, AppLocalization.Language);
    public string NextStep => MissingEvidenceExplanation.NextStep(ReasonCode, AppLocalization.Language);
}

internal static class MissingEvidenceExplanation
{
    internal static EvidenceExplanation For(IEnumerable<SecurityEvidence> evidence)
    {
        var items = evidence.ToArray();
        bool Has(string key, string value) => items.Any(x => x.Key == key && x.Value == value);
        string code = "cause_not_established";
        // Only explicit retained collector states establish a cause. Values from
        // different volumes/subjects never establish a conflict by themselves.
        foreach (var item in items.Where(x => x.Key is "CollectionError" or "CollectionState"))
        {
            code = item.Value switch
            {
                "UnauthorizedAccessException" or "SecurityException" => "access_denied",
                "TimeoutException" => "timeout",
                "OperationCanceledException" or "TaskCanceledException" => "cancelled",
                "MissingObservation" => "not_collected",
                "UnsupportedManufacturer" or "UnsupportedField" => "unsupported",
                "ConflictingEvidence" or "ConflictingSources" => "conflicting_evidence",
                _ => code
            };
        }
        if (code == "cause_not_established")
        {
            if (Has("PolicyConfigured", "False")) code = "policy_missing";
            else if (Has("PolicyConfigured", "True") && Has("PolicyReadable", "False")) code = "policy_unreadable";
        }
        return new(code, items.Select(x => SafeSource(x.Source)).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string SafeSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return "";
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            return uri.GetLeftPart(UriPartial.Authority).Replace(uri.UserInfo.Length > 0 ? uri.UserInfo + "@" : "\0", "") + uri.AbsolutePath;
        // Provider identity is useful; free-form credential-bearing text is not.
        if (source.Contains('=') || source.Contains('@') || source.Any(char.IsControl)) return "[source redacted]";
        return source.Length <= 200 ? source : source[..200];
    }

    internal static string Reason(string code, string language) => Text(language, "Evidence.Reason." + code);
    internal static string NextStep(string code, string language) => Text(language, "Evidence.Next." + code);
    internal static string Format(EvidenceExplanation explanation, string language)
        => Text(language, "Evidence.Reason.Label") + ": " + Reason(explanation.ReasonCode, language)
            + "; " + Text(language, "Evidence.Source.Label") + ": "
            + (explanation.Sources.Count == 0 ? Text(language, "Evidence.Source.NotRecorded") : string.Join(", ", explanation.Sources))
            + "; " + Text(language, "Evidence.Next.Label") + ": " + NextStep(explanation.ReasonCode, language);
    internal static string Text(string language, string key) => AppLocalization.TextForCulture(AppLocalization.NormalizeLanguage(language), key);
}

public sealed record TechnicalSignalExplanation(string Signal, EvidenceExplanation Explanation);
internal static class TechnicalEvidence
{
    internal static IReadOnlyList<TechnicalSignalExplanation> Explain(Assessment assessment)
        => assessment.MissingSignals.Select(x => new TechnicalSignalExplanation(x, new EvidenceExplanation("cause_not_established", []))).ToArray();
    internal static string Format(Assessment assessment, string language)
        => string.Join(Environment.NewLine, Explain(assessment).Select(x => x.Signal + ": " + MissingEvidenceExplanation.Format(x.Explanation, language)));
}
