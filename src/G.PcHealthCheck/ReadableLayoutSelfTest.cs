using System.Reflection;
using System.Drawing.Imaging;

namespace G.PcHealthCheck;

internal static class ReadableLayoutSelfTest
{
    internal static int Run()
    {
        var original = AppLocalization.Language;
        var failures = new List<string>(); int count = 0;
        try
        {
            foreach (var language in new[] { "ru", "en" }) foreach (int width in new[] { 950, 1320 })
            {
                count++;
                try
                {
                    AppLocalization.SetLanguage(language);
                    using var form = new MainForm(false) { Width = width, Height = 1000 };
                    form.Show(); form.PerformLayout();
                    var metrics = Descendants(form).OfType<TableLayoutPanel>().Single(x => x.Name == "AdaptiveMetrics");
                    var cards = metrics.Controls.OfType<Panel>().ToArray();
                    Require(cards.Length == 7, "seven cards required");
                    foreach (var card in cards)
                    {
                        Require(metrics.ClientRectangle.Contains(card.Bounds), "metric is clipped: " + card.Name);
                        Require(card.Width >= 150 * form.DeviceDpi / 96f, "metric unreadable width");
                        Require(card.Controls[2].Height >= 30 * form.DeviceDpi / 96f, "coverage detail clipped");
                    }
                    var findings = Field<DataGridView>(form, "_findings"); var actions = Field<DataGridView>(form, "_actions");
                    var split = Descendants(form).OfType<SplitContainer>().Single(x => x.Name == "RecommendationsSplit");
                    Require(split.Panel1.Contains(findings) || Descendants(split.Panel1).Contains(findings), "findings must be primary upper pane");
                    Require(split.Panel1.Height > split.Panel2.Height, "findings require primary space");
                    findings.Rows.Add("WARN", "RAM", "Long observation", "85%", string.Concat(Enumerable.Repeat("Copyable detail / подробности\r\n", 40)));
                    actions.Rows.Add(false, "manual", "Inspect", "reason", false, "low", "verify unchanged", "available");
                    findings.CurrentCell = findings.Rows[0].Cells[2]; actions.CurrentCell = actions.Rows[0].Cells[2];
                    var details = Descendants(form).OfType<TextBox>().Where(x => x.Name is "FindingDetails" or "ActionDetails").ToArray();
                    Require(details.Length == 2 && details.All(x => x.ReadOnly && x.Multiline && x.ScrollBars == ScrollBars.Both), "selectable scrollable details required");
                    var findingDetail = details.Single(x => x.Name == "FindingDetails");
                    Require(findingDetail.Text.Contains("Copyable detail"), "complete selected finding text missing");
                    findingDetail.SelectAll(); Require(findingDetail.SelectedText == findingDetail.Text, "copy selection loses details");
                    Require(details.Single(x => x.Name == "ActionDetails").Text.Contains("verify unchanged"), "hidden action facts missing from details");
                    var output = Environment.GetEnvironmentVariable("GPC_LAYOUT_EVIDENCE_DIR");
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        Directory.CreateDirectory(output);
                        using var bitmap = new Bitmap(form.Width, form.Height);
                        form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                        bitmap.Save(Path.Combine(output, $"layout-{language}-{width}-dpi{form.DeviceDpi}.png"), ImageFormat.Png);
                    }
                    Console.WriteLine($"Readable layout synthetic render PASS: {language}, width={width}, actual DPI={form.DeviceDpi}; not Windows11/RDP acceptance");
                    form.Close();
                }
                catch (Exception ex) { failures.Add(ex.GetBaseException().Message); }
            }
            count++;
            using var about = new AboutForm();
            Require(about.FormBorderStyle == FormBorderStyle.Sizable && about.MinimumSize.Width > 0, "About must resize long localized text");
        }
        catch (Exception ex) { failures.Add(ex.GetBaseException().Message); }
        finally { AppLocalization.SetLanguage(original); }
        foreach (var failure in failures) Console.Error.WriteLine("Readable layout FAIL: " + failure);
        Console.WriteLine($"Readable layout self-test: {count - failures.Count}/{count} passed");
        return failures.Count == 0 ? 0 : 234;
    }
    private static T Field<T>(object obj, string name) => (T)(obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(obj)!);
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
