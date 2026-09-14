using System.Text.Json;
using System.Text.RegularExpressions;

namespace G.PcHealthCheck;

internal static class PerformanceLocalizationSelfTest
{
    private static readonly Regex Cyrillic = new("[А-Яа-яЁё]", RegexOptions.CultureInvariant);

    public static int Run()
    {
        var failures = new List<string>();
        var original = AppLocalization.Language;
        try
        {
            AppLocalization.SetLanguage("en");
            const string rawWarning = "RAW-ПРЕДУПРЕЖДЕНИЕ";
            const string rawMarker = "RAW-МАРКЕР";
            var snapshot = PerformanceSessionSelfTest.Snapshot(10, null, 90);
            snapshot.Warnings.Add(rawWarning);
            snapshot.Markers.Add(new PerformanceMarker(1500, rawMarker));

            Check("English summary localizes framing and preserves raw evidence", () =>
            {
                var summary = PerformanceSessionReport.Summary(snapshot);
                var framing = summary.Replace(rawWarning, "", StringComparison.Ordinal)
                    .Replace(rawMarker, "", StringComparison.Ordinal);
                Require(!Cyrillic.IsMatch(framing), "English performance summary still contains Russian framing: " + OneLine(framing));
                Require(summary.Contains(rawWarning, StringComparison.Ordinal), "Raw warning was translated or removed.");
                Require(summary.Contains(rawMarker, StringComparison.Ordinal), "Raw marker note was translated or removed.");
            });

            Check("English HTML localizes framing and language metadata", () =>
            {
                var html = PerformanceSessionReport.Html(snapshot);
                var framing = html.Replace(rawWarning, "", StringComparison.Ordinal)
                    .Replace(rawMarker, "", StringComparison.Ordinal);
                Require(!Cyrillic.IsMatch(framing), "English performance HTML still contains Russian framing.");
                Require(html.Contains("lang='en'", StringComparison.OrdinalIgnoreCase) || html.Contains("lang=\"en\"", StringComparison.OrdinalIgnoreCase),
                    "English performance HTML language metadata is not en.");
                Require(html.Contains(rawWarning, StringComparison.Ordinal) && html.Contains(rawMarker, StringComparison.Ordinal),
                    "Raw performance HTML evidence was translated or removed.");
            });

            Check("Performance JSON keeps stable schema and raw evidence", () =>
            {
                var json = PerformanceSessionReport.Json(snapshot);
                using var parsed = JsonDocument.Parse(json);
                var root = parsed.RootElement;
                Require(root.TryGetProperty("Snapshot", out var data), "Stable Snapshot JSON key changed with language.");
                Require(data.GetProperty("Warnings")[0].GetString() == rawWarning, "Raw warning JSON evidence was translated.");
                Require(data.GetProperty("Markers")[0].GetProperty("Note").GetString() == rawMarker, "Raw marker JSON evidence was translated.");
                Require(data.GetProperty("Outcome").GetString() == "Completed", "Stable outcome ID changed with language.");
            });
        }
        finally
        {
            AppLocalization.SetLanguage(original);
        }

        Console.WriteLine($"Performance localization self-test: {3 - failures.Count}/3 passed.");
        foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 213;

        void Check(string name, Action action)
        {
            try { action(); }
            catch (Exception ex) { failures.Add(name + ": " + ex.GetBaseException().Message); }
        }
    }

    private static string OneLine(string text)
        => Regex.Replace(text, "\\s+", " ").Trim() is var one && one.Length > 220 ? one[..220] + "…" : one;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
