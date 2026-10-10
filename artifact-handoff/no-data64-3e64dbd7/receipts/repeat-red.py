from pathlib import Path
import hashlib,json
S=Path('/workspace/gpc-no-data61/src/G.PcHealthCheck');Q=Path('/workspace/work/gpc-no-data64/red-repeat');H=Q/'harness';H.mkdir(exist_ok=True)
old=Path('/workspace/work/gpc-no-data61/harness/Harness.csproj').read_text();extra=['FirmwareSecurityCollector','FirmwareSecurityCollectorSelfTest','LenovoFirmwareSecurityAdapter','DellFirmwareSecurityAdapter','HpFirmwareSecurityAdapter','HuaweiFirmwareSecurityAdapter','BitLockerSecurityCollector','BitLockerSecurityCollectorSelfTest','ReportService']
old=old.replace('</ItemGroup>',''.join(f'<Compile Include="{S}/{n}.cs"/>' for n in extra)+'<Compile Include="UiExtraction.cs"/><Compile Include="CorrectiveTests.cs"/><PackageReference Include="System.Management" Version="8.0.0"/></ItemGroup>')
if (S/'SecurityDetailPresentation.cs').exists():old=old.replace('</ItemGroup>',f'<Compile Include="{S}/SecurityDetailPresentation.cs"/></ItemGroup>')
(H/'Harness.csproj').write_text(old);(H/'ExtractedDeclarations.cs').write_bytes(Path('/workspace/work/gpc-no-data61/harness/ExtractedDeclarations.cs').read_bytes())
prov=[]
def method(file,name):
 t=(S/file).read_text();start=t.index('    private ',t.index(name)-70) if False else t.rfind('\n    private ',0,t.index(name)) + 1
 b=t.index('{',t.index(name));end=b+1;depth=1
 while depth:
  depth+=(t[end]=='{')-(t[end]=='}');end+=1
 out=t[start:end];prov.append(dict(file=file,sha256=hashlib.sha256((S/file).read_bytes()).hexdigest(),method=name,start=start,end=end,transform='access modifier only private→internal; full logic preserved'))
 return out.replace('private ','internal ',1)
methods=['AddSecurityControlRow(','AddBitLockerDetailRows(','AddLocalAdministratorDetailRows(','SplitEvidenceGroups(','SecurityStatusFromBitLockerEvidence(','DistinctSources(','SecurityGuidance(','SecurityStatusText(','SecurityPointsText(']
# Locate declarations rather than prior invocations.
def declaration(file,name):
 t=(S/file).read_text();positions=[i for i in range(len(t)) if t.startswith(name,i)];pos=next(i for i in positions if '\n    private ' in t[max(0,i-170):i]);start=t.rfind('\n    private ',0,pos)+1;b=t.index('{',pos)
 # expression-bodied declarations end at semicolon
 arrow=t.find('=>',pos,b)
 if arrow!=-1:
  end=t.index(';',arrow)+1
 else:
  end=b+1;depth=1
  while depth:
   depth+=(t[end]=='{')-(t[end]=='}');end+=1
 prov.append(dict(file=file,sha256=hashlib.sha256((S/file).read_bytes()).hexdigest(),method=name,start=start,end=end,transform='access modifier only'))
 return t[start:end].replace('private ','internal ',1)
# All detail-presentation method bodies are the actual production source, not a mirror.
ui='\n'.join(declaration('MainForm.SecurityPosture.cs',n) for n in methods)
t=(S/'MainForm.Symptoms.cs').read_text();start=t.index('        if (_isBusy) return;',t.index('private void OpenSymptom'));end=t.index('        using Form form',start);select=t[start:end];prov.append(dict(file='MainForm.Symptoms.cs',sha256=hashlib.sha256((S/'MainForm.Symptoms.cs').read_bytes()).hexdigest(),block='actual OpenSymptom admission/persistence prefix; native dialog creation excluded',start=start,end=end))
t=(S/'MainForm.cs').read_text();start=t.index('            var after = _assessment.Assess(afterData);');end=t.index('            Populate(after);',start);verify=t[start:end];prov.append(dict(file='MainForm.cs',sha256=hashlib.sha256((S/'MainForm.cs').read_bytes()).hexdigest(),block='actual post-collection verification/persistence block; collection/remediation/native controls excluded',start=start,end=end))
pre='''namespace G.PcHealthCheck;
internal sealed class UiExtraction
{
 internal readonly FakeGrid _securityGrid = new();
 internal bool _isBusy;
 internal List<SymptomNote> _symptomNotes = [];
 internal ScanResult? _current;
 internal readonly ReportService _reports = new();
 internal string _latestReport = "";
 private readonly AssessmentService _assessment = new();
 private object? _applyProgressOwner;
 internal void SelectSymptom(SymptomRoute route)
 {
'''
post='''
 }
 internal async Task<VerificationResult> Verify(ScanResult before, DiagnosticData afterData)
 {
 object? verificationOwner = null; IProgress<string>? verificationProgress = null; var batch = new RemediationBatchResult();
'''
finish='''
 return verification;
 }
 private static Task AttachSecurityPostureAsync(ScanResult after, IProgress<string>? progress) => Task.CompletedTask;
 private static void StyleSecurityRow(object?[] row, SecurityControlStatus status) { }
}
internal sealed class FakeGrid { internal readonly FakeRows Rows = new(); }
internal sealed class FakeRows : List<object?[]> { internal int Add(params object?[] row) { base.Add(row); return Count-1; } }
internal enum MessageBoxButtons { OK }
internal enum MessageBoxIcon { Error, Warning }
internal static class MessageBox { internal static string? Last; internal static void Show(object owner,string message,string title,MessageBoxButtons buttons,MessageBoxIcon icon) { Last=message; } }
'''
(H/'UiExtraction.cs').write_text(pre+select+post+verify+finish+ '\n')
# append actual private detail functions INSIDE class, before SelectSymptom declaration
p=H/'UiExtraction.cs';p.write_text(p.read_text().replace(' internal void SelectSymptom',ui+'\n internal void SelectSymptom'))
(H/'source-extraction-provenance.json').write_text(json.dumps(prov,indent=2));(H/'Program.cs').write_text('using G.PcHealthCheck;\nreturn CorrectiveTests.Run();\n')
