using System.Globalization;

namespace G.PcHealthCheck;

internal static class HumanSize
{
    internal static double MegabytesValue(long bytes) => bytes / 1024d / 1024d;

    internal static string FormatMegabytes(long? bytes, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return bytes is null ? "—" : MegabytesValue(bytes.Value).ToString("N1", culture) + " MB";
    }

    internal static string Megabytes(long? bytes) => FormatMegabytes(bytes, AppLocalization.Culture);
}
