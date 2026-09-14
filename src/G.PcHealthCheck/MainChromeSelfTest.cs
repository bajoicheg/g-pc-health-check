using System.Reflection;
using System.Text;

namespace G.PcHealthCheck;

internal static class MainChromeSelfTest
{
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
                var message = name + ": " + ex.GetBaseException().Message;
                failures.Add(message);
                Console.Error.WriteLine("FAIL: " + message);
            }
        }

        var originalLanguage = AppLocalization.Language;
        try
        {
            Test("main menu exposes idempotent RU/EN and About controls", () =>
            {
                using var main = new MainForm();
                CommonProblemsMenu.Attach(main);
                ReadOnlyReviewMenu.Attach(main);
                CommonProblemsMenu.Attach(main);
                var menu = main.MainMenuStrip ?? throw new InvalidOperationException("Main menu is missing.");
                Require(menu.Items.Find("LanguageRu", true).Length == 1, "RU language control missing or duplicated.");
                Require(menu.Items.Find("LanguageEn", true).Length == 1, "EN language control missing or duplicated.");
                Require(menu.Items.Find("AboutOpen", true).Length == 1, "About menu entry missing or duplicated.");
            });

            Test("language switch updates current main menu without restart", () =>
            {
                using var main = new MainForm();
                CommonProblemsMenu.Attach(main);
                ReadOnlyReviewMenu.Attach(main);
                var menu = main.MainMenuStrip ?? throw new InvalidOperationException("Main menu is missing.");
                var en = menu.Items.Find("LanguageEn", true).OfType<ToolStripMenuItem>().SingleOrDefault()
                    ?? throw new InvalidOperationException("EN switch missing.");
                en.PerformClick();
                Require(AppLocalization.Language == "en", "EN click did not switch the current culture.");
                var analysis = menu.Items.Find("ReadOnlyInspections", false).OfType<ToolStripMenuItem>().Single();
                Require(analysis.Text == "Analysis", "Already-open main Analysis menu was not retranslated.");
                var ru = menu.Items.Find("LanguageRu", true).OfType<ToolStripMenuItem>().Single();
                Require(en.Checked && !ru.Checked, "Selected language is not visibly marked.");
            });

            Test("About shows running version and owner attribution", () =>
            {
                var aboutType = typeof(MainForm).Assembly.GetType("G.PcHealthCheck.AboutForm")
                    ?? throw new InvalidOperationException("AboutForm is missing.");
                using var about = Activator.CreateInstance(aboutType) as Form
                    ?? throw new InvalidOperationException("AboutForm could not be created.");
                var text = AllText(about);
                Require(text.Contains("© 2026 V. Vasilev", StringComparison.Ordinal), "About attribution is missing or not conventional.");
                Require(text.Contains(Application.ProductVersion, StringComparison.OrdinalIgnoreCase), "About does not use the running application version.");
            });

            Test("shield asset blends with card background", () =>
            {
                using var image = BrandAssets.LoadShield() ?? throw new InvalidOperationException("Shield resource is missing.");
                using var bitmap = new Bitmap(image);
                var corners = new[]
                {
                    bitmap.GetPixel(0, 0), bitmap.GetPixel(bitmap.Width - 1, 0),
                    bitmap.GetPixel(0, bitmap.Height - 1), bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1)
                };
                Require(corners.All(x => x.A == 0), "Shield corner pixels are opaque instead of transparent.");
            });
        }
        finally
        {
            AppLocalization.SetLanguage(originalLanguage);
        }

        Console.WriteLine($"Main chrome / About / branding self-test: {count - failures.Count}/{count} passed.");
        return failures.Count == 0 ? 0 : 233;
    }

    private static string AllText(Control root)
    {
        var builder = new StringBuilder();
        void Walk(Control control)
        {
            if (!string.IsNullOrWhiteSpace(control.Text)) builder.AppendLine(control.Text);
            foreach (Control child in control.Controls) Walk(child);
        }
        Walk(root);
        return builder.ToString();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
