using System.Text.Json;
namespace G.PcHealthCheck;
internal static class CorrectiveTests
{
 internal static int Run() {
 int failed=0;
 void Test(string name,Action a){try{a();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
 void Require(bool b,string why){if(!b)throw new Exception(why);}
 AppLocalization.SetLanguage("en");
 Test("1 real unsupported manufacturer collector",()=> {
 var o=FirmwareSecurityCollector.Collect("Unlisted OEM",[]).Values.First();
 Require(MissingEvidenceExplanation.For(o.Evidence).ReasonCode=="unsupported","actual UnsupportedManufacturer evidence not classified");
 Require(MissingEvidenceExplanation.For(FirmwareSecurityCollector.Collect("Unknown",[]).Values.First().Evidence).ReasonCode=="cause_not_established","unknown OEM inferred"); });
 Test("2 actual per-volume unknown detail and exports",()=> {
 var c=new SecurityControlResult("SEC-BITLOCKER-DATA",SecurityControlStatus.Fail,10,0,"SEC-BITLOCKER-DATA",[new("Volume","D:","WMI"),new("ProtectionStatus","Unknown","WMI"),new("ConversionStatus","Unknown","WMI")]);
 var scan=new ScanResult{Security=new(){Controls=[c]}}; var ui=new UiExtraction();ui.AddBitLockerDetailRows(scan.Security);
 Require(ui._securityGrid.Rows[0][3]!.ToString()!.Contains("Next step"),"volume detail lacks explanation");
 Require(JsonSerializer.Serialize(scan).Contains("SecurityDetails"),"subject metadata absent from JSON");
 Require(SecurityReportSection.BuildHtml(scan,"en").Contains("D:"),"HTML volume absent");
 Require(SecurityReportSection.BuildClipboardSummary(scan,"en").Contains("D:"),"clipboard volume absent"); });
 Test("2 actual per-member unknown detail",()=> {
 var r=LocalAdministratorsCollector.Evaluate(new(false,true,[],"Policy"),[new("User",null,"User","NetAPI32",true)],"PC");
 var ui=new UiExtraction();ui.AddLocalAdministratorDetailRows(r);
 Require(ui._securityGrid.Rows[0][3]!.ToString()!.Contains("Next step"),"member detail lacks explanation"); });
 Test("3 saved scan symptom HTML/JSON/clipboard coherence",()=> {
 var scan=new ScanResult(); var ui=new UiExtraction{_current=scan}; var old=ui._reports.SaveScan(scan);ui._latestReport=old.Html;var bytes=File.ReadAllBytes(old.Json);
 ui.SelectSymptom(SymptomRoutes.All[0]);
 Require(ui._latestReport!=old.Html,"selected symptom not durably saved");
 Require(File.ReadAllText(Path.ChangeExtension(ui._latestReport,".json")).Contains("\"slow\""),"JSON stale");
 Require(File.ReadAllText(ui._latestReport).Contains(SymptomRoutes.Format(ui._current!.SymptomNotes,"en")),"HTML stale");
 Require(File.ReadAllBytes(old.Json).SequenceEqual(bytes),"history overwritten"); });
 Test("4 actual verification block carries original symptom notes",()=> {
 var before=new ScanResult{SymptomNotes=[new("slow","performance-session")]};var ui=new UiExtraction();
 var v=ui.Verify(before,new()).GetAwaiter().GetResult();
 Require(v.After.SymptomNotes.SequenceEqual(before.SymptomNotes),"verification lost symptom notes");Require(!ReferenceEquals(v.After.SymptomNotes,before.SymptomNotes),"mutable notes shared");
 Require(File.ReadAllText(Path.ChangeExtension(ui._latestReport,".json")).Contains("\"slow\""),"verification JSON missing notes");
 });
 Test("5 throwing policy source never claims configured",()=> {
 var p=LocalAdministratorsPolicy.Load(new ThrowPolicy());Require(p.Configured&&!p.Readable,"collector sentinel drifted");
 var x=MissingEvidenceExplanation.For([new("PolicyConfigured",p.Configured.ToString(),p.Source),new("PolicyReadable",p.Readable.ToString(),p.Source)]);
 Require(!MissingEvidenceExplanation.Reason(x.ReasonCode,"en").Contains("policy is configured",StringComparison.OrdinalIgnoreCase),"sentinel claims confirmed configuration"); });
 Console.WriteLine($"CORRECTIVE {6-failed}/6");return failed==0?0:1;
 }
 private sealed class ThrowPolicy:ILocalAdminPolicySource {public LocalAdminPolicyReadResult Read()=>throw new InvalidOperationException();}
}
