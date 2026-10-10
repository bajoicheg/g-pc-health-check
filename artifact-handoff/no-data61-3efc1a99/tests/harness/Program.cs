using G.PcHealthCheck;
int a=EvidencePresentationSelfTest.Run();int b=SecurityPostureEvaluatorSelfTest.Run();int c=TriageRegressionSelfTest.Run();Console.WriteLine($"Regression exits: evidence={a}, security={b}, triage={c}");return a+b+c;
