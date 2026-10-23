#!/usr/bin/env python3
"""Run complete original review settings with isolated defaults and its nil-service memory key store."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, between, sense_policy_sources, source
SETTINGS='Cotabby/Models/Review/AnkiReviewSettings.swift'
DRIVER=r'''
import Foundation
func text(_ r:[String:AnkiValue],_ k:String,_ fallback:String="")->String {r[k]?.string ?? fallback}
func mapping(_ v:AnkiValue?)->[ReviewField:String] {Dictionary(uniqueKeysWithValues:(v?.object ?? [:]).map{(ReviewField(rawValue:$0.key)!,$0.value.string!)})}
func bool(_ r:[String:AnkiValue],_ k:String)->Bool {if case let .bool(v)=r[k]{return v};return false}
@MainActor func snapshot(_ s:AnkiReviewSettings,_ d:UserDefaults)->AnkiValue {
 var result:[String:AnkiValue] = ["address":.string(s.address),"deck":.string(s.deck),"model":.string(s.model),
  "mapping":.strings(Dictionary(uniqueKeysWithValues:s.mapping.map{($0.key.rawValue,$0.value)})),"effectiveMapping":.strings(Dictionary(uniqueKeysWithValues:s.effectiveMapping.map{($0.key.rawValue,$0.value)})),
  "recognition":.bool(s.defaultRecognition),"gap":.bool(s.defaultGap),"hasSavedDeck":.bool(s.hasSavedDeck)]
 let keys=["ankiReviewAddress","ankiReviewDeck","ankiReviewModel","ankiReviewMapping","ankiReviewRecognition","ankiReviewGap"]
 result["savedKeys"] = .list(keys.filter{d.object(forKey:$0) != nil})
 if let data=d.data(forKey:"ankiReviewMapping") { result["savedMappingIsArray"] = .bool((try? JSONDecoder().decode(AnkiValue.self,from:data))?.array != nil) }
 else { result["savedMappingIsArray"] = .null }
 if let data=d.data(forKey:"ankiReviewMapping"),let decoded=try? JSONDecoder().decode([ReviewField:String].self,from:data) {
  result["savedMapping"] = .strings(Dictionary(uniqueKeysWithValues:decoded.map{($0.key.rawValue,$0.value)}))
 }else{result["savedMapping"] = .null}
 return .object(result)
}
@main struct Main {
 @MainActor static func main()throws {
  let corpus=try JSONDecoder().decode(AnkiValue.self,from:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1])))
  var results:[AnkiValue]=[]
  for input in corpus.object!["cases"]!.array! {
   let r=input.object!, name="lingrove-anki-settings-"+UUID().uuidString,d=UserDefaults(suiteName:name)!
   d.removePersistentDomain(forName:name);defer{d.removePersistentDomain(forName:name)}
   for(key,v) in r["seed"]?.object ?? [:] {
    let spec=v.object!, value=spec["value"] ?? .null
    switch text(spec,"type") {
    case "string":d.set(value.string!,forKey:key)
    case "number":if case let .number(n)=value{d.set(n,forKey:key)}
    case "bool":if case let .bool(v)=value{d.set(v,forKey:key)}
    case "data":d.set(Data(base64Encoded:value.string!)!,forKey:key)
    case "array":d.set(["unexpected"],forKey:key)
    default:break
    }
   }
   let credentials=AnkiCredentialStore(service:nil);var s=AnkiReviewSettings(defaults:d,credentials:credentials),trace:[AnkiValue]=[snapshot(s,d)]
   for value in r["actions"]?.array ?? [] {
    let a=value.object!;var outcome:AnkiValue = .null
    do {
     switch text(a,"op") {
     case "address":s.address=text(a,"value")
     case "deck":s.deck=text(a,"value")
     case "model":s.model=text(a,"value")
     case "mapping":s.mapping=mapping(a["value"])
     case "recognition":s.defaultRecognition=bool(a,"value")
     case "gap":s.defaultGap=bool(a,"value")
     case "saveKey":try s.saveKey(text(a,"value"))
     case "readKey":outcome = .object(["key":.string(try s.readKey())])
     case "connection":let c=try s.connection();outcome = .object(["endpoint":.string(c.endpoint.absoluteString),"key":.string(c.apiKey)])
     case "restart":s=AnkiReviewSettings(defaults:d,credentials:credentials)
     default:fatalError("Unknown settings action")
     }
    }catch{outcome = .object(["error":.string(error.localizedDescription)])}
    trace.append(.object(["state":snapshot(s,d),"result":outcome]))
   }
   results.append(.object(["id":r["id"]!,"expected":.array(trace)]))
  }
  let encoder=JSONEncoder();encoder.outputFormatting=[.sortedKeys];FileHandle.standardOutput.write(try encoder.encode(AnkiValue.array(results)))
 }
}
'''
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--mac-repo',type=Path,required=True)
 p.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/anki-settings-v1.json');p.add_argument('--capture-output',type=Path)
 a=p.parse_args();c=json.loads(a.fixture.read_text());rows=c['cases'];ids=[r['id'] for r in rows]
 if c.get('version')!=1 or c.get('macReference')!=BASELINE or not rows or len(ids)!=len(set(ids)):p.error('Pinned unique settings cases required')
 if hashlib.sha256('\n'.join(sorted(ids)).encode()).hexdigest()!='fa6f2af61d65b4abe76140b381fd606ea8c79d98bf915512d481ec7dd5dae5c4':p.error('Reviewed60-case settings matrix changed')
 if any(r.get('macSource')!=SETTINGS or r.get('provenanceKind')!='source-policy-extension' for r in rows):p.error('Original settings provenance required')
 policy=source(a.mac_repo,'Cotabby/Support/Review/AnkiReviewPolicy.swift')
 values=sense_policy_sources(a.mac_repo)+[source(a.mac_repo,'Cotabby/Models/Review/AnkiWireModels.swift'),
  'import Foundation\nextension AnkiReviewPolicy {\n'+between(policy,'    static let presetName','    static func removesExistingCard(')+between(policy,'    static func endpoint(','    static func fields(')+'}\n',source(a.mac_repo,SETTINGS)]
 with tempfile.TemporaryDirectory(prefix='lingrove-anki-settings-')as temp:
  root=Path(temp);files=[]
  for i,v in enumerate(values+[DRIVER]):
   f=root/f'source{i}.swift';f.write_text(v);files.append(str(f))
  binary=root/'anki-settings';subprocess.run(['swiftc','-swift-version','6','-O',*files,'-o',str(binary)],check=True)
  actual=json.loads(subprocess.check_output([str(binary),str(a.fixture)]))
 if a.capture_output:
  with a.capture_output.open('x',encoding='utf-8')as out:json.dump(actual,out,ensure_ascii=False,indent=2);out.write('\n')
  print(f'Captured {len(actual)} original Anki settings traces');return
 if len(actual)!=len(rows):raise SystemExit('Settings count changed')
 for r,v in zip(rows,actual):
  if v!={'id':r['id'],'expected':r['expected']}:raise SystemExit('Mac settings mismatch: '+r['id'])
 print(f'Verified {len(rows)} original Anki settings traces')
if __name__=='__main__':main()
