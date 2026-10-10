using System.Reflection;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class ReadableLayoutSelfTest
{
    internal static int Run()
    {
        var original = AppLocalization.Language;
        var failures = new List<string>(); int count = 0;
        var images = new List<RenderEvidence>();
        try
        {
            var output = ResolveRenderOutput();
            foreach (var language in new[] { "ru", "en" }) foreach (var area in new[] { new Size(800, 600), new Size(800, 720), new Size(950, 768), new Size(1320, 720), new Size(1320, 1000) })
            {
                count++;
                try
                {
                    AppLocalization.SetLanguage(language);
                    using var form = new MainForm(false);
                    var workArea = new Rectangle(0, 0, area.Width, area.Height);
                    CommonProblemsMenu.Attach(form); ReadOnlyReviewMenu.Attach(form);
                    form.Show(); form.Location = new Point(300, 70); form.Size = area; ReadableWindowLayout.Fit(form, workArea); form.PerformLayout();
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
                    var tabs = Field<TabControl>(form, "_tabs");
                    var findingsPage = tabs.TabPages.Cast<TabPage>().Single(x => Descendants(x).Contains(findings));
                    var actionsPage = tabs.TabPages.Cast<TabPage>().Single(x => Descendants(x).Contains(actions));
                    Require(!ReferenceEquals(findingsPage, actionsPage), "findings and actions require separate tabs");
                    Require(findings.Height >= tabs.DisplayRectangle.Height - 80 * form.DeviceDpi / 96f, "findings table loses vertical space");
                    var findingPane = Descendants(findingsPage).OfType<SplitContainer>().Single(x => x.Name == "FindingDetailsPane");
                    Require(findingPane.Panel2Collapsed, "details must be collapsed initially");
                    Descendants(findingsPage).OfType<Button>().Single(x => x.Name == "FindingDetailsToggle").PerformClick();
                    Require(!findingPane.Panel2Collapsed, "finding details toggle did not open");
                    tabs.SelectedTab = actionsPage; Application.DoEvents();
                    var actionPane = Descendants(actionsPage).OfType<SplitContainer>().Single(x => x.Name == "ActionDetailsPane");
                    Require(actionPane.Panel2Collapsed, "action details must be collapsed initially");
                    Descendants(actionsPage).OfType<Button>().Single(x => x.Name == "ActionDetailsToggle").PerformClick();
                    Require(!actionPane.Panel2Collapsed, "action details toggle did not open");
                    findings.Rows.Add("WARN", "RAM", "Long observation", "85%", string.Concat(Enumerable.Repeat("Copyable detail / подробности\r\n", 40)));
                    actions.Rows.Add(false, "manual", "Inspect", string.Concat(Enumerable.Repeat(language == "ru" ? "Длинная причина действия / " : "Long action reason / ", 80)), false, "low", "verify unchanged", "available");
                    findings.CurrentCell = findings.Rows[0].Cells[2]; actions.CurrentCell = actions.Rows[0].Cells[2];
                    var details = Descendants(form).OfType<TextBox>().Where(x => x.Name is "FindingDetails" or "ActionDetails").ToArray();
                    Require(details.Length == 2 && details.All(x => x.ReadOnly && x.Multiline && x.ScrollBars == ScrollBars.Both), "selectable scrollable details required");
                    var findingDetail = details.Single(x => x.Name == "FindingDetails");
                    Require(findingDetail.Text.Contains("Copyable detail"), "complete selected finding text missing");
                    findingDetail.SelectAll(); Require(findingDetail.SelectedText == findingDetail.Text, "copy selection loses details");
                    Require(details.Single(x => x.Name == "ActionDetails").Text.Contains("verify unchanged"), "hidden action facts missing from details");
                    var footer = Descendants(form).Single(x => x.Name == "MainFooter");
                    void RequireVisible(Control control, string message)
                    {
                        var bounds = new Rectangle(form.PointToClient(control.PointToScreen(Point.Empty)), control.Size);
                        Require(control.Visible && form.ClientRectangle.Contains(bounds), message);
                    }
                    RequireVisible(footer, "footer must remain visible without scrolling");
                    foreach (var button in Descendants(footer).OfType<Button>())
                        RequireVisible(button, "footer button clipped: " + button.Text);
                    var status = Field<Label>(form, "_status");
                    status.Text = string.Concat(Enumerable.Repeat("Collecting diagnostics / сбор данных ", 12));
                    RequireVisible(status, "collection status must remain visible on its own line");
                    foreach (TabPage page in tabs.TabPages)
                    {
                        tabs.SelectedTab = page; Application.DoEvents();
                        RequireVisible(footer, "changing tabs hides footer");
                        RequireVisible(status, "changing tabs hides status");
                    }
                    var viewport = Descendants(form).OfType<Panel>().Single(x => x.Name == "OverviewScrollViewport");
                    tabs.SelectedTab = tabs.TabPages["OverviewTab"]; Application.DoEvents();
                    viewport.AutoScrollPosition = new Point(0, 10000); Application.DoEvents();
                    RequireVisible(footer, "scrolling overview hides footer");
                    RequireVisible(status, "scrolling overview hides status");
                    tabs.SelectedTab = actionsPage; Application.DoEvents();
                    Require(details.All(x => x.Width > 0 && x.Height >= 30), "finding/action details unreachable");
                    var actionDetail = details.Single(x => x.Name == "ActionDetails");
                    actionDetail.SelectAll(); Require(actionDetail.SelectedText == actionDetail.Text && actionDetail.Text.Length > 1000, "long action reason loses copyable text");
                    findings.Rows.Add("WARN", "Other", "SECOND_FINDING", "1", "Second finding detail");
                    actions.Rows.Add(false, "automatic", "SECOND_ACTION", "Second reason", false, "low", "second verify", "available");
                    foreach (var pair in new[] { (Grid: findings, Detail: findingDetail, Marker: "SECOND_FINDING"), (Grid: actions, Detail: actionDetail, Marker: "SECOND_ACTION") })
                    {
                        pair.Grid.CurrentCell = pair.Grid.Rows[1].Cells[2]; Application.DoEvents();
                        Require(pair.Detail.Text.Contains(pair.Marker), "Q1 details lag selected second row");
                        pair.Grid.ClearSelection(); Application.DoEvents();
                        Require(pair.Detail.Text.Length == 0, "Q1 cleared selection retains stale details");
                        pair.Grid.Rows[1].Selected = true; Application.DoEvents();
                        Require(pair.Detail.Text.Contains(pair.Marker), "Q1 same-cell reselection must restore current details");
                        pair.Grid.CurrentCell = null; pair.Grid.CurrentCell = pair.Grid.Rows[0].Cells[2]; Application.DoEvents();
                        Require(!pair.Detail.Text.Contains(pair.Marker), "Q1 first row retains second details");
                    }
                    // Same Unavailable label, but a changed real target scope must refresh copyable details.
                    var user = ExecutionContextSelfTest.User();
                    var context = user with { HasAdministratorToken = true, IsElevated = true };
                    actions.Rows[0].Tag = new ActionRecommendation { Id = "CleanTemp", CanAutomate = true };
                    var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                    var render = typeof(MainForm).GetMethod("RenderExecutionContext", flags)!;
                    var refresh = typeof(MainForm).GetMethod("RefreshActionAvailability", flags)!;
                    render.Invoke(form, [context]); refresh.Invoke(form, null);
                    var availability = ExecutionPolicy.For("CleanTemp", context);
                    Require(availability.State == "Unavailable" && actionDetail.Text.Contains(availability.Reason) && actionDetail.Text.Contains(availability.Scope), "Q2 blocked reason/scope absent from selectable action details");
                    string oldLabel = Convert.ToString(actions.Rows[0].Cells["Availability"].Value) ?? "";
                    var changed = context with { SessionProfile = @"C:\Users\SyntheticSecond" };
                    render.Invoke(form, [changed]); refresh.Invoke(form, null);
                    var newAvailability = ExecutionPolicy.For("CleanTemp", changed);
                    Require(oldLabel == Convert.ToString(actions.Rows[0].Cells["Availability"].Value) && newAvailability.Scope != availability.Scope, "Q2 fixture must retain same state with different scope");
                    Require(actionDetail.Text.Contains(newAvailability.Scope) && !actionDetail.Text.Contains(availability.Scope), "Q2 same-label context change leaves stale scope");
                    // Exercise real queued production helper around a simulated framework rectangle write.
                    bool afterScaling = false;
                    ReadableWindowLayout.AfterScaling(form, () => { ReadableWindowLayout.Fit(form, workArea); afterScaling = true; });
                    form.Bounds = new Rectangle(0, 0, area.Width + 120, area.Height + 120);
                    Require(!afterScaling, "Q4 fit must defer until framework scaling returns");
                    Application.DoEvents();
                    Require(afterScaling && workArea.Contains(form.Bounds), "Q4 post-scaling clamp was overwritten");
                    tabs.SelectedTab = findingsPage; Application.DoEvents();
                    SaveRender(form, language, area, "findings", output, images);
                    tabs.SelectedTab = actionsPage; Application.DoEvents();
                    SaveRender(form, language, area, "actions", output, images);
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
                    var securityGrid = Field<DataGridView>(form, "_securityGrid");
                    string completeEvidence = string.Concat(Enumerable.Repeat("Unknown retained evidence / причина / next step; ", 100));
                    int securityRow = securityGrid.Rows.Add("SEC-TEST", "Unknown", "—", completeEvidence, "Manual verification", "Fixture source");
                    securityGrid.CurrentCell = securityGrid.Rows[securityRow].Cells[3];
                    var securityToggle = Descendants(security).OfType<Button>().Single(x => x.Name == "SecurityDetailsToggle");
                    securityToggle.PerformClick(); Application.DoEvents();
                    var securityDetail = Descendants(security).OfType<TextBox>().Single(x => x.Name == "SecurityDetails");
                    Require(securityGrid.AutoSizeRowsMode == DataGridViewAutoSizeRowsMode.None
                        && securityGrid.Columns.Cast<DataGridViewColumn>().All(x => x.DefaultCellStyle.WrapMode == DataGridViewTriState.False), "security prose must not expand every grid row");
                    Require(securityGrid.Rows[securityRow].Height <= Math.Ceiling(28.0 * form.DeviceDpi / 96) + 2, "security row height is unbounded");
                    Require(securityDetail.Text.Contains(completeEvidence) && securityDetail.ReadOnly && securityDetail.Multiline, "compact grid lost complete selectable evidence");
                    SaveRender(form, language, area, "security", output, images);
                    Console.WriteLine($"Readable layout synthetic render PASS: {language}, area={area.Width}x{area.Height}, actual DPI={form.DeviceDpi}; not Windows11/RDP acceptance");
                    form.Close();
                }
                catch (Exception ex) { failures.Add(ex.GetBaseException().Message); }
            }
            if (output is not null) CompleteRenderEvidence(output, images);
            foreach (var item in new (double? Value, string Expected)[] { (null, "UNKNOWN"), (0, "CRIT"), (8, "CRIT"), (8.1, "WARN"), (15, "WARN"), (15.1, "OK") })
            {
                count++;
                Require(ReadableLayout.MemoryBand(item.Value, 8, 15) == item.Expected, "RAM presentation boundary " + item.Value);
            }
            count++;
            using (var closed = new Form())
            {
                closed.Show(); bool called = false;
                ReadableWindowLayout.AfterScaling(closed, () => called = true);
                closed.Dispose(); Application.DoEvents();
                Require(!called, "Q4 disposed form callback must not run");
            }
            count++;
            using var common = new CommonProblemsForm();
            ReadableWindowLayout.Fit(common, new Rectangle(0, 0, 800, 720));
            Require(common.Width <= 800 && common.Height <= 720 && common.MinimumSize.Width <= 800, "common problems dialog outside narrow/short area");
            using var about = new AboutForm();
            ReadableWindowLayout.Fit(about, new Rectangle(0, 0, 800, 720));
            Require(about.Width <= 800 && about.Height <= 720, "About outside work area");
            using var contextDialog = new ExecutionContextForm(null);
            ReadableWindowLayout.Fit(contextDialog, new Rectangle(0, 0, 800, 720));
            Require(contextDialog.Width <= 800 && contextDialog.Height <= 720 && contextDialog.MinimumSize.Width <= 800, "context dialog outside narrow/short area");
            Require(about.FormBorderStyle == FormBorderStyle.Sizable && about.MinimumSize.Width > 0, "About must resize long localized text");
        }
        catch (Exception ex) { failures.Add(ex.GetBaseException().Message); }
        finally { AppLocalization.SetLanguage(original); }
        foreach (var failure in failures) Console.Error.WriteLine("Readable layout FAIL: " + failure);
        Console.WriteLine($"Readable layout self-test: {count - failures.Count}/{count} passed");
        return failures.Count == 0 ? 0 : 234;
    }
    private sealed record RenderEvidence(string File, string Sha256, string Language, int FixtureWidth, int FixtureHeight, string View, int ActualDpi);
    private static string? ResolveRenderOutput()
    {
        var explicitOutput = Environment.GetEnvironmentVariable("GPC_LAYOUT_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(explicitOutput)) return Path.GetFullPath(explicitOutput);
        if (!string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.Ordinal)) return null;
        var workspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        Require(!string.IsNullOrWhiteSpace(workspace) && Path.IsPathFullyQualified(workspace), "CI evidence requires absolute GITHUB_WORKSPACE");
        var root = Path.GetFullPath(workspace!);
        Require(File.Exists(Path.Combine(root, "src", "G.PcHealthCheck", "G.PcHealthCheck.csproj")), "CI evidence workspace is not this repository");
        // Fixed synthetic self-test subtree only; no collector or user diagnostic output.
        return Path.Combine(root, "artifacts", "final", "readable-layout");
    }
    private static void SaveRender(Form form, string language, Size area, string view, string? output, List<RenderEvidence> images)
    {
        if (output is null) return;
        Directory.CreateDirectory(output);
        string file = $"layout-{language}-{area.Width}x{area.Height}-{view}-dpi{form.DeviceDpi}.png";
        string path = Path.Combine(output, file);
        using (var bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(0, 0,form.Width, form.Height));
            bitmap.Save(path, ImageFormat.Png);
        }
        using var input = File.OpenRead(path);
        images.Add(new(file, Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant(), language, area.Width, area.Height, view, form.DeviceDpi));
    }
    private static void CompleteRenderEvidence(string output, List<RenderEvidence> images)
    {
        // Count this invocation, not stale files from an earlier process/run.
        Require(images.Count == 30 && images.Select(x => x.File).Distinct(StringComparer.Ordinal).Count() == 30, "required 30 current-invocation synthetic renders missing");
        foreach (var image in images)
        {
            using var input = File.OpenRead(Path.Combine(output, image.File));
            Require(Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant() == image.Sha256, "synthetic render missing or hash changed");
        }
        var manifest = new { Schema = "gpc-synthetic-readable-layout/v1", Scope = "Synthetic WinForms self-test only; actual DPI recorded per image; Windows11/RDP/UAC acceptance NOT_RUN", Images = images };
        File.WriteAllText(Path.Combine(output, "sha256.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }
    private static T Field<T>(object obj, string name) => (T)(obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(obj)!);
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
