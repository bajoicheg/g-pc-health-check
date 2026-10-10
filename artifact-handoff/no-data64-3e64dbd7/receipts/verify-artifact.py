from pathlib import Path
import hashlib,struct,json,subprocess
q=Path('/workspace/work/gpc-no-data64');r='/workspace/gpc-no-data64';b=(q/'G-PC-Health-0.17.1-evidence-corrected.exe').read_bytes();sha=subprocess.check_output(['git','rev-parse','HEAD'],cwd=r,text=True).strip();pe=struct.unpack_from('<I',b,0x3c)[0];assert b[:2]==b'MZ' and b[pe:pe+4]==b'PE\0\0';assert struct.unpack_from('<H',b,pe+4)[0]==0x8664;assert struct.unpack_from('<H',b,pe+24)[0]==0x20b
n=struct.unpack_from('<H',b,pe+6)[0];opt=pe+24;secs=opt+struct.unpack_from('<H',b,pe+20)[0];rva=struct.unpack_from('<I',b,opt+128)[0]
def off(rva):
 for k in range(n):
  vs,va,rs,ro=struct.unpack_from('<IIII',b,secs+k*40+8)
  if va<=rva<va+max(vs,rs):return ro+rva-va
 raise ValueError(rva)
base=off(rva)
def entries(rel):
 p=base+rel;a,c=struct.unpack_from('<HH',b,p+12);return [struct.unpack_from('<II',b,p+16+k*8) for k in range(a+c)]
v=next(v for t,v in entries(0) if t==16)&0x7fffffff;v=entries(v)[0][1]&0x7fffffff;v=entries(v)[0][1];vrva,size=struct.unpack_from('<II',b,base+v);version=b[off(vrva):off(vrva)+size];i=version.index(bytes.fromhex('bd04effe'));ms,ls=struct.unpack_from('<II',version,i+8);fv=(ms>>16,ms&65535,ls>>16,ls&65535);assert fv==(0,17,1,0);assert ('0.17.1+'+sha).encode('utf-16le') in version
sig=bytes.fromhex('8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae');p=struct.unpack_from('<Q',b,b.index(sig)-8)[0];major,minor,count=struct.unpack_from('<III',b,p);assert major==6 and count>=453
result={'schema':'gpc-static-crosspublish-verification/v1','binary_name':'G-PC-Health-0.17.1-evidence-corrected.exe','bytes':len(b),'sha256':hashlib.sha256(b).hexdigest(),'candidate':sha,'parent':'3efc1a990bf56dc9659faf8be86d921dda8c1a2a','tree':subprocess.check_output(['git','rev-parse','HEAD^{tree}'],cwd=r,text=True).strip(),'PE':'AMD64/PE32+','FileVersion':'.'.join(map(str,fv)),'InformationalVersion':'0.17.1+'+sha,'bundle_entries':count,'Windows_runtime':'NOT_RUN','single_file_self_contained':'Crosspublish configuration; static CoreCLR-linked .NET Windows apphost, not native runtime acceptance','original_CI6_outcome':'UNKNOWN'}
(q/'verification.json').write_text(json.dumps(result,indent=2)+'\n');(q/'G-PC-Health-0.17.1-evidence-corrected.exe.sha256').write_text(result['sha256']+'  '+result['binary_name']+'\n');print(json.dumps(result,indent=2))
