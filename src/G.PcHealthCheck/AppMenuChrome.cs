namespace G.PcHealthCheck;

internal static class AppMenuChrome
{
    private static readonly string[] WindowsTopics = ["Network", "Printing", "Devices", "Storage", "Startup", "Updates", "Apps"];

    public static void Attach(Form main)
    {
        ArgumentNullException.ThrowIfNull(main);
        var menu = main.MainMenuStrip ?? throw new InvalidOperationException("Main menu must exist before app chrome is attached.");
        if (menu.Items.Find("LanguageMenu", false).Length == 0)
        {
            var language = new ToolStripMenuItem { Name = "LanguageMenu" };
            var ru = new ToolStripMenuItem("🇷🇺 RU") { Name = "LanguageRu" };
            var en = new ToolStripMenuItem("🇬🇧 EN") { Name = "LanguageEn" };
            ru.Click += (_, _) => AppLocalization.SetLanguage("ru");
            en.Click += (_, _) => AppLocalization.SetLanguage("en");
            language.DropDownItems.AddRange([ru, en]);
            menu.Items.Add(language);

            var help = new ToolStripMenuItem { Name = "HelpMenu" };
            var about = new ToolStripMenuItem { Name = "AboutOpen" };
            about.Click += (_, _) =>
            {
                using var dialog = new AboutForm();
                dialog.ShowDialog(main);
            };
            help.DropDownItems.Add(about);
            menu.Items.Add(help);

            EventHandler handler = (_, _) => Refresh(main);
            AppLocalization.CultureChanged += handler;
            main.Disposed += (_, _) => AppLocalization.CultureChanged -= handler;
        }
        Refresh(main);
    }

    public static void Refresh(Form main)
    {
        if (main.IsDisposed || main.MainMenuStrip is not { } menu) return;
        SetText(menu, "CommonProblemsOpen", AppLocalization.T("Menu.CommonProblems"));
        SetText(menu, "WindowsTools", AppLocalization.T("Menu.WindowsTools"));
        SetText(menu, "ReadOnlyInspections", AppLocalization.T("Menu.Analysis"));
        SetText(menu, "LanguageMenu", AppLocalization.T("Menu.Language"));
        SetText(menu, "HelpMenu", AppLocalization.T("Menu.Help"));
        SetText(menu, "AboutOpen", AppLocalization.T("Menu.About"));

        foreach (var topic in WindowsTopics)
            SetText(menu, "WindowsTopic_" + topic, AppLocalization.T("Menu.Windows." + topic));

        var ru = Find(menu, "LanguageRu");
        var en = Find(menu, "LanguageEn");
        if (ru is not null)
        {
            ru.Text = "🇷🇺 RU";
            ru.ToolTipText = AppLocalization.T("Language.Russian");
            ru.Checked = AppLocalization.Language == "ru";
        }
        if (en is not null)
        {
            en.Text = "🇬🇧 EN";
            en.ToolTipText = AppLocalization.T("Language.English");
            en.Checked = AppLocalization.Language == "en";
        }
    }

    private static ToolStripMenuItem? Find(MenuStrip menu, string name)
        => menu.Items.Find(name, true).OfType<ToolStripMenuItem>().SingleOrDefault();

    private static void SetText(MenuStrip menu, string name, string text)
    {
        var item = Find(menu, name);
        if (item is not null) item.Text = text;
    }
}
