using System.ComponentModel;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class ReadOnlyReviewUiSelfTest
{
    public static int Run()
    {
        var failures = new List<string>(); var count = 0;
        void Test(string name, Action action)
        {
            count++;
            try { action(); }
            catch (Exception ex) { failures.Add(name + ": " + ex.Message); Console.Error.WriteLine("FAIL: " + failures[^1]); }
        }
        var root = Path.Combine(Path.GetTempPath(), "GPcHealthCheck-ReviewUI-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            Test("review menu is idempotent and preserves old entries", () =>
            {
                using var main = new Form(); CommonProblemsMenu.Attach(main); ReadOnlyReviewMenu.Attach(main); ReadOnlyReviewMenu.Attach(main);
                var analysis = main.MainMenuStrip!.Items.Find("ReadOnlyInspections", false).OfType<ToolStripMenuItem>().Single();
                Require(main.MainMenuStrip.Items.Count == 5 && analysis.DropDownItems.Count == 2, "Menu duplicates or removes entries.");
            });
            Test("Temp table sorts sizes numerically", () =>
            {
                using var form = new ReadOnlyReviewForm(true, 3);
                form.DisplaySnapshot(new TempPreviewSnapshot { LargestFiles = [new() { Path = "a", Bytes = 900 }, new() { Path = "b", Bytes = 1100 }] });
                var grid = Grid(form); grid.Sort(grid.Columns["Bytes"], ListSortDirection.Descending);
                Require(Math.Abs(Convert.ToDouble(grid.Rows[0].Cells["Bytes"].Value) - (1100d / 1048576d)) < 1e-12 && grid.ReadOnly, "Size sort is not numeric/read-only.");
            });
            Test("startup grid preserves full command and unknown enabled state", () =>
            {
                using var form = new ReadOnlyReviewForm(false, 3);
                var command = "%APPDATA%\\" + new string('X', 1000);
                form.DisplaySnapshot(new StartupReviewSnapshot { Entries = [new() { Name = "Длинная запись", Command = command }] });
                Require((string)Grid(form).Rows[0].Cells["Command"].Value == command && (string)Grid(form).Rows[0].Cells["State"].Value == "Не определено", "Grid altered evidence.");
            });
            Test("read-only review detail follows current cell after reselection", () =>
            {
                using var form = new ReadOnlyReviewForm(false, 3);
                form.DisplaySnapshot(new StartupReviewSnapshot
                {
                    Entries =
                    [
                        new() { Name = "First synthetic entry", Command = "FIRST_SYNTHETIC_COMMAND", Scope = "First scope", Source = "First source" },
                        new() { Name = "Second synthetic entry", Command = "SECOND_SYNTHETIC_COMMAND", Scope = "Second scope", Source = "Second source" }
                    ]
                });
                var grid = Grid(form);
                var detailField = typeof(ReadOnlyReviewForm).GetField("_detail", BindingFlags.Instance | BindingFlags.NonPublic);
                var renderDetail = typeof(ReadOnlyReviewForm).GetMethod("RenderDetail", BindingFlags.Instance | BindingFlags.NonPublic);
                Require(detailField is not null && renderDetail is not null && grid.Rows.Count >= 2, "Read-only review detail selection boundary missing.");
                var detail = (TextBox)detailField!.GetValue(form)!;
                grid.CurrentCell = grid.Rows[0].Cells[0];
                renderDetail!.Invoke(form, null);
                Require(detail.Text.Contains("FIRST_SYNTHETIC_COMMAND", StringComparison.Ordinal), "First read-only review detail was not established.");
                grid.CurrentCell = grid.Rows[1].Cells[0];
                Application.DoEvents();
                Require(grid.CurrentCell?.RowIndex == 1, "Second read-only review row was not selected.");
                Require(detail.Text.Contains("SECOND_SYNTHETIC_COMMAND", StringComparison.Ordinal), "Read-only review detail pane still shows the previously selected row.");
            });
            Test("snapshot cannot be displayed in wrong mode", () =>
            {
                using var form = new ReadOnlyReviewForm(true, 3);
                try { form.DisplaySnapshot(new StartupReviewSnapshot()); } catch (ArgumentException) { return; }
                throw new InvalidOperationException("Wrong snapshot type accepted.");
            });
            Test("export writes both documents without replacing previous files", () =>
            {
                var snapshot = new StartupReviewSnapshot { Account = "SYNTHETIC" };
                var first = ReviewExport.Save(snapshot, root); var second = ReviewExport.Save(snapshot, root);
                Require(first != second && File.Exists(Path.Combine(first, "report.html")) && File.Exists(Path.Combine(second, "snapshot.json")), "Export overwrote evidence.");
                using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(first, "snapshot.json")));
                Require(json.RootElement.GetProperty("Kind").GetString() == "StartupReview", "Wrong exported type.");
            });
            Test("access-denied subtree yields a partial estimate and test ACL is restored", () =>
            {
                var scanRoot = Path.Combine(root, "denied-test"); Directory.CreateDirectory(scanRoot);
                var denied = Directory.CreateDirectory(Path.Combine(scanRoot, "denied"));
                var original = denied.GetAccessControl().GetSecurityDescriptorBinaryForm();
                var changed = denied.GetAccessControl();
                using var identity = WindowsIdentity.GetCurrent();
                changed.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.ListDirectory, AccessControlType.Deny));
                try
                {
                    denied.SetAccessControl(changed);
                    var result = TempPreviewService.Scan(scanRoot, 3, DateTime.Now);
                    Require(result.State == ReviewCollectionState.Partial && result.Errors > 0 && result.Issues.Count > 0, "Access failure not explicit.");
                }
                finally
                {
                    var restored = new DirectorySecurity();
                    restored.SetSecurityDescriptorBinaryForm(original, AccessControlSections.Access);
                    denied.SetAccessControl(restored);
                }
                Require(!Directory.EnumerateFileSystemEntries(denied.FullName).Any(), "Fixture access was not restored.");
            });
            Test("startup source row limit is explicit", () =>
            {
                var rows = Enumerable.Range(0, 2001).Select(i => new StartupReviewEntry { Name = "item" + i }).ToList();
                var s = StartupReviewService.CollectSources([_ => (new ReviewSource { Name = "large", State = ReviewCollectionState.Complete }, rows)]);
                Require(s.State == ReviewCollectionState.Partial && s.Entries.Count == 2000, "Source limit not enforced.");
            });
            Test("startup refuses silently complete empty provider set", () => Require(StartupReviewService.CollectSources([]).State == ReviewCollectionState.Unavailable, "No providers reported complete."));
        }
        finally
        {
            try { Directory.Delete(root, true); }
            catch (Exception ex) { failures.Add("Synthetic fixture cleanup: " + ex.Message); Console.Error.WriteLine("FAIL: " + failures[^1]); }
        }
        Console.WriteLine($"Read-only review UI/export regression: {count - failures.Count}/{count} passed.");
        return failures.Count == 0 ? 0 : 191;
    }
    private static DataGridView Grid(ReadOnlyReviewForm form) => (DataGridView)typeof(ReadOnlyReviewForm).GetField("_grid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
