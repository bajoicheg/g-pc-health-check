using System.Text.Json;
using System.Text.RegularExpressions;

namespace G.PcHealthCheck;

internal static class ProcessObservationLocalizationSelfTest
{
    private static readonly Regex Cyrillic = new("[А-Яа-яЁё]", RegexOptions.CultureInvariant);

    public static int Run()
    {
        var failures = new List<string>();
        var original = AppLocalization.Language;
        try
        {
            AppLocalization.SetLanguage("en");
            const string rawTarget = "RAW-ПРОЦЕСС";
            const string rawImage = @"C:\Apps\Процесс.exe";
            const string rawMarker = "RAW-СИМПТОМ";
            const string rawWarning = "RAW-ПРЕДУПРЕЖДЕНИЕ";
            var stamp = ProcessObservationSelfTest.Stamp;
            var counters = ProcessObservationSelfTest.Counters with { ImagePath = rawImage };
            var processSample = new ProcessCounterSample(1000, stamp.AddSeconds(1), counters);
            var processReading = new ProcessObservationReading(12.5, 64, 48, 1.5, 0.5, 1000, [rawWarning]);
            var systemReading = new PerformanceReading(20, 50, 10, 1, []);
            var systemSample = new PerformanceSample(1000, 5, stamp.AddSeconds(1), systemReading);
            var system = new PerformanceSessionSnapshot
            {
                Options = new PerformanceSessionOptions(10, 1),
                Outcome = "Completed",
                StartedAt = stamp,
                FinishedAt = stamp.AddSeconds(10),
                ElapsedMs = 10000,
                Samples = [systemSample],
                Markers = [new PerformanceMarker(1000, rawMarker)]
            };
            var snapshot = new ProcessObservationSnapshot
            {
                ComputerName = "SYNTHETIC",
                Target = new ProcessObservationTarget(42, stamp, rawTarget),
                ExecutionContext = new ExecutionContextInfo
                {
                    ProcessAccount = "SYNTHETIC\\user",
                    ProcessSid = "S-1-5-21-SYNTHETIC",
                    SessionId = 1,
                    SessionAccount = "SYNTHETIC\\user",
                    SessionSid = "S-1-5-21-SYNTHETIC"
                },
                System = system,
                Samples = [new ProcessObservationSample(processSample, processReading, systemSample)]
            };

            Check("English Process Observation summary localizes framing and preserves raw evidence", () =>
            {
                var summary = ProcessObservationReport.Summary(snapshot);
                var framing = StripRaw(summary, rawTarget, rawImage, rawMarker, rawWarning);
                Require(!Cyrillic.IsMatch(framing), "English process observation summary still contains Russian framing: " + OneLine(framing));
                Require(summary.Contains(rawTarget, StringComparison.Ordinal) && summary.Contains(rawMarker, StringComparison.Ordinal) && summary.Contains(rawWarning, StringComparison.Ordinal),
                    "Raw process observation summary evidence was translated or removed.");
            });

            Check("English Process Observation HTML localizes framing and preserves raw evidence", () =>
            {
                var html = ProcessObservationReport.Html(snapshot);
                var framing = StripRaw(html, rawTarget, rawImage, rawMarker, rawWarning);
                Require(!Cyrillic.IsMatch(framing), "English process observation HTML still contains Russian framing.");
                Require(html.Contains("lang='en'", StringComparison.OrdinalIgnoreCase) || html.Contains("lang=\"en\"", StringComparison.OrdinalIgnoreCase),
                    "English process observation HTML language metadata is not en.");
                Require(html.Contains(rawTarget, StringComparison.Ordinal) && html.Contains(rawImage, StringComparison.Ordinal) && html.Contains(rawMarker, StringComparison.Ordinal),
                    "Raw process observation HTML evidence was translated or removed.");
            });

            Check("Process Observation JSON keeps stable schema state IDs and raw evidence", () =>
            {
                var json = ProcessObservationReport.Json(snapshot);
                using var parsed = JsonDocument.Parse(json);
                var root = parsed.RootElement;
                Require(root.GetProperty("Target").GetProperty("Name").GetString() == rawTarget, "Raw target name changed in JSON.");
                Require(root.GetProperty("System").GetProperty("Outcome").GetString() == "Completed", "Stable system outcome ID changed with language.");
                Require(root.GetProperty("Samples")[0].GetProperty("Process").GetProperty("Counters").GetProperty("State").GetString() == "Live",
                    "Stable process state ID changed with language.");
                Require(root.GetProperty("Samples")[0].GetProperty("Process").GetProperty("Counters").GetProperty("ImagePath").GetString() == rawImage,
                    "Raw EXE path changed in JSON.");
            });
        }
        finally
        {
            AppLocalization.SetLanguage(original);
        }

        Console.WriteLine($"Process Observation localization self-test: {3 - failures.Count}/3 passed.");
        foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 211;

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
