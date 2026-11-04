using System.Runtime.InteropServices;

namespace G.PcHealthCheck;

internal static class WindowsSecurityCenterReader
{
    private static readonly Guid WscProductListClsid = new("17072F7B-9ABE-4A74-A261-1EB76B55107A");
    private const uint AntivirusProvider = 0x4;

    public static IReadOnlyList<SecurityCenterProduct> ReadAntivirusProducts()
    {
        var result = new List<SecurityCenterProduct>();
        var type = Type.GetTypeFromCLSID(WscProductListClsid, throwOnError: false)
            ?? throw new PlatformNotSupportedException("Windows Security Center product list is unavailable.");
        object? rawList = null;
        try
        {
            rawList = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Cannot create WSC product list.");
            dynamic list = rawList;
            list.Initialize(AntivirusProvider);
            int count = Convert.ToInt32(list.Count);
            for (var i = 0; i < count; i++)
            {
                object? rawProduct = null;
                try
                {
                    rawProduct = list.Item((uint)i);
                    dynamic product = rawProduct;
                    var name = Convert.ToString(product.ProductName) ?? "";
                    var state = MapProductState(Convert.ToInt32(product.ProductState));
                    var signatures = MapSignatureState(Convert.ToInt32(product.SignatureStatus));
                    string? path = null;
                    try { path = Convert.ToString(product.RemediationPath); } catch { }
                    result.Add(new SecurityCenterProduct(name, "Antivirus", state, signatures, path, "Windows Security Center"));
                }
                finally
                {
                    if (rawProduct is not null && Marshal.IsComObject(rawProduct)) Marshal.FinalReleaseComObject(rawProduct);
                }
            }
        }
        finally
        {
            if (rawList is not null && Marshal.IsComObject(rawList)) Marshal.FinalReleaseComObject(rawList);
        }
        return result;
    }

    private static string MapProductState(int state) => state switch
    {
        0 => "Off",
        1 => "On",
        2 => "Snoozed",
        3 => "Expired",
        _ => "Unknown"
    };

    private static string MapSignatureState(int state) => state switch
    {
        0 => "OutOfDate",
        1 => "UpToDate",
        _ => "Unknown"
    };
}
