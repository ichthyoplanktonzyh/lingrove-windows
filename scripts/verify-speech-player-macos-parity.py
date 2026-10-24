#!/usr/bin/env python3
"""Run complete pinned PCM facade and verbatim gated renderer over controlled in-memory flows."""
import argparse,hashlib,json,subprocess,tempfile
from pathlib import Path
from macos_vocabulary_reference import BASELINE,source
PLAYER='Cotabby/Services/Speech/SpeechPCMPlayer.swift'
TESTS='CotabbyTests/Services/Speech/SpeechPCMPlayerTests.swift'
ID_HASH='c2f537e09aad4403ed04f2a6b26a31b0953519378c5d7a4195aaf402de23d481'
OBSERVER=r'''
extension SpeechPCMPlayer {
 func observed(_ names:[SpeechPlaybackToken:String])->[String:Any]{
  ["token":token.flatMap{names[$0]} as Any? ?? NSNull(),"revision":String(revision),"rate":rate,"sampleRate":sampleRate as Any? ?? NSNull(),"playing":playing,"queued":state.queuedSamples,"total":state.totalSamples,"ended":state.inputEnded,"preparation":preparation != nil,"watchdog":drainWatchdog != nil]
 }
 func preparationTask()->Task<Void,Error>{preparation!}
}
extension GatedPCMRenderer {
 func observed()->[String:[String]]{["preparations":preparations.map{String($0.revision)},"enqueues":enqueues.map{String($0)}]}
}
'''
DRIVER=r'''
import Foundation
@MainActor func wait(_ condition:()async->Bool)async throws{
 for _ in 0..<1500{if await condition(){return};try await Task.sleep(for:.milliseconds(10))};throw PCMTestTimeout()
}
func text(_ r:[String:Any],_ key:String,_ fallback:String="")->String{r[key] as? String ?? fallback}
func integer(_ r:[String:Any],_ key:String,_ fallback:Int=0)->Int{(r[key] as? NSNumber)?.intValue ?? fallback}
func errorName(_ error:any Error)->String{if error is CancellationError{return "cancelled"};if case SpeechError.invalidAudio=error{return "invalidAudio"};fatalError("Unexpected source failure: \(error)")}
@main struct Main{
 @MainActor static func main()async throws{
  guard Locale.current.identifier=="en_US"else{fatalError("Player source locale must be en_US")}
  let corpus=try JSONSerialization.jsonObject(with:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1]))) as! [String:Any];var results:[[String:Any]]=[]
  for row in corpus["cases"] as! [[String:Any]]{
   let renderer=GatedPCMRenderer(),player=SpeechPCMPlayer(renderer:renderer)
   var tokens:[String:SpeechPlaybackToken]=[:],names:[SpeechPlaybackToken:String]=[:],events:[[String:Any]]=[],tasks:[String:Task<Void,Error>]=[:],taskPreparations:[String:Int]=[:],preparations:[Task<Void,Error>]=[],released:Set<Int>=[]
   func token(_ name:String)->SpeechPlaybackToken{if let t=tokens[name]{return t};let t=SpeechPlaybackToken();tokens[name]=t;names[t]=name;return t}
   player.eventHandler={e in
    var v:[String:Any]=["token":names[e.token]!]
    switch e{case .prepared:v["kind"]="prepared";v["milliseconds"]="measured"
    case .started:v["kind"]="started";case .finished:v["kind"]="finished";case .cancelled:v["kind"]="cancelled"
    case .failed(_,let message):v["kind"]="failed";v["message"]=message};events.append(v)
   }
   func snapshot(_ result:Any=NSNull())async->[String:Any]{["player":player.observed(names),"renderer":await renderer.observed(),"events":events,"result":result]}
   var trace:[[String:Any]]=[await snapshot()]
   for a in row["actions"] as! [[String:Any]]{
    var result:Any=NSNull()
    do{
     let t=token(text(a,"token","a"))
     switch text(a,"op"){
     case "begin":player.begin(sampleRate:integer(a,"rateHz",24000),rate:(a["rate"] as? NSNumber)?.doubleValue ?? 1,token:t);preparations.append(player.preparationTask());try await wait{await renderer.preparations.count==preparations.count}
     case "release","fail":let index=integer(a,"index");released.insert(index);if text(a,"op")=="fail"{await renderer.failPreparation(at:index)}else{await renderer.releasePreparation(at:index)};let readiness=await preparations[index].result;for (name,task)in tasks where taskPreparations[name]==index{_ = await task.result};try readiness.get()
     case "enqueue","launch":
      let chunk=try SpeechPCMChunk(data:Data(repeating:0,count:integer(a,"samples",19200)*4),sampleRate:integer(a,"rateHz",24000))
      if text(a,"op")=="launch"{var entered=false;taskPreparations[text(a,"task","job")]=preparations.count-1;tasks[text(a,"task","job")]=Task{entered=true;try await player.enqueue(chunk,token:t)};try await wait{entered}}
      else if a["cancel"] as? Bool==true{try await Task{withUnsafeCurrentTask{$0?.cancel()};try await player.enqueue(chunk,token:t)}.value}
      else{try await player.enqueue(chunk,token:t)}
     case "join":try await tasks[text(a,"task","job")]!.value
     case "cancel":tasks[text(a,"task","job")]!.cancel()
     case "end":try await player.endInput(token:t)
     case "played":await renderer.finishBuffer(preparation:integer(a,"index"),bufferID:integer(a,"buffer"))
     case "invalidate":await renderer.invalidate(preparation:integer(a,"index"))
     case "stop":player.stop()
     case "shutdown":player.shutdown()
     case "waitFailure":try await wait{events.contains{($0["kind"] as? String)=="failed" && ($0["token"] as? String)==text(a,"token","a")}}
     default:fatalError("Unknown player action")
     }
    }catch{result=errorName(error)}
    trace.append(await snapshot(result))
   }
   player.shutdown()
   for index in preparations.indices where !released.contains(index){await renderer.releasePreparation(at:index)}
   for task in preparations{_ = try? await task.value};for task in tasks.values{_ = try? await task.value}
   results.append(["id":row["id"]!,"expected":trace])
  }
  FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject:results,options:[.sortedKeys]))
 }
}
'''
STUB=r'''
struct SpeechPCMRenderer:SpeechPCMRendering{
 func prepare(sampleRate:Int,rate:Double,revision:UInt64,played:@escaping SpeechPCMPlayedCallback,invalidated:@escaping SpeechPCMInvalidationCallback)async throws{fatalError("Reference must inject the original test renderer")}
 func enqueue(_ chunk:SpeechPCMChunk,bufferID:Int,revision:UInt64)async throws{fatalError("Unused native renderer")}
 func play(revision:UInt64)async throws{fatalError("Unused native renderer")}
 func stop(revision:UInt64,release:Bool)async{fatalError("Unused native renderer")}
}
'''
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--mac-repo',type=Path,required=True);p.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/speech-player-v1.json');p.add_argument('--capture-output',type=Path)
 a=p.parse_args();c=json.loads(a.fixture.read_text());rows=c['cases'];ids=[r['id']for r in rows]
 if c.get('version')!=1 or c.get('macReference')!=BASELINE or len(ids)!=len(set(ids))or hashlib.sha256('\n'.join(sorted(ids)).encode()).hexdigest()!=ID_HASH:p.error('Pinned reviewed player matrix required')
 if any(r.get('macSource')!=PLAYER or r.get('provenanceKind')not in ['original-test-adaptation','source-policy-extension']for r in rows):p.error('Original player provenance required')
 paths=['Cotabby/Models/Context/LanguageCatalog.swift','Cotabby/Models/LanguageLearning/LearningLanguage.swift','Cotabby/Models/Runtime/OpenAICompatibleEndpointModels.swift','Cotabby/Models/Runtime/Parameters/ModelSamplingOverrides.swift','Cotabby/Models/Speech/LearningSpeechModels.swift','Cotabby/Models/Speech/SpeechAudioChunk.swift','Cotabby/Support/Speech/SpeechPCMPlaybackState.swift']
 renderer=source(a.mac_repo,'Cotabby/Services/Speech/SpeechPCMRenderer.swift');protocol=renderer[renderer.index('nonisolated protocol SpeechPCMRendering:'):renderer.index('/// One renderer belongs')]
 tests=source(a.mac_repo,TESTS);fake=tests[tests.index('private struct PCMTestTimeout:'):]
 adaptations=[r for r in rows if r['provenanceKind']=='original-test-adaptation']
 if len(adaptations)!=6 or len({r.get('sourceTest')for r in adaptations})!=6 or any('@Test func '+r.get('sourceTest','')+'('not in tests for r in adaptations):p.error('All six original player regressions must retain their actual test provenance')
 # Append read-only observers in each declaration's file: no private field or exercised body changes.
 values=[source(a.mac_repo,path)for path in paths]+[protocol,STUB,source(a.mac_repo,PLAYER)+OBSERVER.split('extension GatedPCMRenderer')[0],fake+'\nextension GatedPCMRenderer'+OBSERVER.split('extension GatedPCMRenderer')[1]+DRIVER]
 with tempfile.TemporaryDirectory(prefix='lingrove-speech-player-')as directory:
  root=Path(directory);files=[]
  for i,value in enumerate(values):
   f=root/f'source{i}.swift';f.write_text('import Foundation\n'+value);files.append(str(f))
  binary=root/'probe';subprocess.run(['swiftc','-swift-version','6','-O',*files,'-o',str(binary)],check=True)
  actual=json.loads(subprocess.check_output([str(binary),str(a.fixture),'-AppleLocale','en_US'],timeout=90))
 if a.capture_output:
  with a.capture_output.open('x',encoding='utf-8')as out:json.dump(actual,out,ensure_ascii=False,indent=2);out.write('\n')
  print(f'Captured {len(actual)} complete original PCM player traces');return
 if len(actual)!=len(rows):raise SystemExit('Player count changed')
 for row,value in zip(rows,actual):
  if value!={'id':row['id'],'expected':row['expected']}:raise SystemExit('Mac player mismatch: '+row['id'])
 print(f'Verified {len(rows)} complete original PCM player traces')
if __name__=='__main__':main()
