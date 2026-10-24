#!/usr/bin/env python3
"""Run complete original speech telemetry and bounded history store over isolated preferences."""
import argparse,hashlib,json,subprocess,tempfile
from pathlib import Path
from macos_vocabulary_reference import BASELINE,source
TELEMETRY='Cotabby/Services/Speech/SpeechTelemetry.swift'
STORE='Cotabby/Models/Runtime/Metrics/PerformanceMetricsStore.swift'
ID_HASH='a7ab20a4021b11b01f253e88eb9f0a03f9c1ebfaf86a36de1258718cb8358cbd'
DRIVER=r'''
import Foundation
func text(_ r:[String:Any],_ key:String,_ fallback:String="")->String{r[key] as? String ?? fallback}
func integer(_ r:[String:Any],_ key:String,_ fallback:Int=0)->Int{(r[key] as? NSNumber)?.intValue ?? fallback}
func encoded<T:Encodable>(_ v:T)->Any{try! JSONSerialization.jsonObject(with:JSONEncoder().encode(v))}
func uuid(_ n:Int)->UUID{UUID(uuidString:String(format:"00000000-0000-0000-0000-%012x",n))!}
@MainActor func state(_ telemetry:SpeechTelemetry,_ store:PerformanceMetricsStore?,_ d:UserDefaults,_ events:[[String:Any]],_ ids:[UUID],_ reads:Int)->[String:Any]{
 let runtime=telemetry.runtime
 var raw:Any=NSNull();if let data=d.data(forKey:"cotabbyPerformanceMetricEntries"){raw=(try? JSONSerialization.jsonObject(with:data)) as Any? ?? ["dataBase64":data.base64EncodedString()]}
 return ["runtime":["sessionID":runtime.sessionID?.uuidString as Any? ?? NSNull(),"generation":String(runtime.generation),"phase":runtime.phase.rawValue,"label":runtime.phase.label,"processID":runtime.processID as Any? ?? NSNull(),"modelName":runtime.modelName,"files":runtime.loadedModelFilenames.sorted()],"entries":store.map{encoded($0.entries)} as Any? ?? NSNull(),"raw":raw,"events":events,"ids":ids.map(\.uuidString),"trackingReads":reads]
}
@main struct Main{
 @MainActor static func main()throws{
  guard Locale.current.identifier=="en_US"else{fatalError("Telemetry source locale must be en_US")}
  let corpus=try JSONSerialization.jsonObject(with:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1]))) as! [String:Any];var results:[[String:Any]]=[]
  for row in corpus["cases"] as! [[String:Any]]{
   if text(row,"scope")=="metadata"{
    results.append(["id":row["id"]!,"expected":["sources":ModelRequestSource.allCases.map{["id":$0.rawValue,"label":$0.label]},"analysis":ModelRequestSource.analysis.label,"outcomes":[ModelRequestOutcome.completed,.failed,.cancelled].map{["id":$0.rawValue,"label":$0.label]}]]);continue
   }
   let name="lingrove-speech-telemetry-"+UUID().uuidString,d=UserDefaults(suiteName:name)!;d.removePersistentDomain(forName:name);defer{d.removePersistentDomain(forName:name)}
   if let raw=row["seedBase64"] as? String{d.set(Data(base64Encoded:raw)!,forKey:"cotabbyPerformanceMetricEntries")}
   var store:PerformanceMetricsStore?=row["noMetrics"] as? Bool==true ? nil:PerformanceMetricsStore(userDefaults:d)
   var clock:TimeInterval=10,tracking=row["tracking"] as? Bool ?? false,reads=0,events:[[String:Any]]=[],ids:[UUID]=[],tokens:[String:SpeechPlaybackToken]=[:],sessions:[String:UUID]=[:]
   func token(_ name:String)->SpeechPlaybackToken{if let v=tokens[name]{return v};let v=SpeechPlaybackToken(rawValue:uuid(tokens.count+1));tokens[name]=v;return v}
   func session(_ name:String)->UUID{if let v=sessions[name]{return v};let v=uuid(sessions.count+1000);sessions[name]=v;return v}
   let telemetry=SpeechTelemetry(metrics:store,trackingEnabled:{reads+=1;return tracking},now:{clock},event:{context,phase,details in events.append(["context":["requestID":context.requestID,"operationID":context.operationID,"source":context.source.rawValue,"modelName":context.modelName,"engine":context.engine],"phase":phase,"details":details])})
   func begin(_ name:String,_ model:String){telemetry.begin(token(name),model:model,engine:"llama",language:.english)}
   var trace:[[String:Any]]=[state(telemetry,store,d,events,ids,reads)]
   for a in row["actions"] as! [[String:Any]]{
    switch text(a,"op"){
    case "clock":clock=(a["value"] as! NSNumber).doubleValue
    case "tracking":tracking=a["value"] as! Bool
    case "begin":begin(text(a,"token"),text(a,"model","model"))
    case "finish":telemetry.finish(token(text(a,"token")),outcome:.init(rawValue:text(a,"outcome","completed"))!,errorType:a["errorType"] as? String)
    case "generated":telemetry.generated(token(text(a,"token")),bytes:integer(a,"bytes"))
    case "generation":telemetry.generationCompleted(token(text(a,"token")))
    case "started":telemetry.playbackStarted(token(text(a,"token")))
    case "prepared":telemetry.playbackPrepared(token(text(a,"token")),milliseconds:integer(a,"milliseconds"))
    case "many":for i in 0..<integer(a,"count"){let name="t\(i)";begin(name,"model\(i)");telemetry.finish(token(name),outcome:.completed)}
    case "runtime":
     let snapshot=SpeechRuntimeSnapshot(sessionID:(a["session"] as? String).map(session),generation:UInt64(text(a,"generation","0"))!,phase:.init(rawValue:text(a,"phase","loading"))!,processID:(a["processID"] as? NSNumber).map{Int32($0.intValue)},modelName:text(a,"model","model"),loadedModelFilenames:Set(a["files"] as? [String] ?? []))
     telemetry.receive(.init(snapshot:snapshot,token:(a["token"] as? String).map(token),sequence:UInt64(text(a,"sequence","0"))!,modelLoadMs:(a["load"] as? NSNumber)?.intValue,reason:a["reason"] as? String))
    case "record":ids.append(store!.record(modelName:text(a,"model","model"),latencyMs:integer(a,"latency",300),timestamp:Date(timeIntervalSinceReferenceDate:(a["timestamp"] as? NSNumber)?.doubleValue ?? 771692800),source:.init(rawValue:text(a,"source","completion"))!,outcome:.init(rawValue:text(a,"outcome","completed"))!,requestID:a["requestID"] as? String,modelLoadMs:(a["load"] as? NSNumber)?.intValue,firstOutputMs:(a["first"] as? NSNumber)?.intValue,usableResultMs:(a["usable"] as? NSNumber)?.intValue,displayMs:(a["display"] as? NSNumber)?.intValue))
    case "recordMany":for i in 0..<integer(a,"count"){ids.append(store!.record(modelName:"model",latencyMs:i,timestamp:Date(timeIntervalSinceReferenceDate:771692800+Double(i))))}
    case "load","display":let index=integer(a,"index"),id=index<0 ? uuid(999):ids[index];if text(a,"op")=="load"{store!.recordLoad(entryID:id,elapsedMs:integer(a,"elapsed"))}else{store!.recordDisplay(entryID:id,elapsedMs:integer(a,"elapsed"))}
    case "clear":store!.clear()
    case "restartStore":store=PerformanceMetricsStore(userDefaults:d)
    default:fatalError("Unknown telemetry action")
    }
    trace.append(state(telemetry,store,d,events,ids,reads))
   }
   results.append(["id":row["id"]!,"expected":trace])
  }
  FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject:results,options:[.sortedKeys]))
 }
}
'''
def normalize(value,scope):
 ids=set();requests=set()
 def collect(v):
  if isinstance(v,dict):
   if 'id'in v and 'modelName'in v and 'timestamp'in v and not v['id'].startswith('00000000-'):ids.add(v['id'])
   if isinstance(v.get('ids'),list):ids.update(x for x in v['ids'] if not x.startswith('00000000-'))
   if 'requestID'in v and 'operationID'in v:requests.update([v['requestID'],v['operationID']])
   for x in v.values():collect(x)
  elif isinstance(v,list):
   for x in v:collect(x)
 collect(value);uuid_map={};request_map={}
 def visit(v):
  if isinstance(v,dict):
   return {k:('captured'if scope=='telemetry'and k=='timestamp'and 'id'in v and 'modelName'in v else visit(v[k]))for k in sorted(v)}
  if isinstance(v,list):return [visit(x)for x in v]
  if isinstance(v,str):
   if v in ids:return uuid_map.setdefault(v,'entry-'+str(len(uuid_map)+1))
   if v in requests:return request_map.setdefault(v,'request-'+str(len(request_map)+1))
  return v
 return visit(value)
def sources(repo):
 paths=['Cotabby/Models/Context/LanguageCatalog.swift','Cotabby/Models/LanguageLearning/LearningLanguage.swift','Cotabby/Models/Runtime/OpenAICompatibleEndpointModels.swift','Cotabby/Models/Runtime/Parameters/ModelSamplingOverrides.swift','Cotabby/Models/Speech/LearningSpeechModels.swift','Cotabby/Models/Speech/SpeechRuntimeModels.swift','Cotabby/Models/Runtime/Metrics/ModelRequestMetrics.swift','Cotabby/Support/Logging/RequestID.swift']
 values=[source(repo,path)for path in paths]
 context=source(repo,'Cotabby/Models/LanguageLearning/LearningRequestContext.swift');values.append(context[:context.index('/// Task-local scopes')])
 telemetry=source(repo,TELEMETRY);line='            CotabbyLogger.speech.info("Speech \\($1)", metadata: LearningRequestLog.metadata($0, phase: $1, details: $2))'
 if telemetry.count('import Logging\n')!=1 or telemetry.count(line)!=1:raise RuntimeError('Pinned telemetry diagnostic boundary changed')
 values.append(telemetry.replace('import Logging\n','').replace(line,'            _ = ($0, $1, $2); fatalError("Reference driver must inject the event sink")'))
 store=source(repo,STORE);diagnostic='''            CotabbyLogger.app.error(
                "Failed to persist performance metrics: \\(error.localizedDescription)",
                metadata: ["entry_count": .stringConvertible(entries.count)]
            )'''
 if store.count('import Logging\n')!=1 or store.count(diagnostic)!=1:raise RuntimeError('Pinned history diagnostic boundary changed')
 values.append(store.replace('import Logging\n','').replace(diagnostic,'            fatalError("Unexpected primitive reference encoding failure")'))
 return values

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--mac-repo',type=Path,required=True);p.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/speech-telemetry-v1.json');p.add_argument('--capture-output',type=Path)
 a=p.parse_args();c=json.loads(a.fixture.read_text());rows=c['cases'];ids=[r['id']for r in rows]
 if c.get('version')!=1 or c.get('macReference')!=BASELINE or c.get('referenceLocale')!='en_US' or len(ids)!=len(set(ids))or hashlib.sha256('\n'.join(sorted(ids)).encode()).hexdigest()!=ID_HASH:p.error('Pinned reviewed telemetry/history matrix required')
 if any(r.get('scope')not in ['telemetry','store','metadata'] or r.get('macSource')!=(TELEMETRY if r.get('scope')=='telemetry' else 'Cotabby/Models/Runtime/Metrics/ModelRequestMetrics.swift' if r.get('scope')=='metadata' else STORE)or r.get('provenanceKind')not in ['original-test-adaptation','source-policy-extension']for r in rows):p.error('Original telemetry/history provenance required')
 with tempfile.TemporaryDirectory(prefix='lingrove-speech-telemetry-')as directory:
  root=Path(directory);files=[]
  for i,value in enumerate(sources(a.mac_repo)+[DRIVER]):
   f=root/f'source{i}.swift';f.write_text(value);files.append(str(f))
  binary=root/'probe';subprocess.run(['swiftc','-swift-version','6','-O',*files,'-o',str(binary)],check=True)
  actual=json.loads(subprocess.check_output([str(binary),str(a.fixture),'-AppleLocale','en_US'],timeout=60))
 for row,value in zip(rows,actual):value['expected']=normalize(value['expected'],row['scope'])
 if a.capture_output:
  with a.capture_output.open('x',encoding='utf-8')as out:json.dump(actual,out,ensure_ascii=False,indent=2);out.write('\n')
  print(f'Captured {len(actual)} original speech telemetry/history observations');return
 if len(actual)!=len(rows):raise SystemExit('Telemetry count changed')
 for row,value in zip(rows,actual):
  if value!={'id':row['id'],'expected':row['expected']}:raise SystemExit('Mac telemetry mismatch: '+row['id'])
 print(f'Verified {len(rows)} original speech telemetry/history observations')
if __name__=='__main__':main()
