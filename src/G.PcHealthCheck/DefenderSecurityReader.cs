using System.Management;

namespace G.PcHealthCheck;

internal static class DefenderSecurityReader
{
    public static DefenderSecurityObservation Read()
    {
        using var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Defender", "SELECT * FROM MSFT_MpComputerStatus");
        using var item = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
        if (item is null) return new(null, null, null, null, null, null, null, "MSFT_MpComputerStatus");
        return new(
            Bool(item, "AntivirusEnabled"),
            Bool(item, "RealTimeProtectionEnabled"),
            Bool(item, "BehaviorMonitorEnabled"),
            Bool(item, "IoavProtectionEnabled"),
            Bool(item, "IsTamperProtected"),
            Int(item, "AntivirusSignatureAge"),
            Text(item, "AMProductVersion") ?? Text(item, "AMEngineVersion"),
            "MSFT_MpComputerStatus");
    }

    private static object? Value(ManagementObject item, string property)
    {
        try { return item.Properties[property]?.Value; } catch { return null; }
    }

    private static bool? Bool(ManagementObject item, string property)
    {
        var value = Value(item, property);
        if (value is null) return null;
        try { return Convert.ToBoolean(value); } catch { return null; }
    }

    private static int? Int(ManagementObject item, string property)
    {
        var value = Value(item, property);
        if (value is null) return null;
        try { return Convert.ToInt32(value); } catch { return null; }
    }

    private static string? Text(ManagementObject item, string property)
        => Convert.ToString(Value(item, property));
}
