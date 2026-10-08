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
            foreach (var language in new[] { "ru", "en" }) foreach (var area in new[] { new Size(800, 720), new Size(950, 768), new Size(1320, 720), new Size(1320, 1000) })
            {
                count++;
                try
                {
                    AppLocalization.SetLanguage(language);
                    using var form = new MainForm(false);
                    var workArea = new Rectangle(0, 0, area.Width, area.Height);
                    form.Show(); ReadableWindowLayout.Fit(form, workArea); form.PerformLayout();
                    Require(workArea.Contains(form.Bounds), "window exceeds narrow/short available work area");
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
                    Field<TabControl>(form, "_tabs").SelectedTab = Descendants(form).OfType<TabPage>().Single(x => Descendants(x).Contains(findings) && x.Parent == Field<TabControl>(form, "_tabs"));
                    form.PerformLayout();
                    var split = Descendants(form).OfType<SplitContainer>().Single(x => x.Name == "RecommendationsSplit");
                    Require(split.Panel1.Contains(findings) || Descendants(split.Panel1).Contains(findings), "findings must be primary upper pane");
                    Require(split.Panel1.Height > split.Panel2.Height, "findings require primary space");
                    findings.Rows.Add("WARN", "RAM", "Long observation", "85%", string.Concat(Enumerable.Repeat("Copyable detail / подробности\r\n", 40)));
                    actions.Rows.Add(false, "manual", "Inspect", string.Concat(Enumerable.Repeat(language == "ru" ? "Длинная причина действия / " : "Long action reason / ", 80)), false, "low", "verify unchanged", "available");
                    findings.CurrentCell = findings.Rows[0].Cells[2]; actions.CurrentCell = actions.Rows[0].Cells[2];
                    var details = Descendants(form).OfType<TextBox>().Where(x => x.Name is "FindingDetails" or "ActionDetails").ToArray();
                    Require(details.Length == 2 && details.All(x => x.ReadOnly && x.Multiline && x.ScrollBars == ScrollBars.Both), "selectable scrollable details required");
                    var findingDetail = details.Single(x => x.Name == "FindingDetails");
                    Require(findingDetail.Text.Contains("Copyable detail"), "complete selected finding text missing");
                    findingDetail.SelectAll(); Require(findingDetail.SelectedText == findingDetail.Text, "copy selection loses details");
                    Require(details.Single(x => x.Name == "ActionDetails").Text.Contains("verify unchanged"), "hidden action facts missing from details");
                    var viewport = Descendants(form).OfType<Panel>().Single(x => x.Name == "MainScrollViewport");
                    var footer = Descendants(form).Single(x => x.Name == "MainFooter");
                    viewport.ScrollControlIntoView(footer); form.PerformLayout();
                    var bottom = viewport.PointToClient(footer.PointToScreen(new Point(0, footer.Height)));
                    Require(bottom.Y <= viewport.ClientSize.Height && bottom.Y > 0, "bottom controls unreachable in short work area");
                    Require(footer.Controls.OfType<Button>().All(x => footer.ClientRectangle.Contains(x.Bounds)), "wrapped footer buttons clipped");
                    viewport.ScrollControlIntoView(details[0]);
                    Require(details.All(x => x.Width > 0 && x.Height >= 30), "finding/action details unreachable");
                    var actionDetail = details.Single(x => x.Name == "ActionDetails");
                    actionDetail.SelectAll(); Require(actionDetail.SelectedText == actionDetail.Text && actionDetail.Text.Length > 1000, "long action reason loses copyable text");
                    SaveRender(form, language, area, "findings-actions");
                    var security = Descendants(form).OfType<TabPage>().Single(x => x.Name == "SecurityPostureTab");
                    Field<TabControl>(form, "_tabs").SelectedTab = security;
                    foreach (var name in new[] { "SecuritySummary", "SecurityOverrides" })
                    {
                        var box = Descendants(security).OfType<TextBox>().Single(x => x.Name == name);
                        string longValue = string.Concat(Enumerable.Repeat(language == "ru" ? "Покрытие неизвестных источников / исключение \r\n" : "Unknown source coverage / override \r\n", 80));
                        box.Text = longValue; box.SelectAll();
                        Require(box.SelectedText == longValue && box.ReadOnly && box.Multiline && box.WordWrap && box.ScrollBars == ScrollBars.Vertical, "complete security summary/override must wrap, scroll and copy");
                        Require(box.Width > 100 && box.Height >= 30, "security details unavailable at narrow width");
                        box.SelectionStart = box.TextLength; box.ScrollToCaret();
                    }
                    SaveRender(form, language, area, "security");
                    Console.WriteLine($"Readable layout synthetic render PASS: {language}, area={area.Width}x{area.Height}, actual DPI={form.DeviceDpi}; not Windows11/RDP acceptance");
                    form.Close();
                }
                catch (Exception ex) { failures.Add(ex.GetBaseException().Message); }
            }
            foreach (var item in new (double? Value, string Expected)[] { (null, "UNKNOWN"), (0, "CRIT"), (8, "CRIT"), (8.1, "WARN"), (15, "WARN"), (15.1, "OK") })
            {
                count++;
                Require(ReadableLayout.MemoryBand(item.Value, 8, 15) == item.Expected, "RAM presentation boundary " + item.Value);
            }
            count++;
            using var common = new CommonProblemsForm();
            ReadableWindowLayout.Fit(common, new Rectangle(0, 0, 800, 720));
            Require(common.Width <= 800 && common.Height <= 720 && common.MinimumSize.Width <= 800, "common problems dialog outside narrow/short area");
            using var about = new AboutForm();
            ReadableWindowLayout.Fit(about, new Rectangle(0, 0, 800, 720));
            Require(about.Width <= 800 && about.Height <= 720, "About outside work area");
            using var context = new ExecutionContextForm(null);
            ReadableWindowLayout.Fit(context, new Rectangle(0, 0, 800, 720));
            Require(context.Width <= 800 && context.Height <= 720 && context.MinimumSize.Width <= 800, "context dialog outside narrow/short area");
            Require(about.FormBorderStyle == FormBorderStyle.Sizable && about.MinimumSize.Width > 0, "About must resize long localized text");
        }
        catch (Exception ex) { failures.Add(ex.GetBaseException().Message); }
        finally { AppLocalization.SetLanguage(original); }
        foreach (var failure in failures) Console.Error.WriteLine("Readable layout FAIL: " + failure);
        Console.WriteLine($"Readable layout self-test: {count - failures.Count}/{count} passed");
        return failures.Count == 0 ? 0 : 234;
    }
    private static void SaveRender(Form form, string language, Size area, string view)
    {
        var output = Environment.GetEnvironmentVariable("GPC_LAYOUT_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
        bitmap.Save(Path.Combine(output, $"layout-{language}-{area.Width}x{area.Height}-{view}-dpi{form.DeviceDpi}.png"), ImageFormat.Png);
    }
    private static T Field<T>(object obj, string name) => (T)(obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(obj)!);
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
