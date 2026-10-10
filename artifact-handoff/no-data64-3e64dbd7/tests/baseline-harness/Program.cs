using G.PcHealthCheck;
{
 int a=EvidencePresentationSelfTest.Run();int b=SecurityPostureEvaluatorSelfTest.Run();int c=FirmwareSecurityCollectorSelfTest.Run();int d=BitLockerSecurityCollectorSelfTest.Run();int e=GuidanceRegressionSelfTest.Run();int f=TriageRegressionSelfTest.Run();
 Console.WriteLine($"Regression exits evidence={a}, evaluator={b}, firmware={c}, bitlocker={d}, guidance={e}, triage={f}");return a+b+c+d+e+f;
}

