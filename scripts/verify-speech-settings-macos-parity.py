#!/usr/bin/env python3
"""Execute complete original speech preferences/profile storage and destination identity."""
import argparse,hashlib,json,subprocess,tempfile
from pathlib import Path
from macos_vocabulary_reference import BASELINE,source
SETTINGS='Cotabby/Models/Speech/LearningSpeechSettings.swift'
IDENTITY='Cotabby/Support/Prompting/ModelContextBudget.swift'
ID_HASH='2c31a6bcfade261b924c5899cae9cb0937ecbe5a428568eae4b0cf78b2580515'
DRIVER=r'''
import Combine
import Foundation
let keys=["provider","localModel","localProjection","referenceAudio","endpointURL","endpointModel","endpointVoice","rateMultiplier","voiceIDs","parameters.v1"].map{"learningSpeech."+$0}
func text(_ r:[String:Any],_ k:String,_ fallback:String="")->String{r[k] as? String ?? fallback}
func scalar(_ value:Any?)->Double{if let v=value as? NSNumber{return v.doubleValue};switch value as? String{case "nan":return .nan;case "infinity":return .infinity;case "-infinity":return -.infinity;default:fatalError("Invalid scalar fixture")}}
func sampler(_ r:[String:Any])->ModelSamplingOverrides{.init(temperature:r["temperature"].map(scalar),topP:r["topP"].map(scalar),topK:r["topK"] as? Int,repetitionPenalty:r["repetitionPenalty"].map(scalar),seedMode:.init(rawValue:text(r,"seedMode","recommended"))!,fixedSeed:(r["fixedSeed"] as? NSNumber)?.uint32Value ?? 42)}
func identity(_ r:[String:Any])->String{ModelContextBudget.identity(backend:text(r,"backend"),model:text(r,"model"),baseURL:text(r,"baseURL"),mode:text(r,"mode"))}
func encoded<T:Encodable>(_ value:T)->Any{try! JSONSerialization.jsonObject(with:JSONEncoder().encode(value))}
func identityFields(_ value:String)->Any{(try? JSONDecoder().decode([String].self,from:Data(value.utf8))) as Any? ?? value}
@MainActor final class Credentials:OpenAICompatibleCredentialStoring{
 let inner=InMemoryOpenAICompatibleCredentialStore();var reads=0,saves=0;var fail=false
 func readAPIKey()throws->String?{reads+=1;if fail{throw ProbeFailure()};return try inner.readAPIKey()}
 func saveAPIKey(_ value:String?)throws{saves+=1;if fail{throw ProbeFailure()};try inner.saveAPIKey(value)}
 func deleteAPIKey()throws{try inner.deleteAPIKey()}
}
struct ProbeFailure:LocalizedError{var errorDescription:String?{"fixture credential failure"}}
@MainActor func snapshot(_ s:LearningSpeechSettings,_ d:UserDefaults,_ c:Credentials)->[String:Any]{
 var raw:[String:Any]=[:];for key in keys{if let value=d.object(forKey:key){
  if let bytes=value as? Data{
   if let profiles=try? JSONSerialization.jsonObject(with:bytes) as? [String:Any]{raw[key]=["profiles":profiles.keys.sorted().map{["identity":identityFields($0),"profile":profiles[$0]!] as [String:Any]}]}
   else{raw[key]=["dataBase64":bytes.base64EncodedString()]}
  }else{raw[key]=value}
 }}
 return ["provider":s.selectedProvider.rawValue,"localModel":s.localModelPath,"localProjection":s.localProjectionPath,"referenceAudio":s.referenceAudioPath,"endpointURL":s.endpointBaseURL,"endpointModel":s.endpointModel,"endpointVoice":s.endpointVoice,"rate":s.rateMultiplier,"voices":s.voiceIDs,"identity":identityFields(s.parameterIdentity),"profile":encoded(s.generationProfile),"raw":raw,"keyReads":c.reads,"keySaves":c.saves]
}
func normalize(_ value:Any,_ root:String)->Any{
 if let s=value as? String{return s.replacingOccurrences(of:root,with:"$ASSETS")}
 if let a=value as? [Any]{return a.map{normalize($0,root)}}
 if let d=value as? [String:Any]{return d.mapValues{normalize($0,root)}}
 return value
}
@main struct Main{
 @MainActor static func main()throws{
  guard Locale.current.identifier=="en_US" else{fatalError("Speech settings reference locale must be en_US")}
  let corpus=try JSONSerialization.jsonObject(with:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1]))) as! [String:Any]
  var results:[[String:Any]]=[]
  for input in corpus["cases"] as! [[String:Any]]{
   if text(input,"scope")=="identity"{results.append(["id":input["id"]!,"expected":identity(input)]);continue}
   let name="lingrove-speech-settings-"+UUID().uuidString,d=UserDefaults(suiteName:name)!,c=Credentials()
   d.removePersistentDomain(forName:name);defer{d.removePersistentDomain(forName:name)}
   let root=FileManager.default.temporaryDirectory.appendingPathComponent(name,isDirectory:true).standardizedFileURL
   try FileManager.default.createDirectory(at:root,withIntermediateDirectories:true);defer{try? FileManager.default.removeItem(at:root)}
   for file in input["files"] as? [[String:Any]] ?? []{
    let url=root.appendingPathComponent(text(file,"path"));_ = FileManager.default.createFile(atPath:url.path,contents:Data())
    let handle=try FileHandle(forWritingTo:url);try handle.truncate(atOffset:(file["size"] as! NSNumber).uint64Value);try handle.close()
   }
   func expand(_ value:Any)->Any{if let s=value as? String{return s.replacingOccurrences(of:"$ASSETS",with:root.path)};if let a=value as? [Any]{return a.map(expand)};if let v=value as? [String:Any]{return v.mapValues(expand)};return value}
   let r=expand(input) as! [String:Any]
   for(key,value)in r["seed"] as? [String:Any] ?? [:]{if let descriptor=value as? [String:Any],let data=descriptor["$data"] as? String{d.set(Data(base64Encoded:data)!,forKey:key)}else{d.set(value,forKey:key)}}
   if let specs=r["seedProfiles"] as? [[String:Any]]{var map:[String:Any]=[:];for p in specs{map[identity(p)]=p["profile"]!};d.set(try JSONSerialization.data(withJSONObject:map),forKey:"learningSpeech.parameters.v1")}
   var s=LearningSpeechSettings(defaults:d,credentialStore:c),invalidations:[[String:Any]]=[],remembered:[String:String]=[:]
   var token=s.playbackInvalidations.sink{invalidations.append(snapshot(s,d,c))}
   func observation(_ outcome:Any)->[String:Any]{["state":snapshot(s,d,c),"invalidations":invalidations,"result":outcome]}
   var trace:[[String:Any]]=[observation(NSNull())]
   for a in r["actions"] as? [[String:Any]] ?? []{
    var result:Any=NSNull();c.fail=a["fail"] as? Bool ?? false
    do{switch text(a,"op"){
     case "provider":s.selectProvider(.init(rawValue:text(a,"value"))!)
     case "assets":s.setLocalAssets(model:text(a,"model"),projection:text(a,"projection"))
     case "model":s.setLocalModelPath(text(a,"value"))
     case "projection":s.setLocalProjectionPath(text(a,"value"))
     case "reference":s.setReferenceAudioPath(text(a,"value"))
     case "endpointURL":s.setEndpointBaseURL(text(a,"value"))
     case "endpointModel":s.setEndpointModel(text(a,"value"))
     case "endpointVoice":s.setEndpointVoice(text(a,"value"))
     case "rate":s.setRateMultiplier(scalar(a["value"]))
     case "voice":s.setVoiceID(a["value"] as? String,for:.init(rawValue:text(a,"language"))!)
     case "rememberIdentity":remembered[text(a,"name")]=s.parameterIdentity
     case "sampling":s.setSamplingOverrides(sampler(a["value"] as! [String:Any]),expectedIdentity:(a["expected"] as? String).flatMap{remembered[$0]})
     case "normalizeCurrent":s.setSamplingOverrides(s.generationProfile.sampling)
     case "saveKey":try s.saveEndpointAPIKey(a["value"] as? String)
     case "readKey":result=["key":try s.endpointAPIKey() as Any? ?? NSNull()]
     case "localConfiguration":let v=try s.localConfiguration();result=["model":v.modelURL.standardizedFileURL.path,"projection":v.projectionURL.standardizedFileURL.path,"reference":v.referenceAudioURL?.standardizedFileURL.path as Any? ?? NSNull()]
     case "endpointConfiguration":let v=try s.endpointConfiguration();result=["baseURL":v.endpoint.baseURL.absoluteString,"model":v.endpoint.modelName,"voice":v.voice,"apiMode":v.endpoint.apiMode.rawValue]
     case "restart":token.cancel();s=LearningSpeechSettings(defaults:d,credentialStore:c);token=s.playbackInvalidations.sink{invalidations.append(snapshot(s,d,c))}
     default:fatalError("Unknown settings action")
    }}catch let e as OpenAICompatibleEndpointError{result=["endpointError":String(describing:e)]}catch{result=["error":error.localizedDescription]}
    trace.append(observation(result))
   }
   token.cancel();results.append(["id":r["id"]!,"expected":normalize(trace,root.path)])
  }
  FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject:results,options:[.sortedKeys]))
 }
}
'''
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--mac-repo',type=Path,required=True);p.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/speech-settings-v1.json');p.add_argument('--capture-output',type=Path)
 a=p.parse_args();c=json.loads(a.fixture.read_text());rows=c['cases'];ids=[r['id'] for r in rows]
 if c.get('version')!=1 or c.get('macReference')!=BASELINE or c.get('referenceLocale')!='en_US' or len(ids)!=len(set(ids)) or hashlib.sha256('\n'.join(sorted(ids)).encode()).hexdigest()!=ID_HASH:p.error('Pinned reviewed speech settings matrix required')
 if any(r.get('scope')not in ['settings','identity'] or r.get('macSource')!=(SETTINGS if r.get('scope')=='settings' else IDENTITY) or r.get('provenanceKind')not in ['source-policy-extension','original-test-adaptation'] for r in rows):p.error('Original settings/identity provenance required')
 paths=['Cotabby/Models/Context/LanguageCatalog.swift','Cotabby/Models/LanguageLearning/LearningLanguage.swift','Cotabby/Models/Runtime/OpenAICompatibleEndpointModels.swift','Cotabby/Models/Runtime/Parameters/ModelSamplingOverrides.swift','Cotabby/Models/Runtime/Parameters/ModelParameterSettings.swift','Cotabby/Models/Speech/LearningSpeechModels.swift','Cotabby/Models/Speech/SpeechGenerationProfile.swift','Cotabby/Support/Speech/SpeechModelPairingPolicy.swift','Cotabby/Support/Prompting/TokenCountEstimator.swift','Cotabby/Support/LanguageLearning/LearningFailure.swift',IDENTITY,'Cotabby/Services/Runtime/OpenAICompatible/OpenAICompatibleCredentialStore.swift',SETTINGS]
 with tempfile.TemporaryDirectory(prefix='lingrove-speech-settings-') as directory:
  root=Path(directory);files=[]
  for i,value in enumerate([source(a.mac_repo,path) for path in paths]+[DRIVER]):
   f=root/f'source{i}.swift';f.write_text(value);files.append(str(f))
  binary=root/'probe';subprocess.run(['swiftc','-swift-version','6','-O',*files,'-o',str(binary)],check=True)
  actual=json.loads(subprocess.check_output([str(binary),str(a.fixture),'-AppleLocale','en_US'],timeout=60))
 if a.capture_output:
  with a.capture_output.open('x',encoding='utf-8')as out:json.dump(actual,out,ensure_ascii=False,indent=2);out.write('\n')
  print(f'Captured {len(actual)} original speech settings/identity observations');return
 if len(actual)!=len(rows):raise SystemExit('Speech settings count changed')
 for r,v in zip(rows,actual):
  if v!={'id':r['id'],'expected':r['expected']}:raise SystemExit('Mac speech settings mismatch: '+r['id'])
 print(f'Verified {len(rows)} original speech settings/identity observations')
if __name__=='__main__':main()
