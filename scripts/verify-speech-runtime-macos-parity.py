#!/usr/bin/env python3
"""Run the complete pinned local speech runtime against its original real-process fixture."""
import argparse,collections,hashlib,json,re,subprocess,tempfile
from pathlib import Path
from macos_vocabulary_reference import BASELINE,source
RUNTIME='Cotabby/Services/Speech/LocalSpeechRuntime.swift'
TEST='CotabbyTests/Services/Speech/LocalSpeechRuntimeTests.swift'
METHOD_HASH='ac0ead0ad1c395cc06fe45439c867d9394a9048d3572aac79bd9d468df540cce'
ID_HASH='068fcafb0e9f9e2f8f91bd340f8b39af6b2bbf20805b4396a2cad48d730281b8'
OBSERVER=r'''
extension LocalSpeechRuntime { fileprivate func observedSession()->SpeechHelperSession?{session ?? drainingSession} }
extension SpeechHelperSession {
 fileprivate func joinObservedReader()async{await readTask?.value}
 fileprivate func joinObservedEvents()async{await eventDelivery?.value}
}
'''
DRIVER=r'''
import CryptoKit
actor Observations {
 var chunks:[Int]=[],results:[String]=[]
 var payload=Data()
 func append(_ c:SpeechPCMChunk){chunks.append(c.data.count);payload.append(c.data)}
 func success(_ data:Data){results.append("ok:"+String(data.count)+":"+SHA256.hash(data:data).map{String(format:"%02x",$0)}.joined())}
 func failure(_ cancelled:Bool=false){results.append(cancelled ? "cancelled":"failed")}
 func snapshot()->([Int],[String]){(chunks,results)}
}
actor RuntimeEvents {
 var events:[SpeechRuntimeEvent]=[]
 func append(_ e:SpeechRuntimeEvent){events.append(e)}
 func unloaded()->Bool{events.contains{$0.snapshot.phase == .unloaded}}
}
@MainActor func waitUntil(_ predicate:()async throws->Bool)async throws{
 let deadline=ContinuousClock.now.advanced(by:.seconds(15));while try await !predicate(){if ContinuousClock.now>deadline{fatalError("Runtime fixture barrier timed out")};try await Task.sleep(for:.milliseconds(5))}
}
private func lines(_ f:Fixture,_ file:String)->[String]{((try? String(contentsOf:f.root.appendingPathComponent(file),encoding:.utf8)) ?? "").split(separator:"\n").map(String.init)}
@main struct Main{
 static func main()async throws{
  guard Locale.current.identifier=="en_US"else{fatalError("Reference locale must be en_US")}
  let corpus=try JSONSerialization.jsonObject(with:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1])))as! [String:Any];var output:[[String:Any]]=[]
  for row in corpus["cases"]as! [[String:Any]]{
   let id=row["id"]as! String,f=try Fixture(),gate=SpeechEventGate(),events=RuntimeEvents(),observations=Observations()
   let runtime=LocalSpeechRuntime(helperURL:f.helper,idleTimeout:id=="idle" ? .milliseconds(30):.seconds(120),operationTimeout:id=="timeout" ? .seconds(2):.seconds(180),events:{e in await events.append(e);if id=="blocked-events"{await gate.wait()}})
   var requests:[SpeechAudioRequest]=[],sessions:[SpeechHelperSession]=[]
   func remember()async{if let s=await runtime.observedSession(){sessions.append(s)}}
   func collect(_ request:SpeechAudioRequest,_ config:SpeechLocalConfiguration?=nil)async throws->Data{
    let data=PCMCollector();try await runtime.stream(for:request,configuration:config ?? f.configuration){c in await observations.append(c);if id=="receiver-reject" && request.text=="reject"{throw SpeechError.invalidAudio};await data.append(c)}
    return await data.data
   }
   func run(_ text:String,_ config:SpeechLocalConfiguration?=nil,_ sampling:ModelSamplingOverrides?=nil)async{
    var r=f.request(text);if let sampling{r.sampling=sampling};requests.append(r)
    do{await observations.success(try await collect(r,config))}catch{await observations.failure()};await remember()
   }
   switch id{
   case "reuse":await run("first");await run("second")
   case "sampling":await run("first",nil,.init(temperature:0.1,topP:0.7,topK:12,repetitionPenalty:1.1,seedMode:.fixed,fixedSeed:81));await run("second")
   case "sampling-bounds":await run("你好，héllo.\nNext",nil,.init(temperature:9,topP:-1,topK:999,repetitionPenalty:-1,seedMode:.random,fixedSeed:81))
   case "reference":let config=SpeechLocalConfiguration(modelURL:f.configuration.modelURL,projectionURL:f.configuration.projectionURL,referenceAudioURL:f.root.appendingPathComponent("speaker 声音.wav"));await run("reference",config)
   case "configuration":await run("first");let config=SpeechLocalConfiguration(modelURL:f.root.appendingPathComponent("different.gguf"),projectionURL:f.configuration.projectionURL,referenceAudioURL:nil);await run("second",config)
   case "unsupported":let r=SpeechAudioRequest(token:SpeechPlaybackToken(),text:"unsupported",language:.dutch,source:.local(f.configuration));requests.append(r);do{await observations.success(try await collect(r))}catch let e as SpeechError{await observations.failure();guard case .unsupportedLanguage=e else{throw e}}
   case "pre-cancel":let r=f.request("pre-cancel");requests.append(r);let job=Task{withUnsafeCurrentTask{$0?.cancel()};return try await collect(r)};do{await observations.success(try await job.value)}catch is CancellationError{await observations.failure(true)}
   case "idle":await run("first");try await waitUntil{await events.unloaded()};await run("second")
   case "timeout":await run("ignore-term");await run("after")
   case "cancel","stream-cancel","blocked-receiver","cancel-drain":
    let text=id=="stream-cancel" ? "chunk-then-wait":id=="blocked-receiver" ? "first":id=="cancel-drain" ? "ignore-term":"wait",r=f.request(text);requests.append(r)
    let job=Task{let data=PCMCollector();try await runtime.stream(for:r,configuration:f.configuration){c in await observations.append(c);await data.append(c);if id=="blocked-receiver"{await gate.wait()}};return await data.data}
    if id=="blocked-receiver"{try await waitUntil{await gate.entered}}
    else if id=="stream-cancel"{try await waitUntil{await observations.snapshot().0.count>0}}
    else{try await waitUntil{lines(f,"requests").contains(text)}}
    await remember()
    if id=="cancel-drain"{
     let second=f.request("second");requests.append(second);let next=Task{try await collect(second)}
     try await Task.sleep(for:.milliseconds(150));next.cancel()
     do{await observations.success(try await next.value)}catch is CancellationError{await observations.failure(true)}
     do{await observations.success(try await job.value)}catch{await observations.failure()}
     await run("after")
    }else{
     if id != "blocked-receiver"{job.cancel()};await runtime.cancel(r.token)
     do{await observations.success(try await job.value)}catch{await observations.failure()}
     if id != "stream-cancel"{await run("after")}
    }
    await gate.release()
   case "blocked-events":await run("first");await runtime.shutdown();await gate.release()
   case "receiver-reject":await run("reject");await run("after")
   default:await run(id);await run("after")
   }
   await remember();await runtime.shutdown();await gate.release()
   for session in sessions{await session.joinObservedReader();await session.joinObservedEvents()}
   let raw=try lines(f,"parameters").map{try JSONSerialization.jsonObject(with:Data($0.utf8))as! [String:Any]}
   let known=Dictionary(uniqueKeysWithValues:requests.map{($0.text,$0.token.rawValue.uuidString)});var parameters:[[String:Any]]=[]
   for var r in raw{let emitted=r.removeValue(forKey:"id")as? String;r["matchingId"]=emitted != nil && known[r["text"]as? String ?? ""]==emitted;if let p=r["speaker_audio_path"]as? String{r["speaker_audio_path"]="$ROOT/"+URL(fileURLWithPath:p).lastPathComponent};parameters.append(r)}
   let state=await observations.snapshot(),pids=lines(f,"starts").compactMap(Int32.init)
   let value:[String:Any]=["results":state.1,"chunks":state.0,"starts":pids.count,"reaped":pids.allSatisfy{kill($0,0) != 0},"parameters":parameters]
   output.append(["id":id,"expected":value]);try FileManager.default.removeItem(at:f.root)
  }
  FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject:output,options:[.sortedKeys]))
 }
}
'''
def rows(fixture,repo):
 assert fixture['version']==1 and fixture['macReference']==BASELINE and fixture['referenceLocale']=='en_US'
 values=fixture['cases'];ids=[r['id']for r in values]
 assert len(ids)==17 and len(set(ids))==17 and hashlib.sha256('\n'.join(sorted(ids)).encode()).hexdigest()==ID_HASH
 assert collections.Counter(r['provenanceKind']for r in values)=={'original-test-adaptation':11,'source-policy-extension':6}
 methods=set()
 for r in values:
  assert r['macSource']==RUNTIME
  if r['provenanceKind']=='original-test-adaptation':
   path,method=r['sourceTest'].split(':');assert path==TEST and re.search(r'func\s+'+re.escape(method)+r'\s*\(',source(repo,path));methods.add(method)
  else:assert not r.get('sourceTest')
 assert len(methods)==10
 assert hashlib.sha256("\n".join(sorted(r["id"]+"|"+r["sourceTest"]for r in values if "sourceTest"in r)).encode()).hexdigest()==METHOD_HASH
 return values

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--mac-repo',required=True,type=Path);p.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/speech-runtime-v1.json');p.add_argument('--capture-output',type=Path);a=p.parse_args();cases=rows(json.loads(a.fixture.read_text()),a.mac_repo)
 tests=source(a.mac_repo,TEST);start=tests.index('    private struct Fixture {');end=tests.index('\n    }\n}\n\n/// A deliberately blocked observer',start)+len('\n    }');fixture=tests[start:end]
 helpers=tests[tests.index('private actor SpeechEventGate {'):]
 # Complete unchanged source; diagnostic observers and the verbatim private fixture share its
 # file so Swift private access rules remain real. No runtime method or protocol rule is replaced.
 paths=['Cotabby/Models/Context/LanguageCatalog.swift','Cotabby/Models/LanguageLearning/LearningLanguage.swift','Cotabby/Models/Runtime/Parameters/ModelSamplingOverrides.swift','Cotabby/Models/Runtime/Parameters/ModelSamplingValues.swift','Cotabby/Models/Runtime/OpenAICompatibleEndpointModels.swift','Cotabby/Models/Speech/LearningSpeechModels.swift','Cotabby/Models/Speech/SpeechRuntimeModels.swift','Cotabby/Models/Speech/SpeechAudioChunk.swift','Cotabby/Support/Speech/SpeechAudioFrameDecoder.swift','Cotabby/Services/Speech/SpeechEndpointClient.swift']
 text=source(a.mac_repo,'Cotabby/Support/Speech/SpeechTextPolicy.swift')
 values=[source(a.mac_repo,path)for path in paths]+[text[text.index('nonisolated enum LocalSpeechLanguage {'):]]+[source(a.mac_repo,RUNTIME)+OBSERVER+fixture+'\n'+helpers+DRIVER]
 with tempfile.TemporaryDirectory(prefix='lingrove-speech-runtime-')as directory:
  root=Path(directory);files=[]
  for i,value in enumerate(values):
   f=root/f'source{i}.swift';f.write_text('import Foundation\n'+value);files.append(str(f))
  binary=root/'probe';subprocess.run(['swiftc','-swift-version','6','-O',*files,'-o',str(binary)],check=True)
  actual=json.loads(subprocess.check_output([str(binary),str(a.fixture),'-AppleLocale','en_US'],timeout=120))
 assert len(actual)==len(cases) and [r['id']for r in actual]==[r['id']for r in cases]
 if a.capture_output:
  with a.capture_output.open('x',encoding='utf-8')as out:json.dump(actual,out,ensure_ascii=False,indent=2);out.write('\n')
  print(f'Captured {len(actual)} original local speech process observations');return
 for row,value in zip(cases,actual):
  if value!={'id':row['id'],'expected':row['expected']}:raise SystemExit('Runtime mismatch: '+row['id']+'\n'+json.dumps(value,ensure_ascii=False))
 print(f'Verified {len(actual)} original local speech process observations')
if __name__=='__main__':main()
