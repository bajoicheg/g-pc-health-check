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
 var full=ui.VerifyFullBatch(before,new());Require(full.After.SymptomNotes.SequenceEqual(before.SymptomNotes),"full-batch verification lost notes"); });
 Test("5 throwing policy source never claims configured",()=> {
 var p=LocalAdministratorsPolicy.Load(new ThrowPolicy());Require(p.Configured&&!p.Readable,"collector sentinel drifted");
 var x=MissingEvidenceExplanation.For([new("PolicyConfigured",p.Configured.ToString(),p.Source),new("PolicyReadable",p.Readable.ToString(),p.Source)]);
 Require(!MissingEvidenceExplanation.Reason(x.ReasonCode,"en").Contains("policy is configured",StringComparison.OrdinalIgnoreCase),"sentinel claims confirmed configuration"); });
 Test("3 persistence failure rolls back snapshot and history",()=> {
 var scan=new ScanResult{SymptomNotes=[new("application","incident-review")]};var ui=new UiExtraction{_current=scan,_symptomNotes=scan.SymptomNotes.ToList()};
 var saved=ui._reports.SaveScan(scan); ui._latestReport=saved.Html;var dir=ui._reports.ReportsDirectory;var backup=dir+"-owned-backup";
 Directory.Move(dir,backup);File.WriteAllText(dir,"owned failure fixture");
 try {ui.SelectSymptom(SymptomRoutes.All[0]);Require(ReferenceEquals(ui._current,scan)&&ui._latestReport==saved.Html&&ui._symptomNotes.SequenceEqual(scan.SymptomNotes),"failed save changed snapshot");Require(MessageBox.Last is not null,"save failure not surfaced");}
 finally {File.Delete(dir);Directory.Move(backup,dir);}
 });
 Test("2 member subject metadata survives serialization with aggregate failure",()=> {
 var members=LocalAdministratorsCollector.Evaluate(new(true,true,["SID:S-allowed"],"Policy"),[new("intruder","S-denied","User","NetAPI32",true),new(null,null,"User","NetAPI32",false)],"PC");
 var scan=new ScanResult{Security=new(){Controls=[new("SEC-LOCAL-ADMINS",SecurityControlStatus.Fail,10,0,"SEC-LOCAL-ADMINS",members.Control.Evidence)]},SecuritySnapshot=new(){LocalAdministrators=members}};
 var json=JsonSerializer.Serialize(scan);var copy=JsonSerializer.Deserialize<ScanResult>(json)!;
 Require(copy.SecurityDetails.Count==2,"per-member metadata lost across JSON");Require(copy.SecurityDetails[1].Explanation?.ReasonCode=="identity_unavailable","individual cause lost");
 Require(SecurityReportSection.BuildHtml(copy,"en").Contains("PrincipalIdentityUnavailable"),"HTML member facts lost");Require(SecurityReportSection.BuildClipboardSummary(copy,"en").Contains("PrincipalIdentityUnavailable"),"clipboard member facts lost");
 });
 Test("2 per-subject cause never inherited from another volume",()=> {
 var control=new SecurityControlResult("SEC-BITLOCKER-DATA",SecurityControlStatus.Unknown,10,0,"SEC-BITLOCKER-DATA",[new("Volume","D:","WMI"),new("CollectionError","UnauthorizedAccessException","WMI"),new("Volume","E:","WMI"),new("ProtectionStatus","Unknown","WMI"),new("ConversionStatus","Unknown","WMI")]);
 var details=SecurityDetailPresentation.BitLocker(control);Require(details[0].Explanation?.ReasonCode=="access_denied"&&details[1].Explanation?.ReasonCode=="cause_not_established","cross-volume cause inference");
 });
 Console.WriteLine($"CORRECTIVE {9-failed}/9");return failed==0?0:1;
 }
 private sealed class ThrowPolicy:ILocalAdminPolicySource {public LocalAdminPolicyReadResult Read()=>throw new InvalidOperationException();}
}
