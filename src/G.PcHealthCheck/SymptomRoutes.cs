namespace G.PcHealthCheck;
public sealed record SymptomNote(string SymptomId, string ToolId);
internal sealed record SymptomRoute(string Id, string ToolId, string TitleKey, bool AutoStart = false, bool IsRemediation = false);
internal static class SymptomRoutes
{
    internal static IReadOnlyList<SymptomRoute> All { get; } = Array.AsReadOnly(new[]
    {
        new SymptomRoute("slow", "performance-session", "Evidence.Symptom.Slow"),
        new SymptomRoute("application", "incident-review", "Evidence.Symptom.Application"),
        new SymptomRoute("resource", "resource-probe", "Evidence.Symptom.Resource"),
        new SymptomRoute("printing", "common-problems", "Evidence.Symptom.Printing")
    });
    internal static string Format(IEnumerable<SymptomNote> notes, string language)
        => string.Join(Environment.NewLine, notes.Select(x => All.SingleOrDefault(r => r.Id == x.SymptomId && r.ToolId == x.ToolId)).Where(x => x is not null).Select(x => MissingEvidenceExplanation.Text(language, x!.TitleKey)));
    internal static List<SymptomNote> Select(IEnumerable<SymptomNote> previous, string id)
    {
        var route = All.SingleOrDefault(x => x.Id == id) ?? throw new ArgumentException("Unknown symptom route", nameof(id));
        var result = previous.ToList(); result.Add(new(route.Id, route.ToolId)); return result;
    }
}
