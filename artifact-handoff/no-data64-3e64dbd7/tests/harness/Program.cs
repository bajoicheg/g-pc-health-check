using G.PcHealthCheck;
if(args.Contains("regressions")) {
 int a=EvidencePresentationSelfTest.Run();int b=SecurityPostureEvaluatorSelfTest.Run();int c=FirmwareSecurityCollectorSelfTest.Run();int d=BitLockerSecurityCollectorSelfTest.Run();int e=GuidanceRegressionSelfTest.Run();int f=TriageRegressionSelfTest.Run();
 Console.WriteLine($"Regression exits evidence={a}, evaluator={b}, firmware={c}, bitlocker={d}, guidance={e}, triage={f}");return a+b+c+d+e+f;
}
return CorrectiveTests.Run();
