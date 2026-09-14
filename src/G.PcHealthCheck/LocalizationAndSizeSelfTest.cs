using System.Globalization;
using System.Reflection;

namespace G.PcHealthCheck;

internal static class LocalizationAndSizeSelfTest
{
    public static int Run()
    {
        try
        {
            var assembly = typeof(MainForm).Assembly;
            var localization = assembly.GetType("G.PcHealthCheck.AppLocalization")
                ?? throw new InvalidOperationException("0.16.0 localization foundation is missing.");
            var humanSize = assembly.GetType("G.PcHealthCheck.HumanSize")
                ?? throw new InvalidOperationException("0.16.0 MB presentation foundation is missing.");

            Require((string?)Invoke(localization, "NormalizeLanguage", null) == "ru", "RU must be the default language.");
            Require((string?)Invoke(localization, "NormalizeLanguage", "en-US") == "en", "English culture family normalization failed.");
            Require((string?)Invoke(localization, "TextForCulture", "ru", "Menu.Analysis") == "Анализ", "RU resource lookup failed.");
            Require((string?)Invoke(localization, "TextForCulture", "en", "Menu.Analysis") == "Analysis", "EN resource lookup failed.");

            var oneMb = Convert.ToDouble(Invoke(humanSize, "MegabytesValue", 1_048_576L), CultureInfo.InvariantCulture);
            Require(oneMb == 1d, "Byte-to-MB conversion changed.");
            Require((string?)Invoke(humanSize, "FormatMegabytes", 1_572_864L, CultureInfo.GetCultureInfo("en-US")) == "1.5 MB", "EN MB format is not human-readable.");
            Require((string?)Invoke(humanSize, "FormatMegabytes", null, CultureInfo.GetCultureInfo("ru-RU")) == "—", "Unknown file size was converted to zero.");

            Console.WriteLine("Localization and MB presentation self-test passed: 7/7.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Localization and MB presentation self-test failed: " + ex.GetBaseException().Message);
            return 237;
        }
    }

    private static object? Invoke(Type type, string methodName, params object?[] args)
    {
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(x => x.Name == methodName && x.GetParameters().Length == args.Length)
            .ToList();
        if (methods.Count != 1)
            throw new InvalidOperationException($"Expected exactly one {type.Name}.{methodName} overload for the regression contract.");
        return methods[0].Invoke(null, args);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
