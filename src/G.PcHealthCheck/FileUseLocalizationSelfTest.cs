using System.Text.Json;
using System.Text.RegularExpressions;

namespace G.PcHealthCheck;

internal static class FileUseLocalizationSelfTest
{
    private static readonly Regex Cyrillic = new("[А-Яа-яЁё]", RegexOptions.CultureInvariant);

    public static int Run()
    {
        var failures = new List<string>();
        var original = AppLocalization.Language;
        try
        {
            AppLocalization.SetLanguage("en");
            const string rawTarget = @"C:\Test\файл.txt";
            const string rawApplication = "RAW-ПРИЛОЖЕНИЕ";
            const string rawService = "RAW-СЛУЖБА";
            const string rawImage = @"C:\Apps\Проверено.exe";
            var row = FileUseSelfTest.Row() with
            {
                ApplicationName = rawApplication,
                ServiceName = rawService,
                ImagePath = rawImage,
                ProcessName = "Проверено.exe",
                IdentityState = "Matched"
            };
            var snapshot = FileUseSelfTest.Snapshot(row);
            snapshot.TargetPath = rawTarget;
            snapshot.Stage = "QueryList";

            Check("English File Use summary localizes framing and preserves raw evidence", () =>
            {
                var summary = FileUseReport.Summary(snapshot, null);
                var framing = StripRaw(summary, rawTarget, rawApplication, rawService, rawImage, "Проверено.exe");
                Require(!Cyrillic.IsMatch(framing), "English File Use summary still contains Russian framing: " + OneLine(framing));
                Require(summary.Contains(rawTarget, StringComparison.Ordinal), "Raw target path was translated or removed.");
            });

            Check("English File Use HTML localizes framing and preserves Restart Manager evidence", () =>
            {
                var html = FileUseReport.Html(snapshot, null);
                var framing = StripRaw(html, rawTarget, rawApplication, rawService, rawImage, "Проверено.exe");
                Require(!Cyrillic.IsMatch(framing), "English File Use HTML still contains Russian framing.");
                Require(html.Contains("lang='en'", StringComparison.OrdinalIgnoreCase) || html.Contains("lang=\"en\"", StringComparison.OrdinalIgnoreCase),
                    "English File Use HTML language metadata is not en.");
                Require(html.Contains(rawApplication, StringComparison.Ordinal) && html.Contains(rawService, StringComparison.Ordinal) && html.Contains("Проверено.exe", StringComparison.Ordinal),
                    "Raw Restart Manager/process evidence was translated or removed.");
            });

            Check("File Use JSON keeps stable schema and raw evidence", () =>
            {
                var json = FileUseReport.Json(snapshot, null);
                using var parsed = JsonDocument.Parse(json);
                var current = parsed.RootElement.GetProperty("Current");
                Require(current.GetProperty("TargetPath").GetString() == rawTarget, "Raw File Use target changed in JSON.");
                Require(current.GetProperty("State").GetString() == "Complete", "Stable File Use state ID changed with language.");
                Require(current.GetProperty("Processes")[0].GetProperty("ApplicationName").GetString() == rawApplication, "Raw RM application changed in JSON.");
                Require(current.GetProperty("Processes")[0].GetProperty("ServiceName").GetString() == rawService, "Raw RM service changed in JSON.");
            });
        }
        finally
        {
            AppLocalization.SetLanguage(original);
        }

        Console.WriteLine($"File Use localization self-test: {3 - failures.Count}/3 passed.");
        foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 212;

        void Check(string name, Action action)
        {
            try { action(); }
            catch (Exception ex) { failures.Add(name + ": " + ex.GetBaseException().Message); }
        }
    }

    private static string StripRaw(string text, params string[] values)
    {
        foreach (var value in values) text = text.Replace(value, "", StringComparison.Ordinal);
        return text;
    }

    private static string OneLine(string text)
        => Regex.Replace(text, "\\s+", " ").Trim() is var one && one.Length > 220 ? one[..220] + "…" : one;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
