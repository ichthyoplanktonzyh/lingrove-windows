#!/usr/bin/env python3
"""Run original Qwen companion/language metadata and voice-tag compatibility without providers."""
import argparse,hashlib,json,subprocess,tempfile
from pathlib import Path
from macos_vocabulary_reference import BASELINE,between,source
PAIR='Cotabby/Support/Speech/SpeechModelPairingPolicy.swift'
TEXT='Cotabby/Support/Speech/SpeechTextPolicy.swift'
ID_HASH='a5ed8d5f808adf72d726a38a6f87ea43a13bd01e11c5091f292bd9218457d5e1'
DRIVER=r'''
import Foundation
struct Corpus:Decodable{let cases:[Row]}
struct Row:Decodable{let id:String,kind:String;let model:String?;let decoder:String?;let filename:String?;let language:String?;let tag:String?}
struct Observation:Codable{var decoders:[String]?;var known:Bool?;var mismatch:Bool?;var displayName:String?;var code:String?;var match:Int?}
struct Result:Codable{let id:String;let expected:Observation}
@main struct Main{
 static func main()throws{
  let corpus=try JSONDecoder().decode(Corpus.self,from:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1])))
  var results:[Result]=[]
  for row in corpus.cases{
   var o=Observation()
   switch row.kind{
   case "pair":o.decoders=SpeechModelPairingPolicy.decoderFilenames(for:row.model!);o.known=SpeechModelPairingPolicy.isKnownDecoder(row.decoder!);o.mismatch=SpeechModelPairingPolicy.isKnownMismatch(model:row.model!,decoder:row.decoder!);o.displayName=SpeechModelPairingPolicy.displayName(for:row.filename!)
   case "local":o.code=LocalSpeechLanguage.code(for:LearningLanguage(rawValue:row.language!)!)
   case "match":o.match=SpeechVoicePolicy.languageMatch(row.tag!,for:LearningLanguage(rawValue:row.language!)!)?.rawValue
   default:fatalError("Unknown speech metadata action")
   }
   results.append(Result(id:row.id,expected:o))
  }
  let encoder=JSONEncoder();encoder.outputFormatting=[.sortedKeys];FileHandle.standardOutput.write(try encoder.encode(results))
 }
}
'''
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--mac-repo',type=Path,required=True);p.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/speech-metadata-v1.json');p.add_argument('--capture-output',type=Path)
 a=p.parse_args();c=json.loads(a.fixture.read_text());rows=c['cases'];ids=[r['id'] for r in rows]
 if c.get('version')!=1 or c.get('macReference')!=BASELINE or len(ids)!=len(set(ids)) or hashlib.sha256('\n'.join(sorted(ids)).encode()).hexdigest()!=ID_HASH:p.error('Pinned reviewed metadata matrix required')
 if any(r.get('macSource')!=(PAIR if r['kind']=='pair' else TEXT) or r.get('provenanceKind')!='source-policy-extension' for r in rows):p.error('Original metadata provenance required')
 text=source(a.mac_repo,TEXT);models=source(a.mac_repo,'Cotabby/Models/Speech/LearningSpeechModels.swift')
 values=[source(a.mac_repo,'Cotabby/Models/Context/LanguageCatalog.swift'),source(a.mac_repo,'Cotabby/Models/LanguageLearning/LearningLanguage.swift'),
  'import Foundation\n'+between(models,'nonisolated enum SpeechVoiceQuality:','/// Distinguishes equal text'),
  'import Foundation\n'+text[text.index('nonisolated enum LocalSpeechLanguage {'):],source(a.mac_repo,PAIR),DRIVER]
 with tempfile.TemporaryDirectory(prefix='lingrove-speech-metadata-') as directory:
  root=Path(directory);files=[]
  for i,value in enumerate(values):
   f=root/f'source{i}.swift';f.write_text(value);files.append(str(f))
  binary=root/'probe';subprocess.run(['swiftc','-swift-version','6','-O',*files,'-o',str(binary)],check=True)
  actual=json.loads(subprocess.check_output([str(binary),str(a.fixture)]))
 for row in actual:
  for key in ['decoders','known','mismatch','displayName','code','match']:row['expected'].setdefault(key,None)
 if a.capture_output:
  with a.capture_output.open('x',encoding='utf-8') as out:json.dump(actual,out,ensure_ascii=False,indent=2);out.write('\n')
  print(f'Captured {len(actual)} original speech metadata observations');return
 if len(actual)!=len(rows):raise SystemExit('Speech metadata count changed')
 for row,value in zip(rows,actual):
  if value!={'id':row['id'],'expected':row['expected']}:raise SystemExit('Mac speech metadata mismatch: '+row['id'])
 print(f'Verified {len(rows)} original speech metadata observations')
if __name__=='__main__':main()
