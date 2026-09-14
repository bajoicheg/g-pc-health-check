using System.Text.Json;
using System.Text.RegularExpressions;

namespace G.PcHealthCheck;

internal static class LocalizationCoverageSelfTest
{
    private static readonly Regex Cyrillic = new("[А-Яа-яЁё]", RegexOptions.CultureInvariant);

    public static int Run()
    {
        var failures = new List<string>();
        var count = 0;
        void Test(string name, Action action)
        {
            count++;
            try { action(); }
            catch (Exception ex)
            {
                failures.Add(name + ": " + ex.GetBaseException().Message);
                Console.Error.WriteLine("FAIL: " + failures[^1]);
            }
        }

        var original = AppLocalization.Language;
        try
        {
            Test("English primary operator UI contains no Russian application labels", () =>
            {
                AppLocalization.SetLanguage("en");
                using var forms = new FormSet();
                var leaks = new List<string>();
                foreach (var form in forms.Forms)
                {
                    foreach (var value in UiStrings(form))
                    {
                        if (Cyrillic.IsMatch(value.Text))
                            leaks.Add($"{form.GetType().Name}/{value.Source}: {OneLine(value.Text)}");
                    }
                }
                Require(leaks.Count == 0,
                    $"English operator UI still exposes {leaks.Count} Russian application-authored strings. First: {string.Join(" | ", leaks.Take(12))}");
            });

            Test("Russian and English UI keep stable form/control names and action IDs", () =>
            {
                AppLocalization.SetLanguage("ru");
                using var ru = new FormSet();
                var ruNames = ru.Forms
                    .Select((form, index) => (Key: $"{index}:{form.GetType().Name}:{form.Name}", Names: NamedControls(form)))
                    .ToDictionary(x => x.Key, x => x.Names, StringComparer.Ordinal);
                var ruIds = ServiceDeskActionRegistry.All.Select(x => x.Id).ToArray();

                AppLocalization.SetLanguage("en");
                using var en = new FormSet();
                var enNames = en.Forms
                    .Select((form, index) => (Key: $"{index}:{form.GetType().Name}:{form.Name}", Names: NamedControls(form)))
                    .ToDictionary(x => x.Key, x => x.Names, StringComparer.Ordinal);
                var enIds = ServiceDeskActionRegistry.All.Select(x => x.Id).ToArray();

                Require(ruNames.Keys.OrderBy(x => x).SequenceEqual(enNames.Keys.OrderBy(x => x)), "Form identity changed with language.");
                foreach (var key in ruNames.Keys)
                    Require(ruNames[key].SetEquals(enNames[key]), "Named control identity changed with language: " + key);
                Require(ruIds.SequenceEqual(enIds, StringComparer.Ordinal), "Stable Service Desk action IDs changed with language.");
            });

            Test("JSON keys and raw provider evidence do not translate in English mode", () =>
            {
                AppLocalization.SetLanguage("en");
                const string rawProvider = "Сырой provider string — не переводить";
                var data = new DiagnosticData
                {
                    SecurityProducts = [new SecurityProductInfo { Name = rawProvider, State = "Raw-State", Path = @"C:\raw\путь.exe" }]
                };
                var json = JsonSerializer.Serialize(data);
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                Require(root.TryGetProperty("SecurityProducts", out var products), "Stable JSON key SecurityProducts changed with language.");
                var item = products[0];
                Require(item.GetProperty("Name").GetString() == rawProvider, "Raw provider evidence was translated.");
                Require(item.GetProperty("Path").GetString() == @"C:\raw\путь.exe", "Raw path evidence was translated.");
                Require(item.TryGetProperty("State", out _) && !item.TryGetProperty("Состояние", out _), "JSON schema keys were localized.");
            });

            Test("English human-readable Resource Probe reports localize framing but preserve raw payload", () =>
            {
                AppLocalization.SetLanguage("en");
                const string rawDetail = "RAW-ПРОВАЙДЕР";
                var snapshot = new ResourceProbeSnapshot
                {
                    Target = new ResourceTarget("example.test", 443),
                    StartedAt = DateTimeOffset.Parse("2026-09-14T10:00:00+00:00"),
                    FinishedAt = DateTimeOffset.Parse("2026-09-14T10:00:01+00:00"),
                    Outcome = "Connected",
                    Options = new ResourceProbeOptions(5000, 3000, 8),
                    Addresses = ["192.0.2.10"]
                };
                snapshot.Steps.Add(new ResourceProbeStep("TCP", "192.0.2.10:443", "Connected", 1.0, "", rawDetail));
                var summary = ResourceProbeReport.Summary(snapshot, null);
                var html = ResourceProbeReport.Html(snapshot, null);
                Require(!Cyrillic.IsMatch(summary.Replace(rawDetail, "", StringComparison.Ordinal)), "English Resource Probe summary still contains Russian framing: " + OneLine(summary));
                Require(!Cyrillic.IsMatch(html.Replace(rawDetail, "", StringComparison.Ordinal)), "English Resource Probe HTML still contains Russian framing.");
                Require(summary.Contains(rawDetail, StringComparison.Ordinal) && html.Contains(rawDetail, StringComparison.Ordinal), "Raw report evidence was translated or removed.");
                Require(html.Contains("lang=\"en\"", StringComparison.OrdinalIgnoreCase), "English HTML report language metadata is not en.");

                var json = ResourceProbeReport.Json(snapshot, null);
                using var parsed = JsonDocument.Parse(json);
                Require(parsed.RootElement.GetProperty("Kind").GetString() == "ResourceDiagnostics", "Stable JSON Kind changed with language.");
                Require(parsed.RootElement.GetProperty("Current").GetProperty("Steps")[0].GetProperty("Detail").GetString() == rawDetail,
                    "Raw Resource Probe JSON evidence was translated.");
            });
        }
        finally
        {
            AppLocalization.SetLanguage(original);
        }

        Console.WriteLine($"Localization coverage self-test: {count - failures.Count}/{count} passed.");
        return failures.Count == 0 ? 0 : 214;
    }

    private sealed class FormSet : IDisposable
    {
        internal List<Form> Forms { get; } =
        [
            new MainForm(),
            new CommonProblemsForm(),
            new ReadOnlyReviewForm(true, 3),
            new ReadOnlyReviewForm(false, 3),
            new ResourceProbeForm(),
            new IncidentReviewForm(true),
            new IncidentReviewForm(false),
            new PerformanceSessionForm(),
            new StorageReviewForm(false),
            new StorageReviewForm(true),
            new ExecutionContextForm(Context()),
            new EndpointReviewForm(),
            new FileUseForm(),
            new ProcessObservationForm(new ProcessObservationTarget(1234, DateTimeOffset.Parse("2026-09-14T10:00:00+00:00"), "synthetic-process")),
            new DiagnosticBundleForm()
        ];

        public void Dispose()
        {
            foreach (var form in Forms) form.Dispose();
        }
    }

    private static IEnumerable<(string Source, string Text)> UiStrings(Form form)
    {
        if (!string.IsNullOrWhiteSpace(form.Text)) yield return ("Form.Text", form.Text);
        foreach (var value in ControlStrings(form.Controls)) yield return value;
        if (form.MainMenuStrip is { } menu)
            foreach (var item in ToolStripStrings(menu.Items)) yield return item;
    }

    private static IEnumerable<(string Source, string Text)> ControlStrings(Control.ControlCollection controls)
    {
        foreach (Control control in controls)
        {
            var id = string.IsNullOrWhiteSpace(control.Name) ? control.GetType().Name : control.Name;
            if (!string.IsNullOrWhiteSpace(control.Text)) yield return (id + ".Text", control.Text);
            if (control is TextBox textBox && !string.IsNullOrWhiteSpace(textBox.PlaceholderText))
                yield return (id + ".PlaceholderText", textBox.PlaceholderText);
            if (control is ComboBox combo)
            {
                for (var i = 0; i < combo.Items.Count; i++)
                    if (combo.Items[i] is string item && !string.IsNullOrWhiteSpace(item)) yield return ($"{id}.Items[{i}]", item);
            }
            if (control is DataGridView grid)
            {
                foreach (DataGridViewColumn column in grid.Columns)
                    if (!string.IsNullOrWhiteSpace(column.HeaderText)) yield return ($"{id}.Columns[{column.Name}]", column.HeaderText);
                foreach (DataGridViewRow row in grid.Rows)
                    foreach (DataGridViewCell cell in row.Cells)
                        if (cell.Value is string cellText && !string.IsNullOrWhiteSpace(cellText)) yield return ($"{id}.Cell", cellText);
            }
            foreach (var nested in ControlStrings(control.Controls)) yield return nested;
        }
    }

    private static IEnumerable<(string Source, string Text)> ToolStripStrings(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            var id = string.IsNullOrWhiteSpace(item.Name) ? item.GetType().Name : item.Name;
            if (!string.IsNullOrWhiteSpace(item.Text)) yield return (id + ".Text", item.Text);
            if (!string.IsNullOrWhiteSpace(item.ToolTipText)) yield return (id + ".ToolTipText", item.ToolTipText);
            if (item is ToolStripDropDownItem dropDown)
                foreach (var nested in ToolStripStrings(dropDown.DropDownItems)) yield return nested;
        }
    }

    private static HashSet<string> NamedControls(Form form)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        void Add(Control.ControlCollection controls)
        {
            foreach (Control control in controls)
            {
                if (!string.IsNullOrWhiteSpace(control.Name)) names.Add(control.GetType().Name + ":" + control.Name);
                Add(control.Controls);
            }
        }
        Add(form.Controls);
        if (form.MainMenuStrip is { } menu)
        {
            void AddItems(ToolStripItemCollection items)
            {
                foreach (ToolStripItem item in items)
                {
                    if (!string.IsNullOrWhiteSpace(item.Name)) names.Add(item.GetType().Name + ":" + item.Name);
                    if (item is ToolStripDropDownItem dropDown) AddItems(dropDown.DropDownItems);
                }
            }
            AddItems(menu.Items);
        }
        return names;
    }

    private static ExecutionContextInfo Context()
        => new()
        {
            SessionId = 1,
            ProcessSid = "S-1-5-21-SYNTHETIC",
            SessionSid = "S-1-5-21-SYNTHETIC",
            ProcessAccount = "SYNTHETIC\\user",
            SessionAccount = "SYNTHETIC\\user",
            ProcessProfile = @"C:\Users\synthetic",
            SessionProfile = @"C:\Users\synthetic",
            ProfileSource = "synthetic",
            IsElevated = false,
            HasAdministratorToken = false,
            AdministratorMember = true,
            ElevationType = 3
        };

    private static string OneLine(string text)
        => Regex.Replace(text, "\\s+", " ").Trim() is var one && one.Length > 180 ? one[..180] + "…" : one;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
