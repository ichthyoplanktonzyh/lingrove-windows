#!/usr/bin/env python3
"""Run complete original ReviewAudioPreparation with in-memory speech effects and held exports."""
import argparse,hashlib,json,subprocess,tempfile
from pathlib import Path
from macos_vocabulary_reference import BASELINE,model_sources,source
SOURCE='Cotabby/Services/Review/ReviewAudioPreparation.swift'
ID_HASH='1a37df193e251fd7289ec15bd109172d33153548e1afdc3afc07485d7d2361ac'
DRIVER=r'''
import Foundation
import CryptoKit
struct Corpus:Decodable{let cases:[Row]}
struct Row:Decodable,Sendable{let id:String;let speech:Bool;let actions:[Action]}
struct Action:Decodable,Sendable{
 let op:String;let kind:String?;let fingerprint:String?;let text:String?;let sentence:String?;let language:String?;let force:Bool?;let cancel:Bool?;let fail:String?
}
@MainActor final class FakeSpeech:SpeechExporting{
 var exportConfigurationID:String{"fixture"};var ownerID:UUID?;var calls:[[String:Any]]=[];var failure:String?;var fingerprint="request-fingerprint"
 var hold=false;var waiter:CheckedContinuation<SpeechExportAudio,Error>?;var entered:CheckedContinuation<Void,Never>?
 func exportRequest(text:String,language:LearningLanguage,context:String?)throws->SpeechExportRequest{
  calls.append(["op":"request","text":text,"language":language.rawValue,"context":context as Any? ?? NSNull()])
  if failure=="request"{throw SpeechError.generationFailed}
  return .init(text:text,language:language,fingerprint:fingerprint,source:.system(voiceID:"fixture"))
 }
 func exportAudio(_ request:SpeechExportRequest)async throws->SpeechExportAudio{
  calls.append(["op":"export","text":request.text,"language":request.language.rawValue,"fingerprint":request.fingerprint])
  if hold{return try await withCheckedThrowingContinuation{waiter=$0;entered?.resume();entered=nil}}
  if failure=="export"{throw SpeechError.generationFailed}
  if failure=="cancel"{throw CancellationError()}
  return audio()
 }
 func audio()->SpeechExportAudio{.init(data:Data("audio-\(calls.filter{($0["op"] as? String)=="export"}.count)".utf8),fileExtension:"wav")}
 func audition(_ audio:SpeechExportAudio,ownerID:UUID,language:LearningLanguage)throws{
  calls.append(["op":"audition","ownerMatches":self.ownerID==ownerID,"language":language.rawValue,"bytes":audio.data.count])
  if failure=="audition"{throw SpeechError.generationFailed}
 }
 func stop(ownerID:UUID){calls.append(["op":"stop","ownerMatches":self.ownerID==ownerID])}
 func waitForExport()async{if waiter != nil{return};await withCheckedContinuation{entered=$0}}
 func release(){let next=waiter!;waiter=nil;hold=false;if failure=="export"{next.resume(throwing:SpeechError.generationFailed)}else{next.resume(returning:audio())}}
}
func audio(_ value:ReviewPreparedAudio)->[String:Any]{["fingerprint":value.fingerprint,"bytes":value.audio.data.count,"extension":value.audio.fileExtension,"sha256":SHA256.hash(data:value.audio.data).map{String(format:"%02x",$0)}.joined()]}
@MainActor func snapshot(_ p:ReviewAudioPreparation,_ f:FakeSpeech)->[String:Any]{["generating":p.generating?.rawValue as Any? ?? NSNull(),"prepared":Dictionary(uniqueKeysWithValues:p.prepared.map{($0.key.rawValue,audio($0.value))}),"calls":f.calls]}
func request(_ a:Action)->SpeechExportRequest{.init(text:a.text ?? "clicked text",language:LearningLanguage(rawValue:a.language ?? "en")!,fingerprint:a.fingerprint ?? "fp",source:.system(voiceID:"fixture"))}
@main struct Main{
 @MainActor static func main()async throws{
  let corpus=try JSONDecoder().decode(Corpus.self,from:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1])))
  guard Locale.current.identifier == "en_US" else {fatalError("Speech error reference locale must be en_US")}
  var results:[[String:Any]]=[]
  for row in corpus.cases{
   let fake=FakeSpeech(),p=ReviewAudioPreparation(speech:row.speech ? fake:nil);fake.ownerID=p.ownerID
   var pending:Task<ReviewPreparedAudio,Error>?;var trace:[[String:Any]]=[["state":snapshot(p,fake),"outcome":NSNull()]]
   for a in row.actions{
    let kind=ReviewAudioKind(rawValue:a.kind ?? "expression")!;fake.failure=a.fail;fake.fingerprint=a.fingerprint ?? "request-fingerprint"
    var outcome:Any=NSNull()
    do{
     switch a.op{
     case "error":
      switch a.fail! {
      case "incompleteConfiguration":throw SpeechError.incompleteConfiguration
      case "missingLocalAssets":throw SpeechError.missingLocalAssets
      case "mismatchedLocalAssets":throw SpeechError.mismatchedLocalAssets
      case "unsupportedLanguage":throw SpeechError.unsupportedLanguage
      case "unrecognizedLanguage":throw SpeechError.unrecognizedLanguage
      case "runtimeUnavailable":throw SpeechError.runtimeUnavailable
      case "operationInProgress":throw SpeechError.operationInProgress
      case "generationFailed":throw SpeechError.generationFailed
      case "invalidAudio":throw SpeechError.invalidAudio
      case "invalidReferenceAudio":throw SpeechError.invalidReferenceAudio
      case "responseTooLarge":throw SpeechError.responseTooLarge
      case "textTooLong":throw SpeechError.textTooLong
      case "endpointStatus":throw SpeechError.endpointStatus(Int(a.text!)!)
      default:fatalError("Unknown original speech error")
      }
     case "request":
      let time=Date(timeIntervalSince1970:0),language=LearningLanguage(rawValue:a.language ?? "en")!
      let entry=VocabularyEntry(id:UUID(),expression:"entry",dictionaryForm:"entry",language:language,kind:.word,senses:[],createdAt:time,updatedAt:time)
      let content=ReviewCardContent(expression:a.text ?? "expression",answer:"answer",sentence:a.sentence ?? "sentence",ranges:[],translation:"translation",explanation:"",notes:"",gapPrompt:"",gapAnswer:"",recognition:false,gap:false)
      let value=try p.request(kind,entry:entry,content:content);outcome=["text":value.text,"language":value.language.rawValue,"fingerprint":value.fingerprint]
     case "prepare","audition":
      let value=request(a)
      let work=Task{@MainActor in
       if a.op=="audition"{try await p.audition(kind,request:value);return nil as ReviewPreparedAudio?}
       return try await p.prepare(kind,request:value,force:a.force ?? false)
      }
      if a.cancel==true{work.cancel()}
      if let value=try await work.value{outcome=audio(value)}
     case "start":
      fake.hold=true;let value=request(a);pending=Task{@MainActor in try await p.prepare(kind,request:value,force:a.force ?? false)};await fake.waitForExport()
     case "cancelPending":pending!.cancel()
     case "complete":let work=pending!;pending=nil;fake.release();outcome=audio(try await work.value)
     case "discard":p.discard()
     case "stop":p.stopPreview()
     default:fatalError("Unknown review audio action")
     }
    }catch is CancellationError{outcome=["error":"cancelled"]}
    catch{outcome=["error":error.localizedDescription]}
    trace.append(["state":snapshot(p,fake),"outcome":outcome])
   }
   precondition(fake.waiter==nil && pending==nil,"Held exports must be consumed")
   results.append(["id":row.id,"expected":trace])
  }
  FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject:results,options:[.sortedKeys]))
 }
}
'''
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--mac-repo',type=Path,required=True);p.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/review-audio-v1.json');p.add_argument('--capture-output',type=Path)
 a=p.parse_args();c=json.loads(a.fixture.read_text());rows=c['cases'];ids=[r['id'] for r in rows]
 if c.get('referenceLocale')!='en_US' or c.get('version')!=1 or c.get('macReference')!=BASELINE or len(ids)!=len(set(ids)) or hashlib.sha256('\n'.join(sorted(ids)).encode()).hexdigest()!=ID_HASH:p.error('Pinned reviewed preparation matrix required')
 if any(r.get('scope') not in ['preparation','error-messages'] or r.get('macSource')!=(SOURCE if r.get('scope')=='preparation' else 'Cotabby/Models/Speech/LearningSpeechModels.swift') or r.get('provenanceKind')!='source-policy-extension' for r in rows):p.error('Original preparation provenance required')
 audio=source(a.mac_repo,'Cotabby/Models/Review/ReviewAudioModels.swift')
 values=model_sources(a.mac_repo)+[audio[audio.index('/// A preparation sheet retains only'):],source(a.mac_repo,'Cotabby/Models/Runtime/OpenAICompatibleEndpointModels.swift'),source(a.mac_repo,'Cotabby/Models/Runtime/Parameters/ModelSamplingOverrides.swift'),source(a.mac_repo,'Cotabby/Models/Speech/LearningSpeechModels.swift'),source(a.mac_repo,'Cotabby/Models/Speech/SpeechExportModels.swift'),source(a.mac_repo,SOURCE),DRIVER]
 with tempfile.TemporaryDirectory(prefix='lingrove-review-audio-') as directory:
  root=Path(directory);files=[]
  for i,value in enumerate(values):
   f=root/f'source{i}.swift';f.write_text(value);files.append(str(f))
  binary=root/'probe';subprocess.run(['swiftc','-swift-version','6','-O',*files,'-o',str(binary)],check=True)
  actual=json.loads(subprocess.check_output([str(binary),str(a.fixture),'-AppleLocale','en_US'],timeout=60))
 if a.capture_output:
  with a.capture_output.open('x',encoding='utf-8') as out:json.dump(actual,out,ensure_ascii=False,indent=2);out.write('\n')
  print(f'Captured {len(actual)} original review audio traces');return
 if len(actual)!=len(rows):raise SystemExit('Review audio count changed')
 for row,value in zip(rows,actual):
  if value!={'id':row['id'],'expected':row['expected']}:raise SystemExit('Mac review audio mismatch: '+row['id'])
 print(f'Verified {len(rows)} complete original review audio traces')
if __name__=='__main__':main()
