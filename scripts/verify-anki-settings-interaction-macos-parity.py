#!/usr/bin/env python3
"""Run the exact original settings key-save method and appearance do/catch with recorded effects.
This is a method-boundary oracle, not a SwiftUI execution or a replacement settings/controller test.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, between, source
SOURCE = 'Cotabby/UI/Settings/Panes/LearningReviewSettingsSection.swift'
ID_HASH = '4c5dbe557cfd36ea2d542c19ddeec8be4a868a1a2b60f573441c6628784790fd'
DRIVER = r'''
import Foundation
struct Input:Codable {let cases:[Case]}
struct Case:Codable {let id:String;let stored:String;let actions:[Action]}
struct Action:Codable {let op:String;let value:String?;let flag:Bool?}
struct FixtureFailure:LocalizedError {let message:String;var errorDescription:String?{message}}
final class Effects {
 var stored:String;var reads=0,writes=0,invalidations=0;var readFails=false,writeFails=false
 init(_ stored:String){self.stored=stored}
 func readKey()throws->String {reads+=1;if readFails{throw FixtureFailure(message:"fixture read failure")};return stored}
 func saveKey(_ value:String)throws {writes+=1;if writeFails{throw FixtureFailure(message:"fixture write failure")};stored=value}
 func invalidateConnection(){invalidations+=1}
}
struct Observation:Codable {let key:String,savedKey:String;let keyError:String?;let stored:String;let reads:Int,writes:Int,invalidations:Int}
final class Harness {
 var key="",savedKey="";var keyError:String?;let settings:Effects;let controller:Effects
 init(_ effects:Effects){settings=effects;controller=effects}
 func appear(){ __APPEAR__ }
 func save(){saveKey()}
 __SAVE__
 func snapshot()->Observation{Observation(key:key,savedKey:savedKey,keyError:keyError,stored:settings.stored,reads:settings.reads,writes:settings.writes,invalidations:settings.invalidations)}
}
struct Result:Codable {let id:String;let expected:[Observation]}
@main struct Main {
 static func main()throws {
  let input=try JSONDecoder().decode(Input.self,from:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1])))
  var results:[Result]=[]
  for row in input.cases {
   let effects=Effects(row.stored),view=Harness(effects);var trace=[view.snapshot()]
   for a in row.actions {
    switch a.op {
    case "appear":view.appear()
    case "edit":view.key=a.value!
    case "save":view.save()
    case "readFailure":effects.readFails=a.flag!
    case "writeFailure":effects.writeFails=a.flag!
    default:fatalError("Unknown interaction action")
    }
    trace.append(view.snapshot())
   }
   results.append(Result(id:row.id,expected:trace))
  }
  let encoder=JSONEncoder();encoder.outputFormatting=[.sortedKeys];FileHandle.standardOutput.write(try encoder.encode(results))
 }
}
'''
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--mac-repo',type=Path,required=True)
 p.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/anki-settings-interaction-v1.json');p.add_argument('--capture-output',type=Path)
 a=p.parse_args();corpus=json.loads(a.fixture.read_text());rows=corpus['cases'];ids=[r['id'] for r in rows]
 if corpus.get('version')!=1 or corpus.get('macReference')!=BASELINE or len(rows)!=16 or len(ids)!=len(set(ids)) or hashlib.sha256('\n'.join(sorted(ids)).encode()).hexdigest()!=ID_HASH:p.error('Pinned reviewed 16-case key interaction matrix required')
 if any(r.get('macSource')!=SOURCE or r.get('provenanceKind')!='source-method-extension' for r in rows):p.error('Original settings method provenance required')
 original=source(a.mac_repo,SOURCE)
 method=between(original,'    private func saveKey() {','\n}\n')
 appearance=between(original,'            do { key = try settings.readKey(); savedKey = key }','\n        }\n        .onDisappear')
 # Assert the UI wiring used by the port; only the two literal method bodies below execute.
 for required in ['.focused($keyFocused).onSubmit(saveKey)', '.onDisappear(perform: saveKey)', '.onChange(of: keyFocused) { _, focused in if !focused { saveKey() } }', '.onChange(of: settings.address) { _, _ in controller.invalidateConnection() }', 'if keyError == nil { Task { await controller.testConnection(model: ReviewLayoutPolicy.model) } }', '.disabled(controller.testing || controller.sendingSenseID != nil)']:
  if required not in original:raise SystemExit('Original UI wiring changed: '+required)
 driver=DRIVER.replace('__SAVE__',method).replace('__APPEAR__',appearance)
 with tempfile.TemporaryDirectory(prefix='lingrove-anki-key-interaction-') as directory:
  root=Path(directory);swift=root/'main.swift';swift.write_text(driver);binary=root/'probe'
  subprocess.run(['swiftc','-parse-as-library','-swift-version','6','-O',str(swift),'-o',str(binary)],check=True)
  actual=json.loads(subprocess.check_output([str(binary),str(a.fixture)]))
 # Optional fields omitted by Swift Codable are normalized to explicit null for both runners.
 for row in actual:
  for observation in row['expected']:observation.setdefault('keyError',None)
 if a.capture_output:
  with a.capture_output.open('x',encoding='utf-8') as out:json.dump(actual,out,ensure_ascii=False,indent=2);out.write('\n')
  print(f'Captured {len(actual)} original settings key-method traces');return
 if len(actual)!=len(rows):raise SystemExit('Interaction count changed')
 for row,value in zip(rows,actual):
  if value!={'id':row['id'],'expected':row['expected']}:raise SystemExit('Mac key interaction mismatch: '+row['id'])
 print(f'Verified {len(rows)} original settings key-method traces')
if __name__=='__main__':main()
