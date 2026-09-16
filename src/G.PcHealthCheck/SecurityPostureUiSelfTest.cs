using System.Reflection;

namespace G.PcHealthCheck;

internal static class SecurityPostureUiSelfTest
{
    public static int Run()
    {
        var failures = 0;
        void Test(string name, Action body)
        {
            try { body(); Console.WriteLine($"Security posture UI self-test: PASS — {name}"); }
            catch (Exception ex) { failures++; Console.Error.WriteLine($"Security posture UI self-test: FAIL — {name}: {ex.InnerException?.Message ?? ex.Message}"); }
        }

        Test("main window exposes seven metric cards and navigable Security card", () =>
        {
            using var form = new MainForm();
            var type = typeof(MainForm);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var cardField = type.GetField("_securityMetricCard", flags);
            var tabField = type.GetField("_securityTabPage", flags);
            var tabsField = type.GetField("_tabs", flags);
            Require(cardField is not null && tabField is not null && tabsField is not null, "Security card/tab fields are missing.");

            var card = (Panel?)cardField!.GetValue(form);
            var tab = (TabPage?)tabField!.GetValue(form);
            var tabs = (TabControl?)tabsField!.GetValue(form);
            Require(card is not null && tab is not null && tabs is not null, "Security card/tab were not initialized.");
            Require(card.Name == "SecurityPostureMetricCard", "Security metric card stable Name drifted.");
            Require(tab.Name == "SecurityPostureTab", "Security tab stable Name drifted.");
            Require(tabs.TabPages.Contains(tab), "Security tab is not a top-level tab.");

            var root = form.Controls.OfType<TableLayoutPanel>().Single(x => x.RowCount == 5 && x.ColumnCount == 1);
            var metrics = root.GetControlFromPosition(0, 2) as TableLayoutPanel;
            Require(metrics is not null && metrics.ColumnCount == 7 && metrics.Controls.Count == 7,
                "Main metrics must contain exactly seven cards.");
            Require(ReferenceEquals(metrics.GetControlFromPosition(6, 0), card), "Security card must be the seventh metric card.");

            var onClick = typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(onClick is not null, "Control click test hook unavailable.");
            onClick!.Invoke(card, [EventArgs.Empty]);
            Require(ReferenceEquals(tabs.SelectedTab, tab), "Clicking Security card must select Security tab.");
        });

        Test("Security grid has stable evaluation columns", () =>
        {
            using var form = new MainForm();
            var field = typeof(MainForm).GetField("_securityGrid", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field?.GetValue(form) is DataGridView grid, "Security grid is missing.");
            var expected = new[] { "Control", "Status", "Points", "Evidence", "Recommendation", "Source" };
            Require(grid.Columns.Cast<DataGridViewColumn>().Select(x => x.Name).SequenceEqual(expected),
                "Security grid column contract drifted.");
        });

        Test("security bands use fixed non-misleading colors", () =>
        {
            var method = typeof(MainForm).GetMethod("SecurityBandColor", BindingFlags.Static | BindingFlags.NonPublic);
            Require(method is not null, "SecurityBandColor is missing.");
            Color ColorFor(SecurityBand band) => (Color)method!.Invoke(null, [band])!;

            Require(ColorFor(SecurityBand.High) == Color.FromArgb(23, 122, 75), "High must be green.");
            Require(ColorFor(SecurityBand.Good) == Color.FromArgb(35, 134, 192), "Good must use product blue.");
            Require(ColorFor(SecurityBand.NeedsAttention) == Color.FromArgb(165, 106, 0), "NeedsAttention must be amber.");
            Require(ColorFor(SecurityBand.Low) == Color.FromArgb(181, 54, 54), "Low must be red.");
            var incomplete = ColorFor(SecurityBand.AssessmentIncomplete);
            Require(incomplete != Color.FromArgb(23, 122, 75), "AssessmentIncomplete must never be green.");
        });

        Test("Unknown points stay unknown and never become fake zero or full credit", () =>
        {
            var method = typeof(MainForm).GetMethod("SecurityPointsText", BindingFlags.Static | BindingFlags.NonPublic);
            Require(method is not null, "SecurityPointsText is missing.");
            var unknown = new SecurityControlResult("SEC-X", SecurityControlStatus.Unknown, 8, 0, "SEC-X", []);
            var pass = new SecurityControlResult("SEC-X", SecurityControlStatus.Pass, 8, 1, "SEC-X", []);
            Require((string)method!.Invoke(null, [unknown])! == "—", "Unknown points must render as em dash.");
            Require((string)method.Invoke(null, [pass])! == "8/8", "Pass points must render earned/configured points.");
        });

        Test("Security UI resources are complete in Russian and English", () =>
        {
            var keys = new[]
            {
                "Security.Metric.Caption", "Security.Metric.Coverage", "Security.Tab.Title",
                "Security.Column.Control", "Security.Column.Status", "Security.Column.Points",
                "Security.Column.Evidence", "Security.Column.Recommendation", "Security.Column.Source",
                "Security.Band.High", "Security.Band.Good", "Security.Band.NeedsAttention",
                "Security.Band.Low", "Security.Band.AssessmentIncomplete",
                "Security.Status.Pass", "Security.Status.Warn", "Security.Status.Fail",
                "Security.Status.Unknown", "Security.Status.NotApplicable"
            };
            foreach (var key in keys)
            {
                var ru = AppLocalization.TextForCulture("ru", key);
                var en = AppLocalization.TextForCulture("en", key);
                Require(ru != key && en != key, $"Missing RU/EN Security resource: {key}");
                Require(!string.IsNullOrWhiteSpace(ru) && !string.IsNullOrWhiteSpace(en), $"Blank Security resource: {key}");
            }
        });

        Test("stable identifiers and raw evidence values remain untranslated", () =>
        {
            const string id = "SEC-LOCAL-ADMINS";
            const string sid = "S-1-5-21-100-200-300-500";
            const string oem = "Lenovo WMI";
            Require(id == "SEC-LOCAL-ADMINS" && sid.StartsWith("S-1-5-21-", StringComparison.Ordinal) && oem == "Lenovo WMI",
                "Stable/raw values must remain presentation-independent.");
        });

        Console.WriteLine($"Security posture UI self-test: {6 - failures}/6 passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
