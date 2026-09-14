using System.Globalization;
using System.Resources;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class AppLocalization
{
    private sealed record Settings(string Language);

    private static readonly ResourceManager[] ResourceManagers =
    [
        new("G.PcHealthCheck.Resources.Strings", typeof(AppLocalization).Assembly),
        new("G.PcHealthCheck.Resources.MainStrings", typeof(AppLocalization).Assembly),
        new("G.PcHealthCheck.Resources.AnalysisStrings", typeof(AppLocalization).Assembly)
    ];
    private static readonly object Sync = new();
    private static CultureInfo _culture = CultureFor(NormalizeLanguage(LoadLanguage()));

    public static CultureInfo Culture => _culture;
    public static string Language => NormalizeLanguage(_culture.Name);
    public static event EventHandler? CultureChanged;

    public static void Initialize() => ApplyCulture(_culture);

    internal static string NormalizeLanguage(string? language)
    {
        if (!string.IsNullOrWhiteSpace(language) && language.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return "en";
        return "ru";
    }

    internal static string TextForCulture(string language, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var culture = CultureFor(NormalizeLanguage(language));
        foreach (var manager in ResourceManagers)
        {
            if (manager.GetString(key, culture) is { } localized) return localized;
        }

        var russian = CultureInfo.GetCultureInfo("ru-RU");
        foreach (var manager in ResourceManagers)
        {
            if (manager.GetString(key, russian) is { } fallback) return fallback;
        }
        return key;
    }

    public static string T(string key, params object?[] args)
    {
        var text = TextForCulture(Language, key);
        return args.Length == 0 ? text : string.Format(Culture, text, args);
    }

    public static void SetLanguage(string language)
    {
        var normalized = NormalizeLanguage(language);
        var culture = CultureFor(normalized);
        lock (Sync)
        {
            _culture = culture;
            ApplyCulture(culture);
            TrySaveLanguage(normalized);
        }
        CultureChanged?.Invoke(null, EventArgs.Empty);
    }

    private static CultureInfo CultureFor(string language)
        => CultureInfo.GetCultureInfo(language == "en" ? "en-US" : "ru-RU");

    private static void ApplyCulture(CultureInfo culture)
    {
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    private static string LoadLanguage()
    {
        try
        {
            var path = SettingsPath();
            if (!File.Exists(path)) return "ru";
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path));
            return NormalizeLanguage(settings?.Language);
        }
        catch
        {
            return "ru";
        }
    }

    private static void TrySaveLanguage(string language)
    {
        try
        {
            var path = SettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Settings(language)));
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            // Language persistence is optional. Diagnostics must keep working when the profile is read-only.
        }
    }

    private static string SettingsPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "G", "G PC Health Check", "settings.json");
}
