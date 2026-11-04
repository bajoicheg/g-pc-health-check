namespace G.PcHealthCheck;

internal sealed record SecurityPostureCollectionResult(
    SecurityPostureSnapshot Snapshot,
    SecurityPostureAssessment Assessment);

internal interface ISecurityPostureCollectionBackend
{
    Dictionary<string, SecurityControlObservation> CollectWindowsPlatform();
    Dictionary<string, SecurityControlObservation> CollectAntivirus();
    SecurityControlObservation CollectWindowsUpdate();
    Dictionary<string, SecurityControlObservation> CollectBitLocker();
    Dictionary<string, SecurityControlObservation> CollectFirmware(SystemInfo system);
    LocalAdministratorsCollectionResult CollectLocalAdministrators(SystemInfo system);
}

internal static class SecurityPostureCollector
{
    private static readonly string[] PlatformIds =
    [
        "SEC-FIREWALL", "SEC-SECUREBOOT", "SEC-UAC", "SEC-TPM", "SEC-VBS-HVCI"
    ];

    private static readonly string[] AntivirusIds =
    [
        "SEC-AV-ACTIVE", "SEC-AV-PLATFORM", "SEC-AV-DEFINITIONS", "SEC-AV-RTP", "SEC-AV-TAMPER"
    ];

    private static readonly string[] BitLockerIds = ["SEC-BITLOCKER-OS", "SEC-BITLOCKER-DATA"];
    private static readonly string[] FirmwareIds = ["SEC-BIOS-ADMIN-PASSWORD", "SEC-BOOT-RESTRICTIONS"];

    public static Task<SecurityPostureCollectionResult> CollectAsync(
        SystemInfo system,
        CancellationToken cancellationToken,
        IProgress<string>? progress = null,
        ISecurityPostureCollectionBackend? backend = null)
    {
        ArgumentNullException.ThrowIfNull(system);
        cancellationToken.ThrowIfCancellationRequested();
        backend ??= new WindowsSecurityPostureCollectionBackend();
        return Task.Run(() => CollectCore(system, backend, cancellationToken, progress), cancellationToken);
    }

    internal static SecurityPostureCollectionResult Unavailable(string warning)
    {
        var snapshot = new SecurityPostureSnapshot();
        snapshot.CollectionWarnings.Add(warning);
        return new(snapshot, SecurityPostureEvaluator.Evaluate(snapshot));
    }

    private static SecurityPostureCollectionResult CollectCore(
        SystemInfo system,
        ISecurityPostureCollectionBackend backend,
        CancellationToken cancellationToken,
        IProgress<string>? progress)
    {
        var snapshot = new SecurityPostureSnapshot();

        CollectDictionaryStage(
            snapshot, "WindowsPlatform", "ИБ: проверяю платформенную защиту…", PlatformIds,
            backend.CollectWindowsPlatform, cancellationToken, progress);
        CollectDictionaryStage(
            snapshot, "Antivirus", "ИБ: проверяю антивирусную защиту…", AntivirusIds,
            backend.CollectAntivirus, cancellationToken, progress);
        CollectSingleStage(
            snapshot, "WindowsUpdate", "ИБ: проверяю обновления Windows…", "SEC-OS-UPDATES",
            backend.CollectWindowsUpdate, cancellationToken, progress);
        CollectDictionaryStage(
            snapshot, "BitLocker", "ИБ: проверяю шифрование дисков…", BitLockerIds,
            backend.CollectBitLocker, cancellationToken, progress);
        CollectDictionaryStage(
            snapshot, "Firmware", "ИБ: проверяю настройки BIOS/UEFI…", FirmwareIds,
            () => backend.CollectFirmware(system), cancellationToken, progress);
        CollectLocalAdministratorsStage(snapshot, system, backend, cancellationToken, progress);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report("ИБ: рассчитываю Security Posture…");
        return new(snapshot, SecurityPostureEvaluator.Evaluate(snapshot));
    }

    private static void CollectDictionaryStage(
        SecurityPostureSnapshot snapshot,
        string stage,
        string progressText,
        IReadOnlyList<string> expectedIds,
        Func<Dictionary<string, SecurityControlObservation>> collect,
        CancellationToken cancellationToken,
        IProgress<string>? progress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(progressText);
        try
        {
            var observations = collect();
            foreach (var id in expectedIds)
            {
                if (observations.TryGetValue(id, out var observation))
                    snapshot.ControlObservations[id] = observation;
                else
                {
                    snapshot.ControlObservations[id] = Unknown(id, stage, "MissingObservation");
                    snapshot.CollectionWarnings.Add($"{stage}: missing observation {id}");
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            AddStageFailure(snapshot, stage, expectedIds, ex);
        }
    }

    private static void CollectSingleStage(
        SecurityPostureSnapshot snapshot,
        string stage,
        string progressText,
        string id,
        Func<SecurityControlObservation> collect,
        CancellationToken cancellationToken,
        IProgress<string>? progress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(progressText);
        try
        {
            var observation = collect();
            snapshot.ControlObservations[id] = string.Equals(observation.Id, id, StringComparison.Ordinal)
                ? observation
                : Unknown(id, stage, "UnexpectedObservationId");
            if (!string.Equals(observation.Id, id, StringComparison.Ordinal))
                snapshot.CollectionWarnings.Add($"{stage}: unexpected observation {observation.Id}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            AddStageFailure(snapshot, stage, [id], ex);
        }
    }

    private static void CollectLocalAdministratorsStage(
        SecurityPostureSnapshot snapshot,
        SystemInfo system,
        ISecurityPostureCollectionBackend backend,
        CancellationToken cancellationToken,
        IProgress<string>? progress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report("ИБ: проверяю локальных администраторов…");
        try
        {
            var result = backend.CollectLocalAdministrators(system);
            snapshot.LocalAdministrators = result;
            snapshot.ControlObservations["SEC-LOCAL-ADMINS"] = result.Control;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            AddStageFailure(snapshot, "LocalAdministrators", ["SEC-LOCAL-ADMINS"], ex);
        }
    }

    private static void AddStageFailure(
        SecurityPostureSnapshot snapshot,
        string stage,
        IReadOnlyList<string> ids,
        Exception exception)
    {
        snapshot.CollectionWarnings.Add($"{stage}: {exception.GetType().Name}");
        foreach (var id in ids)
            snapshot.ControlObservations[id] = Unknown(id, stage, exception.GetType().Name);
    }

    private static SecurityControlObservation Unknown(string id, string stage, string reason)
        => new(
            id,
            SecurityControlStatus.Unknown,
            [new SecurityEvidence("CollectionState", reason, stage)],
            id);
}

internal sealed class WindowsSecurityPostureCollectionBackend : ISecurityPostureCollectionBackend
{
    public Dictionary<string, SecurityControlObservation> CollectWindowsPlatform()
        => WindowsPlatformSecurityCollector.Collect();

    public Dictionary<string, SecurityControlObservation> CollectAntivirus()
        => AntivirusSecurityCollector.Collect();

    public SecurityControlObservation CollectWindowsUpdate()
        => WindowsUpdateSecurityCollector.Collect();

    public Dictionary<string, SecurityControlObservation> CollectBitLocker()
        => BitLockerSecurityCollector.Collect();

    public Dictionary<string, SecurityControlObservation> CollectFirmware(SystemInfo system)
        => FirmwareSecurityCollector.Collect(system.Manufacturer);

    public LocalAdministratorsCollectionResult CollectLocalAdministrators(SystemInfo system)
        => LocalAdministratorsCollector.Collect(computerName: system.ComputerName);
}
