from pathlib import Path
import subprocess,json,hashlib,struct,zlib
q=Path('/workspace/work/gpc-no-data61');m=json.loads((q/'parent-manifest.json').read_text());raw=b''
for c in m['chunks']:
 b=subprocess.check_output(['git','cat-file','blob',c['blob_oid']],cwd='/workspace/gpc-no-data61');assert len(b)==c['bytes'] and hashlib.sha256(b).hexdigest()==c['sha256'];raw+=b
assert len(raw)==m['bytes'] and hashlib.sha256(raw).hexdigest()==m['sha256'];(q/'parent-authentic.exe').write_bytes(raw)
sig=bytes.fromhex('8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae');i=raw.index(sig);pos=struct.unpack_from('<Q',raw,i-8)[0]
major,minor,n=struct.unpack_from('<III',raw,pos);pos+=12
def string():
 global pos
 count=shift=0
 while True:
  b=raw[pos];pos+=1;count|=(b&127)<<shift
  if b<128:break
  shift+=7
 t=raw[pos:pos+count].decode();pos+=count;return t
bid=string();pos+=40
entries=[]
for _ in range(n):
 off,size,compressed,kind=struct.unpack_from('<qqqB',raw,pos);pos+=25;name=string();entries.append((name,off,size,compressed,kind))
name,off,size,compressed,kind=next(e for e in entries if e[0]=='G-PC-Health.dll');dll=raw[off:off+(compressed or size)];dll=zlib.decompress(dll,-15) if compressed else dll;assert len(dll)==size
pngpos=dll.index(b'\x89PNG\r\n\x1a\n');end=pngpos+8
while True:
 length=struct.unpack_from('>I',dll,end)[0];typ=dll[end+4:end+8];end+=12+length
 if typ==b'IEND':break
brand=q/'brand';brand.mkdir(exist_ok=True);(brand/'g-shield.png').write_bytes(dll[pngpos:end])
pe=struct.unpack_from('<I',raw,0x3c)[0];sections=struct.unpack_from('<H',raw,pe+6)[0];optsize=struct.unpack_from('<H',raw,pe+20)[0];opt=pe+24;resrva=struct.unpack_from('<I',raw,opt+112+16)[0];sec=opt+optsize
def offset(rva):
 for k in range(sections):
  vs,va,rs,ro=struct.unpack_from('<IIII',raw,sec+k*40+8)
  if va<=rva<va+max(vs,rs):return ro+rva-va
 raise ValueError(rva)
base=offset(resrva)
def children(rel):
 p=base+rel;a,b=struct.unpack_from('<HH',raw,p+12);return [struct.unpack_from('<II',raw,p+16+k*8) for k in range(a+b)]
def resources(typ):
 root=next(v for k,v in children(0) if k==typ)&0x7fffffff;out={}
 for ident,value in children(root):
  lang=children(value&0x7fffffff)[0][1];rva,size=struct.unpack_from('<II',raw,base+lang);out[ident]=raw[offset(rva):offset(rva)+size]
 return out
icons=resources(3)
b=next(iter(icons.values()))
assert b[:8]==b'\x89PNG\r\n\x1a\n'
w,h=struct.unpack_from('>II',b,16);assert 0<w<=256 and 0<h<=256
# Repackage the exact authentic RT_ICON PNG in an ICO container, no rendering.
hdr=struct.pack('<HHHBBBBHHII',0,1,1,w%256,h%256,0,0,1,32,len(b),22);data=b
(brand/'gpchc.ico').write_bytes(hdr+data)
(q/'brand-provenance.json').write_text(json.dumps({'source_artifact_commit':'9cf419be1190725efb540a4ea83485c74b664c02','source_binary_sha256':m['sha256'],'method':'static bundle managed-resource PNG and PE RT_GROUP_ICON/RT_ICON extraction; no native execution/generation','bundle_entries':n,'assets':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in brand.iterdir()}},indent=2));print('Authentic brand restored; entries',n)
