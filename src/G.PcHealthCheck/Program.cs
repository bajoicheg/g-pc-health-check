namespace G.PcHealthCheck;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        AppLocalization.Initialize();
        if (args.Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            var result = SelfTest.Run();
            if (result == 0) result = CommonProblemsRegressionSelfTest.Run();
            if (result == 0) result = CommonProblemsPresentationSelfTest.Run();
            if (result == 0) result = CommonProblemCommandSelfTest.Run();
            if (result == 0) result = CommonProblemsProgressOwnershipSelfTest.Run();
            if (result == 0) result = MainScanProgressOwnershipSelfTest.Run();
            if (result == 0) result = MainApplyProgressOwnershipSelfTest.Run();
            if (result == 0) result = MainCardResizeRedrawSelfTest.Run();
            if (result == 0) result = LocalizationAndSizeSelfTest.Run();
            if (result == 0) result = HumanSizePresentationSelfTest.Run();
            if (result == 0) result = MainChromeSelfTest.Run();
            if (result == 0) result = AnalysisAutoCollectSelfTest.Run();
            if (result == 0) result = ServiceDeskActionRegistrySelfTest.Run();
            if (result == 0) result = ServiceDeskBatchPlannerSelfTest.Run();
            if (result == 0) result = WindowsRepairOperationsSelfTest.Run();
            if (result == 0) result = NetworkRepairSelfTest.Run();
            if (result == 0) result = PhasedWorkerSelfTest.Run();
            if (result == 0) result = PhasedBatchExecutorSelfTest.Run();
            if (result == 0) result = PhasedWorkerEngineSelfTest.Run();
            if (result == 0) result = PhasedWorkerTransportSelfTest.Run();
            if (result == 0) result = SystemDiskSelectionSelfTest.Run();
            if (result == 0) result = PortableElevationSelfTest.Run();
            if (result == 0) result = ReadOnlyReviewSelfTest.Run();
            if (result == 0) result = ReadOnlyReviewUiSelfTest.Run();
            if (result == 0) result = ReadOnlyReviewProgressOwnershipSelfTest.Run();
            if (result == 0)
            {
                var behavior = ResourceProbeSelfTest.Run();
                var integration = ResourceProbeIntegrationSelfTest.Run();
                var cancellation = ResourceCancellationSelfTest.Run();
                var exportFailure = ResourceProbeExportFailureSelfTest.Run();
                result = behavior != 0 ? behavior : integration != 0 ? integration : cancellation != 0 ? cancellation : exportFailure;
            }
            if (result == 0)
            {
                var behavior = IncidentReviewSelfTest.Run();
                var integration = IncidentReviewIntegrationSelfTest.Run();
                var cancellation = IncidentCancelStatusSelfTest.Run();
                result = behavior != 0 ? behavior : integration != 0 ? integration : cancellation;
            }
            if (result == 0)
            {
                var behavior = PerformanceSessionSelfTest.Run();
                var integration = PerformanceSessionUiSelfTest.Run();
                var timing = PerformanceSessionTimingSelfTest.Run();
                result = behavior != 0 ? behavior : integration != 0 ? integration : timing;
            }
            if (result == 0)
            {
                var behavior = StorageReviewSelfTest.Run();
                var integration = StorageReviewIntegrationSelfTest.Run();
                var completion = StorageCompletionSelfTest.Run();
                result = behavior != 0 ? behavior : integration != 0 ? integration : completion;
            }
            if (result == 0)
            {
                var behavior = ExecutionContextSelfTest.Run();
                var integration = ExecutionContextIntegrationSelfTest.Run();
                var actions = ExecutionContextActionSelfTest.Run();
                result = behavior != 0 ? behavior : integration != 0 ? integration : actions;
            }
            if (result == 0)
            {
                var behavior = EndpointReviewSelfTest.Run();
                var integration = EndpointReviewIntegrationSelfTest.Run();
                result = behavior != 0 ? behavior : integration;
            }
            if (result == 0)
            {
                var behavior = FileUseSelfTest.Run();
                var integration = FileUseIntegrationSelfTest.Run();
                var review = FileUseReviewSelfTest.Run();
                var ownership = FileUseProgressOwnershipSelfTest.Run();
                result = behavior != 0 ? behavior : integration != 0 ? integration : review != 0 ? review : ownership;
            }
            if (result == 0)
            {
                var behavior = ProcessObservationSelfTest.Run();
                var integration = ProcessObservationIntegrationSelfTest.Run();
                var completion = ProcessObservationCompletionSelfTest.Run();
                result = behavior != 0 ? behavior : integration != 0 ? integration : completion;
            }
            if (result == 0) result = DiagnosticBundleSelfTest.Run();
            if (result == 0) result = DiagnosticBundleReportSelfTest.Run();
            if (result == 0) result = DiagnosticBundleHealthSelectionSelfTest.Run();
            if (result == 0) result = DiagnosticBundleIntegrationSelfTest.Run();
            if (result == 0) result = DiagnosticBundleSaveFailureSelfTest.Run();
            if (result == 0) result = DiagnosticBundleStaleOptionsSelfTest.Run();
            if (result == 0) result = DiagnosticBundleProgressOwnershipSelfTest.Run();
            if (result == 0) result = DiagnosticBundleAcceptanceSelfTest.Run();
            Environment.Exit(result);
            return;
        }
        if (args.Any(a => string.Equals(a, "--bootstrap-worker", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.Exit(RemediationWorker.RunBootstrap(args));
            return;
        }
        if (args.Any(a => string.Equals(a, "--phased-worker", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.Exit(RemediationWorker.RunPhased(args));
            return;
        }
        if (args.Any(a => string.Equals(a, "--worker", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.Exit(RemediationWorker.Run(args));
            return;
        }
        ApplicationConfiguration.Initialize();
        AnalysisAutoCollect.Install();
        using var main = new MainForm();
        CommonProblemsMenu.Attach(main);
        ReadOnlyReviewMenu.Attach(main);
        ResourceProbeMenu.Attach(main);
        IncidentReviewMenu.Attach(main);
        PerformanceSessionMenu.Attach(main);
        StorageReviewMenu.Attach(main);
        EndpointReviewMenu.Attach(main);
        FileUseMenu.Attach(main);
        DiagnosticBundleMenu.Attach(main);
        AppMenuChrome.Refresh(main);
        Application.Run(main);
    }
}
