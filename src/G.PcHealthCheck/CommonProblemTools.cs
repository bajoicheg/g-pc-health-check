using System.Diagnostics;

namespace G.PcHealthCheck;

internal static class CommonProblemTools
{
    public static string? UriForTopic(string topic) => topic switch
    {
        "Network" => "ms-settings:network-status",
        "Printing" => "ms-settings:printers",
        "Devices" => "ms-settings:connecteddevices",
        "Storage" => "ms-settings:storagesense",
        "Startup" => "ms-settings:startupapps",
        "Updates" => "ms-settings:windowsupdate",
        "Apps" => "ms-settings:appsfeatures",
        _ => null
    };

    public static void Open(string topic)
    {
        var uri = UriForTopic(topic) ?? throw new ArgumentException("Неизвестный раздел Windows.", nameof(topic));
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }
}

internal static class CommonProblemsMenu
{
    public static void Attach(Form main)
    {
        ArgumentNullException.ThrowIfNull(main);
        if (main.MainMenuStrip is null)
        {
            var menu = new MenuStrip { Dock = DockStyle.Top };
            var common = new ToolStripMenuItem { Name = "CommonProblemsOpen" };
            common.Click += (_, _) =>
            {
                using var dialog = new CommonProblemsForm();
                dialog.ShowDialog(main);
            };
            menu.Items.Add(common);
            var windows = new ToolStripMenuItem { Name = "WindowsTools" };
            foreach (var topic in new[] { "Network", "Printing", "Devices", "Storage", "Startup", "Updates", "Apps" })
            {
                var capturedTopic = topic;
                var item = new ToolStripMenuItem { Name = "WindowsTopic_" + topic };
                item.Click += (_, _) =>
                {
                    try { CommonProblemTools.Open(capturedTopic); }
                    catch (Exception ex) { MessageBox.Show(main, ex.Message, AppLocalization.T("Menu.WindowsTools"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                };
                windows.DropDownItems.Add(item);
            }
            menu.Items.Add(windows);
            main.MainMenuStrip = menu;
            main.Controls.Add(menu);
        }
        AppMenuChrome.Attach(main);
    }
}
