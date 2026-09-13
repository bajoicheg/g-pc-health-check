namespace G.PcHealthCheck;

internal sealed record DiagnosticBundleSaveRequest(
    DiagnosticBundleSnapshot Snapshot,
    string ParentDirectory,
    bool CreateZip)
{
    public Task<DiagnosticBundleSaveResult> ExecuteAsync()
    {
        ArgumentNullException.ThrowIfNull(Snapshot);
        if (string.IsNullOrWhiteSpace(ParentDirectory))
            throw new ArgumentException("Parent directory is required.", nameof(ParentDirectory));

        return Task.Run(() => DiagnosticBundleReport.Save(Snapshot, ParentDirectory, CreateZip));
    }
}
