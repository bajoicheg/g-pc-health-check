namespace G.PcHealthCheck;

public sealed partial class MainForm
{
    private async Task AttachSecurityPostureAsync(
        ScanResult scan,
        IProgress<string>? progress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var security = await SecurityPostureCollector.CollectAsync(
                scan.Data.System,
                cancellationToken,
                progress);
            scan.Security = security.Assessment;
            scan.SecuritySnapshot = security.Snapshot;
            PopulateSecurity(scan);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var unavailable = SecurityPostureCollector.Unavailable("SecurityPosture: " + ex.GetType().Name);
            scan.Security = unavailable.Assessment;
            scan.SecuritySnapshot = unavailable.Snapshot;
            PopulateSecurity(scan);
        }
    }
}
