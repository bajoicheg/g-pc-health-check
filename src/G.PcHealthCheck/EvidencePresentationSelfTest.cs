using System.Text.Json;
namespace G.PcHealthCheck;
internal static class EvidencePresentationSelfTest
{
 public static int Run()
 {
  int failed=0,total=0;
  void Test(string name,Action body){total++;try{body();Console.WriteLine("Evidence PASS: "+name);}catch(Exception ex){failed++;Console.Error.WriteLine("Evidence FAIL: "+name+": "+ex.Message);}}
  void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
  EvidenceExplanation Explain(params SecurityEvidence[] e)=>MissingEvidenceExplanation.For(e);
  Test("explicit denial, timeout and cancellation use exact retained tokens",()=>{
   foreach(var pair in new[]{("UnauthorizedAccessException","access_denied"),("TimeoutException","timeout"),("OperationCanceledException","cancelled")})
    Require(Explain(new SecurityEvidence("CollectionError",pair.Item1,"WindowsUpdate")).ReasonCode==pair.Item2,pair.Item1);
  });
  Test("generic exception, null and empty never invent rights or unsupported OEM",()=>{
   Require(Explain().ReasonCode=="cause_not_established","empty cause");
   Require(Explain(new SecurityEvidence("AdminPasswordSet","Unknown","SMBIOS Type24")).ReasonCode=="cause_not_established","null BIOS is not unsupported or password absent");
   Require(Explain(new SecurityEvidence("CollectionError","COMException password=secret","WMI")).ReasonCode=="cause_not_established","generic COM reason");
   Require(!MissingEvidenceExplanation.Format(Explain(new SecurityEvidence("CollectionError","password=secret","WMI")),"en").Contains("secret"),"raw exception copied");
  });
  Test("explicit unsupported and missing observation are distinguishable",()=>{
   Require(Explain(new SecurityEvidence("CollectionState","UnsupportedManufacturer","Firmware")).ReasonCode=="unsupported","explicit OEM");
   Require(Explain(new SecurityEvidence("CollectionState","MissingObservation","TPM")).ReasonCode=="not_collected","missing observation");
  });
  Test("missing local admin policy differs from unreadable or wrong type",()=>{
   Require(Explain(new SecurityEvidence("PolicyConfigured","False","HKLM machine policy")).ReasonCode=="policy_missing","missing policy");
   Require(Explain(new SecurityEvidence("PolicyConfigured","True","HKLM machine policy"),new("PolicyReadable","False","HKLM machine policy")).ReasonCode=="policy_unreadable","wrong type/unreadable is not access denial");
  });
  Test("explicit conflict, but grouped volume differences not guessed conflict",()=>{
   Require(Explain(new SecurityEvidence("CollectionState","ConflictingEvidence","A/B")).ReasonCode=="conflicting_evidence","conflict marker");
   Require(Explain(new SecurityEvidence("ProtectionStatus","On","Volume A"),new("ProtectionStatus","Off","Volume B")).ReasonCode=="cause_not_established","different subjects not conflict");
  });
  Test("source and safe nextstep localized without secret source",()=>{
   var e=Explain(new SecurityEvidence("CollectionError","TimeoutException","https://user:pass@example.test/query?token=private"));
   var en=MissingEvidenceExplanation.Format(e,"en");var ru=MissingEvidenceExplanation.Format(e,"ru");
   Require(en.Contains("Next step")&&ru.Contains("Следующий шаг"),"localized guidance missing");
   Require(!en.Contains("pass@")&&!en.Contains("private"),"source credentials exposed");
   Require(MissingEvidenceExplanation.Format(Explain(),"en").Contains("not recorded"),"unrecorded source invented");
  });
  Test("incomplete triage not all-clear and preserves assessment",()=>{
   var scan=new ScanResult{Assessment=new Assessment{Score=100,Status="OK",CoveragePercent=40,MissingSignals=["CPU"]}};
   var t=TriageSummary.Build(scan);Require(t.Title!="Проблем не обнаружено","false all-clear");
   Require(t.Detail.Contains("40%"),"actual coverage absent");
   Require(scan.Assessment.Score==100&&scan.Assessment.Status=="OK"&&scan.Assessment.CoveragePercent==40,"diagnostic semantics changed");
   Require(TechnicalEvidence.Format(scan.Assessment,"en").Contains("CPU")&&TechnicalEvidence.Format(scan.Assessment,"en").Contains("Next step"),"technical explanation absent");
  });
  Test("healthy empty differs from collection warning and CRIT order retained",()=>{
   var healthy=new ScanResult{Assessment=new Assessment{Score=100}};var before=TriageSummary.Build(healthy).Title;
   healthy.Data.CollectionWarnings.Add("Synthetic collection failed");Require(TriageSummary.Build(healthy).Title!=before,"failed collection all-clear");
   healthy.Assessment.Findings.Add(new Finding{Severity="WARN",Title="warning",Penalty=99});healthy.Assessment.Findings.Add(new Finding{Severity="CRIT",Title="critical",Penalty=1});
   Require(TriageSummary.SignificantFindings(healthy)[0].Title=="critical","CRIT ordering changed");
  });
  Test("JSON, HTML and clipboard carry explanation and original evidence",()=>{
   var evidence=new List<SecurityEvidence>{new("CollectionError","TimeoutException","WindowsUpdate")};
   var c=new SecurityControlResult("SEC-OS-UPDATES",SecurityControlStatus.Unknown,10,0,"SEC-OS-UPDATES",evidence);
   var scan=new ScanResult{Security=new SecurityPostureAssessment{Controls=[c]}};
   var technical = JsonSerializer.Serialize(new Assessment { MissingSignals = ["CPU"] });
   Require(technical.Contains("MissingSignalExplanations") && technical.Contains("cause_not_established"), "technical JSON metadata missing");
   var json=JsonSerializer.Serialize(c);Require(json.Contains("Explanation")&&json.Contains("timeout")&&json.Contains("TimeoutException"),"JSON metadata/raw evidence absent");
   Require(SecurityReportSection.BuildHtml(scan,"en").Contains("Next step"),"HTML explanation absent");
   Require(SecurityReportSection.BuildClipboardSummary(scan,"en").Contains("WindowsUpdate")&&SecurityReportSection.BuildClipboardSummary(scan,"en").Contains("Next step"),"clipboard explanation/raw source absent");
   Require(c.Status==SecurityControlStatus.Unknown&&c.Weight==10&&c.EarnedFraction==0&&c.Evidence==evidence,"raw/scoring changed");
  });
  Test("symptom routes only existing inspections, no auto probes, batch unchanged",()=>{
   Require(SymptomRoutes.All.Count==4,"four routes required");
   var expected=new[]{"performance-session","incident-review","resource-probe","common-problems"};Require(SymptomRoutes.All.Select(x=>x.ToolId).SequenceEqual(expected),"wrong routes");
   var notes=new List<SymptomNote>();var next=SymptomRoutes.Select(notes,"resource");Require(notes.Count==0&&next.Count==1&&next[0].SymptomId=="resource","selection mutated prior state");
   Require(SymptomRoutes.All.All(x=>!x.AutoStart&&!x.IsRemediation),"active probe/remediation");
   bool rejected=false;try{SymptomRoutes.Select(notes,"CleanTemp");}catch(ArgumentException){rejected=true;}Require(rejected,"remediation accepted as symptom");
  });
  Console.WriteLine($"Evidence presentation: {total-failed}/{total} passed");return failed==0?0:1;
 }
}
