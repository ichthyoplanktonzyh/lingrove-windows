#!/usr/bin/env python3
"""Explicit offline Darwin capture; never rewrites checked-in data or downloads Unicode files."""
import argparse,hashlib,json,subprocess,sys,tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
CURRENT='cc468b89f5c360cc7210fac4f1a8b52197870bf2'
CF='883ec6b01e4bd18d8356cc3873acaf3c2feaa050'
SUBSET_SHA='923827fcc4ed65835220b1f4e3f832e784922d114f49f6027245a705edec5789'

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',required=True,type=Path);a=p.parse_args()
 if sys.platform!='darwin':raise SystemExit('Foundation observations require macOS')
 if a.output.exists():raise SystemExit('Capture destination must not exist')
 swift=ROOT/'scripts/capture-editor-foundation-data.swift';subset=ROOT/'src/Cotabby.Core/Resources/Foundation/DerivedCoreProperties-17.0.0.InCB.txt'
 assert hashlib.sha256(subset.read_bytes()).hexdigest()==SUBSET_SHA
 with tempfile.TemporaryDirectory(prefix='lingrove-editor-folds-')as directory:
  binary=Path(directory)/'capture';subprocess.run(['swiftc','-O',str(swift),'-o',str(binary)],check=True)
  data=json.loads(subprocess.check_output([str(binary),'-AppleLocale','en_US'],timeout=120))
 assert data['captureLocale']=='en_US'and data['scalarCount']==1_112_064
 indic={}
 for line in subset.read_text().splitlines():
  fields=[field.strip()for field in line.split('#')[0].split(';')]
  if len(fields)!=3 or fields[1]!='InCB':continue
  endpoints=fields[0].split('..');indic.setdefault(fields[2],[]).append([int(endpoints[0],16),int(endpoints[-1],16)])
 assert set(indic)=={'Consonant','Linker','Extend'}
 data.update(version=1,macReference=CURRENT,coreFoundationSource=CF,unicodeVersion='17.0.0',unicodeSubsetSha256=SUBSET_SHA,
  generatorSha256=hashlib.sha256(swift.read_bytes()).hexdigest(),indicConjunctBreak=indic)
 with a.output.open('x',encoding='utf-8')as stream:json.dump(data,stream,ensure_ascii=False,sort_keys=True,separators=(',',':'));stream.write('\n')
 print('Captured all',data['scalarCount'],'Unicode scalars on',data['operatingSystem'],'Foundation',data['coreFoundationVersion'])
 print('Data SHA256:',hashlib.sha256(a.output.read_bytes()).hexdigest())
if __name__=='__main__':main()
