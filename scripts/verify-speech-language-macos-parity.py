#!/usr/bin/env python3
"""Execute the complete original speech language resolver with scripted unconstrained scores."""
import argparse, hashlib, json, subprocess, tempfile
from pathlib import Path
from macos_vocabulary_reference import BASELINE, source
SOURCE='Cotabby/Support/Speech/SpeechLanguageResolver.swift'
ID_HASH='f36aacd9320d73e760f2f96fd91a4d97d6dfd0b68fb3bc7d7edc96a55ccb4c93'
DRIVER=r'''
import Foundation
import NaturalLanguage
struct Corpus:Decodable{let cases:[Row]}
struct Row:Decodable{let id:String;let text:String;let hint:String;let context:String?;let scores:[[String:String]]}
struct Observation:Codable{let language:String?;let samples:[String]}
struct Result:Codable{let id:String;let expected:Observation}
struct Output:Codable{let results:[Result];let foundationLetterRanges:[Int];let wordLetterRanges:[Int]}
func ranges(_ predicate:(Unicode.Scalar)->Bool)->[Int]{
 var result:[Int]=[];var start:Int?;var last=0
 for value in 0...0x10FFFF {
  let accepted=Unicode.Scalar(value).map(predicate) ?? false
  if accepted { if start == nil {start=value};last=value }
  else if let first=start {result.append(contentsOf:[first,last]);start=nil}
 }
 if let first=start {result.append(contentsOf:[first,last])};return result
}
@main struct Main{
 static func main()throws{
  let corpus=try JSONDecoder().decode(Corpus.self,from:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1])))
  var results:[Result]=[]
  for row in corpus.cases{
   var samples:[String]=[]
   let language=SpeechLanguageResolver.resolve(text:row.text,hint:LearningLanguage(rawValue:row.hint)!,context:row.context){sample in
    let index=samples.count;samples.append(sample)
    let scores=index < row.scores.count ? row.scores[index] : [:]
    return Dictionary(uniqueKeysWithValues:scores.map{(NLLanguage(rawValue:$0.key),Double($0.value)!)})
   }
   results.append(Result(id:row.id,expected:Observation(language:language?.rawValue,samples:samples)))
  }
  let encoder=JSONEncoder();encoder.outputFormatting=[.sortedKeys];FileHandle.standardOutput.write(try encoder.encode(Output(results:results,foundationLetterRanges:ranges { CharacterSet.letters.contains($0) },wordLetterRanges:ranges { Character(String($0)).isLetter })))
 }
}
'''
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--mac-repo',type=Path,required=True);p.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/speech-language-v1.json');p.add_argument('--capture-output',type=Path)
 a=p.parse_args();c=json.loads(a.fixture.read_text());rows=c['cases'];ids=[r['id'] for r in rows]
 if c.get('version')!=1 or c.get('macReference')!=BASELINE or len(ids)!=len(set(ids)) or hashlib.sha256('\n'.join(sorted(ids)).encode()).hexdigest()!=ID_HASH:p.error('Pinned reviewed language matrix required')
 if any(r.get('macSource')!=SOURCE or r.get('provenanceKind') not in ['source-policy-extension','original-test-adaptation'] for r in rows):p.error('Original resolver provenance required')
 values=[source(a.mac_repo,'Cotabby/Models/Context/LanguageCatalog.swift'),source(a.mac_repo,'Cotabby/Models/LanguageLearning/LearningLanguage.swift'),source(a.mac_repo,SOURCE),DRIVER]
 with tempfile.TemporaryDirectory(prefix='lingrove-speech-language-') as directory:
  root=Path(directory);files=[]
  for i,value in enumerate(values):
   f=root/f'source{i}.swift';f.write_text(value);files.append(str(f))
  binary=root/'probe';subprocess.run(['swiftc','-swift-version','6','-O',*files,'-o',str(binary)],check=True)
  actual=json.loads(subprocess.check_output([str(binary),str(a.fixture)]))
 for row in actual['results']:row['expected'].setdefault('language',None)
 if a.capture_output:
  with a.capture_output.open('x',encoding='utf-8') as out:json.dump(actual,out,ensure_ascii=False,indent=2);out.write('\n')
  print(f"Captured {len(actual['results'])} original speech language observations and Unicode ranges");return
 if len(actual['results'])!=len(rows):raise SystemExit('Speech language count changed')
 for key in ['foundationLetterRanges','wordLetterRanges']:
  if c.get(key)!=actual[key]:raise SystemExit('Mac speech Unicode property changed: '+key)
 for row,value in zip(rows,actual['results']):
  if value!={'id':row['id'],'expected':row['expected']}:raise SystemExit('Mac speech language mismatch: '+row['id'])
 print(f'Verified {len(rows)} original speech language observations')
if __name__=='__main__':main()
