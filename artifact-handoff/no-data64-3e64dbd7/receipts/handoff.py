from pathlib import Path
import os,sys,json,subprocess,hashlib,datetime,uuid
Q=Path('/workspace/work/gpc-no-data64');R='/workspace/g-pc-health-check';L='/workspace/gpc-no-data64';S='/workspace/work/gpc-no-data64/observed-runtime331/.agents/skills/continuous-development-cycle/scripts';sys.path.insert(0,S)
from git_object_integrity import git_object_environment
from git_document_store import GitDocumentStore,canonical
from git_lease_store import GitLeaseStore
from git_remote_identity import isolated_remote_args
REF='refs/heads/cdc/gpc-native-fresh-recovery-evidence-20261009';SOURCE='refs/heads/design/0.17.0-security-posture';ID='sha256:e9c5798b2e5857d11563b10845ae993ed5b0e7b040ffd6611bdbd1a36aad08d3';KEY='no_data64_corrective_handoff';env=git_object_environment(GIT_AUTHOR_NAME='GPC artifact evidence',GIT_AUTHOR_EMAIL='evidence@example.invalid',GIT_COMMITTER_NAME='GPC artifact evidence',GIT_COMMITTER_EMAIL='evidence@example.invalid',GIT_TERMINAL_PROMPT='0')
def git(*args,data=None,repo=R,extra=None,timeout=60):
 e=dict(env);e.update(extra or {});p=subprocess.run(['git','-c','advice.graftFileDeprecated=false','-C',repo,*args],input=data,capture_output=True,env=e,timeout=timeout)
 if p.returncode:raise RuntimeError('read/local Git failed '+str(args[:2])+': '+p.stderr.decode(errors='replace'))
 return p.stdout

def durable(path,value):
 raw=json.dumps(value,indent=2,ensure_ascii=False).encode()+b'\n';tmp=path.with_suffix('.tmp');
 with open(tmp,'wb') as f:f.write(raw);f.flush();os.fsync(f.fileno())
 os.replace(tmp,path);fd=os.open(path.parent,os.O_DIRECTORY);os.fsync(fd);os.close(fd)
def text(*args,**kwargs):return git(*args,**kwargs).decode().strip()
def hashblob(raw):return text('hash-object','-w','--stdin',data=raw)
def tree(files,name):
 idx=Q/(name+'.index');assert not idx.exists();extra={'GIT_INDEX_FILE':str(idx)};git('read-tree','--empty',extra=extra);lines=b''.join(('100644 '+oid+'\t'+path+'\n').encode() for path,oid in sorted(files.items()));git('update-index','--index-info',data=lines,extra=extra);return text('write-tree',extra=extra)

assert not (Q/'handoff-send-intent.json').exists(), 'Existing send intent: observe only, never replay'
verification=json.loads((Q/'verification.json').read_text());candidate=verification['candidate']
assert text('rev-parse','HEAD',repo=L)==candidate and text('status','--porcelain',repo=L)==''
assert text('rev-parse',candidate+'^',repo=L)=='3efc1a990bf56dc9659faf8be86d921dda8c1a2a'
assert text('rev-parse',candidate+':.agents/skills/continuous-development-cycle',repo=L)=='9c45d98c3254e9658d452c505d8c97698e3fc9a7'
assert text('hash-object','-t','commit','--stdin',data=(Q/'candidate-commit.raw').read_bytes(),repo=L)==candidate
assert text('rev-parse','--abbrev-ref','HEAD',repo=L)=='codex/local-no-data64'
for name,digest in json.loads((Q/'brand-provenance.json').read_text())['assets'].items():assert hashlib.sha256((Q/'brand'/name).read_bytes()).hexdigest()==digest
store=GitDocumentStore(R,'origin',REF,ID,protected_refs=[SOURCE,'refs/heads/cdc/coordination'])
old,doc=store.read();assert KEY not in doc, 'Existing handoff: reconcile only';before=json.loads(json.dumps(doc))
lease_store=GitLeaseStore(R,'origin','refs/heads/cdc/coordination');coord,lease=lease_store.read()
source=text('ls-remote','--refs','origin',SOURCE).split()[0]
assert source=='325fe132be9ff49bc09b8bfb873f6d3e92df1c5e'
assert lease['generation']==32 and lease['owner_id'] is None and lease['external_guard'] is None
assert 'submission_retirements' in lease, 'Never strip the administrative retirement history'
assert text('rev-parse','HEAD')=='38b00741642cf11afb879767ebd41901daf5d9bb' and text('status','--porcelain')==''
files={};prefix='artifact-handoff/no-data64-'+candidate[:8];raw=(Q/verification['binary_name']).read_bytes()
assert hashlib.sha256(raw).hexdigest()==verification['sha256'];chunks=[]
for i in range(0,len(raw),4*1024*1024):
 chunk=raw[i:i+4*1024*1024];path=prefix+f'/chunk-{len(chunks):03d}.bin';oid=hashblob(chunk);files[path]=oid
 chunks.append(dict(index=len(chunks),path=path,bytes=len(chunk),sha256=hashlib.sha256(chunk).hexdigest(),blob_oid=oid))
paths=text('diff','--name-only',verification['parent'],candidate,repo=L).splitlines();changed=[]
for path in paths:
 assert path.startswith('src/G.PcHealthCheck/') or path=='docs/releases/0.17.1-evidence.md'
 b=git('show',candidate+':'+path,repo=L);oid=hashblob(b);out=prefix+'/source/files/'+path;files[out]=oid
 changed.append(dict(path=path,bytes=len(b),sha256=hashlib.sha256(b).hexdigest(),git_blob=oid,artifact_path=out))
retained=['candidate.bundle','candidate.patch','candidate-commit.raw','verification.json',verification['binary_name']+'.sha256','publish-receipt.json','publish.stdout','publish.stderr','publish-console.log','compile.log','restore.log','red.log','red-repeat.log','green.log','green-extended.log','final-corrective.log','regressions.log','baseline-regressions.log','resource-verification.json','brand-provenance.json','test-receipts.json','build-receipts.json','fresh-boundary.json','fresh-lease.raw.json','fix-contract.txt','fix-plan.json','fix-state.json','fix-launch.json','spec-review.json','handoff.py','publish-local.py','verify-artifact.py','make-harness.py','repeat-red.py']
for local in retained:files[prefix+'/receipts/'+local]=hashblob((Q/local).read_bytes())
for folder in ['harness','red-repeat/harness','baseline-harness']:
 for p in sorted((Q/folder).iterdir()):
  if p.is_file() and p.suffix in ['.cs','.csproj','.json']:files[prefix+'/tests/'+str(p.relative_to(Q))]=hashblob(p.read_bytes())
manifest=dict(**{**verification,'schema':'gpc-raw-artifact-handoff/v1'},chunks=chunks,changed_files=changed,
 source_bundle=prefix+'/receipts/candidate.bundle',source_bundle_sha256=hashlib.sha256((Q/'candidate.bundle').read_bytes()).hexdigest(),
 source_patch=prefix+'/receipts/candidate.patch',source_patch_sha256=hashlib.sha256((Q/'candidate.patch').read_bytes()).hexdigest(),
 complete_history_bundle=True,local_branch='codex/local-no-data64',core='CDC2.12.1 unchanged',SPEC='PENDING Root65',QUALITY='PENDING Root66')
manifest_raw=(json.dumps(manifest,indent=2)+'\n').encode();mp=prefix+'/manifest.json';files[mp]=hashblob(manifest_raw);(Q/'manifest.json').write_bytes(manifest_raw)
artifact_tree=tree(files,'artifact');artifact=text('commit-tree',artifact_tree,data=b'Immutable no-data64 corrected EXE/source/RED-GREEN evidence bytes\n')
assert store.read()==(old,before), 'Evidence ref changed before proposal'
record=dict(schema='gpc-no-data64-corrective-handoff/v1',attempt='gpc-no-data64-corrective-20261010',
 admission='d010f6ca156227f40fe0a24028af92cd05767dc9',token='865ac864-2b44-4a7e-8a86-d340a5c378f7',
 contract_sha256='246b764a45757c89608264f88c65c08d41167507441a57c14707d3e9c09fce85',
 candidate=candidate,root_tree=verification['tree'],sole_parent=verification['parent'],local_branch='codex/local-no-data64',local_worktree=L,
 artifact_commit=artifact,artifact_tree=artifact_tree,manifest_path=mp,manifest_blob=files[mp],manifest_sha256=hashlib.sha256(manifest_raw).hexdigest(),
 binary=verification,bounded_diff=dict(files=len(paths),paths=paths,stat=text('diff','--stat',verification['parent'],candidate,repo=L)),
 corrections=['actual UnsupportedManufacturer shape (blank/Unknown remain unestablished)','individual volume/member explanations and persisted subject metadata across UI/HTML/JSON/clipboard','atomic new HTML/JSON pair before symptom state commit; IO failure rollback and old history preservation','both verification paths copy original symptom notes','unreadable policy never claims confirmed existence/type','fixed compact Security rows, full selectable details, native fixture added NOT_RUN'],
 tests=json.loads((Q/'test-receipts.json').read_text()),resources=json.loads((Q/'resource-verification.json').read_text()),
 build_receipts=json.loads((Q/'build-receipts.json').read_text()),publish_receipt=json.loads((Q/'publish-receipt.json').read_text()),
 brand_provenance=json.loads((Q/'brand-provenance.json').read_text()),source_before=source,coord_before=coord,lease_before=lease,
 current_shared_runtime=dict(version='3.3.1',package_tree='f580a8946c0510540a4be3f4422ab3404875274b',purpose='Read/validate already-adopted current schema only; no adoption/core changes',lease_validation='PASS actual full record including submission_retirements'),
 frozen_candidate_core=dict(version='2.12.1',package_tree='9c45d98c3254e9658d452c505d8c97698e3fc9a7',unchanged=True),
 original_CI6_outcome='UNKNOWN retained; administrative retirement is not original provider outcome proof',no_source_publication=True,no_CI_or_children=True,
 Windows_runtime='NOT_RUN',manual_UI_UAC_DPI_RDP_Huawei_Win11='NOT_RUN',independent_SPEC_QUALITY='PENDING Root65→Root66',artifact_only_not_release=True,
 prepared_at_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
 contract_materialized_at_utc=datetime.datetime.fromtimestamp((Q/'fix-contract.txt').stat().st_mtime,datetime.timezone.utc).isoformat())
doc[KEY]=record;encoded=canonical(doc).encode();assert len(encoded)<=4*1024*1024
blob=hashblob(encoded);final_tree=tree({'document.json':blob},'document')
proposal=text('commit-tree',final_tree,'-p',old,'-p',artifact,data=('Evidence-only no-data64 handoff '+str(uuid.uuid4())+'\n').encode())
assert text('ls-tree','--name-only',proposal)=='document.json';git('merge-base','--is-ancestor',old,proposal)
assert store.read()==(old,before) and lease_store.read()==(coord,lease) and text('ls-remote','--refs','origin',SOURCE).split()[0]==source
config,endpoint=isolated_remote_args(R,'origin',ID)
argv=['git','-c','advice.graftFileDeprecated=false','-C',R,*config,'push','--porcelain','--force-with-lease='+REF+':'+old,endpoint,proposal+':'+REF]
# No private credential config or endpoint strings are persisted/exported.
public_argv=['git','<authenticated isolated remote configuration>','push','--porcelain','--force-with-lease='+REF+':'+old,'<pinned repository endpoint>',proposal+':'+REF]
intent=dict(schema='gpc-evidence-only-CAS/v1',expected_old=old,proposal=proposal,artifact_commit=artifact,ref=REF,argv=public_argv,
 endpoint_identity=ID,source=source,coordination=coord,one_send=True,history_rewrite=False,send_state='consumed_before_send',at_utc=datetime.datetime.now(datetime.timezone.utc).isoformat())
durable(Q/'handoff-send-intent.json',intent)
try:
 p=subprocess.run(argv,env=env,capture_output=True,timeout=180)
 durable(Q/'handoff-send-result.json',dict(returncode=p.returncode,stdout=p.stdout.decode(errors='replace'),stderr=p.stderr.decode(errors='replace'),finished_at_utc=datetime.datetime.now(datetime.timezone.utc).isoformat()))
except subprocess.TimeoutExpired as ex:
 durable(Q/'handoff-send-result.json',dict(returncode=None,outcome='UNKNOWN timeout, no replay',stdout=(ex.stdout or b'').decode(errors='replace'),stderr=(ex.stderr or b'').decode(errors='replace')));raise
assert p.returncode==0,'Transport failed: preserve intent/result, no replay'
assert store.read()==(proposal,doc);assert store.read_revision(old)==before
assert text('ls-tree','--name-only',proposal)=='document.json'
after_coord,after_lease=lease_store.read();assert after_lease==lease and after_coord==coord
assert text('ls-remote','--refs','origin',SOURCE).split()[0]==source
assert text('status','--porcelain')=='' and text('status','--porcelain',repo=L)==''
receipt=dict(ref=REF,canonical_commit=proposal,canonical_blob=blob,key=KEY,artifact_commit=artifact,manifest_path=mp,manifest_blob=files[mp],
 candidate=candidate,root_tree=verification['tree'],sole_parent=verification['parent'],binary_sha256=verification['sha256'],bytes=verification['bytes'],chunks=len(chunks),
 source_unchanged=source,coordination_unchanged=coord,lease_unchanged=True,canonical_tree_only_document=True,old_keys_and_readback_preserved=True,
 finished_at_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),duration_seconds=(datetime.datetime.now(datetime.timezone.utc).timestamp()-(Q/'fix-contract.txt').stat().st_mtime),
 push_exit=p.returncode,Windows_runtime='NOT_RUN',original_CI6_outcome='UNKNOWN')
durable(Q/'final-receipt.json',receipt);print(json.dumps(receipt,indent=2))
