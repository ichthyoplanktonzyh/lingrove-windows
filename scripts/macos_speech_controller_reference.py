"""Explicit value/SDK observations for the complete pinned speech controller reference runner."""
DRIVER = r'''
import Foundation
func text(_ row:[String:Any],_ key:String,_ fallback:String="")->String{row[key] as? String ?? fallback}
func number(_ row:[String:Any],_ key:String,_ fallback:Int=0)->Int{(row[key] as? NSNumber)?.intValue ?? fallback}
func scalar(_ value:Any)->Double{
 if let v=value as? NSNumber{return v.doubleValue}
 switch value as? String{case "nan":return .nan;case "infinity":return .infinity;case "-infinity":return -.infinity;case "-0":return -0.0;default:fatalError("Unknown scalar")}
}
func sampling(_ row:[String:Any])->ModelSamplingOverrides{.init(temperature:row["temperature"].map(scalar),topP:row["topP"].map(scalar),topK:row["topK"] as? Int,repetitionPenalty:row["repetitionPenalty"].map(scalar),seedMode:.init(rawValue:text(row,"seedMode","recommended"))!,fixedSeed:(row["fixedSeed"] as? NSNumber)?.uint32Value ?? 42)}
func profile(_ row:[String:Any])->SpeechGenerationProfile{.init(sampling:sampling(row["sampling"] as? [String:Any] ?? [:]),referenceAudioPath:row["referenceAudioPath"] as? String,endpointVoice:row["endpointVoice"] as? String)}
func encoded<T:Encodable>(_ value:T)throws->Data{let encoder=JSONEncoder();encoder.outputFormatting=[.sortedKeys];return try encoder.encode(value)}
@MainActor func apply(_ a:[String:Any],_ settings:LearningSpeechSettings)throws{
 switch text(a,"op"){
 case "provider":settings.selectProvider(.init(rawValue:text(a,"value"))!)
 case "assets":settings.setLocalAssets(model:text(a,"model"),projection:text(a,"projection"))
 case "model":settings.setLocalModelPath(text(a,"value"))
 case "projection":settings.setLocalProjectionPath(text(a,"value"))
 case "reference":settings.setReferenceAudioPath(text(a,"value"))
 case "endpointURL":settings.setEndpointBaseURL(text(a,"value"))
 case "endpointModel":settings.setEndpointModel(text(a,"value"))
 case "endpointVoice":settings.setEndpointVoice(text(a,"value"))
 case "rate":settings.setRateMultiplier(scalar(a["value"]!))
 case "voice":settings.setVoiceID(a["value"] as? String,for:.init(rawValue:text(a,"language"))!)
 case "sampling":settings.setSamplingOverrides(sampling(a["value"] as! [String:Any]))
 case "saveKey":try settings.saveEndpointAPIKey(a["value"] as? String)
 default:fatalError("Unknown settings action")
 }
}
@main struct Main {
 @MainActor static func main()async throws{
  guard Locale.current.identifier=="en_US"else{fatalError("Controller locale must be en_US")}
  let corpus=try JSONSerialization.jsonObject(with:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1]))) as! [String:Any];var result:[[String:Any]]=[]
  for row in corpus["cases"] as! [[String:Any]]{
   if text(row,"scope")=="profile"{
    var value:[String:Any]
    do{let bytes=try encoded(profile(row["profile"] as! [String:Any]));value=["json":String(data:bytes,encoding:.utf8)!,"digest":SpeechExportPolicy.digest(bytes)]}catch{value=["error":"encodingFailed"]}
    result.append(["id":row["id"]!,"expected":value]);continue
   }
   if text(row,"scope")=="controller"{result.append(["id":row["id"]!,"expected":try await controllerFlow(row)]);continue}
   let name="lingrove-controller-"+UUID().uuidString,d=UserDefaults(suiteName:name)!;d.removePersistentDomain(forName:name);defer{d.removePersistentDomain(forName:name)}
   for(key,value)in row["seed"] as? [String:Any] ?? [:]{d.set(value,forKey:key)}
   let settings=LearningSpeechSettings(defaults:d,credentialStore:InMemoryOpenAICompatibleCredentialStore())
   let provider=FakeSpeechProvider(),generator=PendingSpeechGenerator(),pcm=TestPCMPlayer(),telemetry=SpeechTelemetry(event:{_,_,_ in})
   let c=LearningSpeechController(settings:settings,provider:provider,generator:generator,telemetry:telemetry,pcmPlayer:pcm,modelDirectoryURL:FileManager.default.temporaryDirectory)
   if text(row,"scope")=="identity"{
    func observation()->[String:Any]{["configurationID":c.exportConfigurationID,"profileJSON":(try? encoded(settings.generationProfile)).flatMap{String(data:$0,encoding:.utf8)} as Any? ?? NSNull(),"revision":String(c.observedExportRevision())]}
    var trace:[[String:Any]]=[observation()]
    for a in row["actions"] as! [[String:Any]]{try apply(a,settings);trace.append(observation())}
    result.append(["id":row["id"]!,"expected":trace]);c.shutdown();await generator.shutdown();continue
   }
   fatalError("Controller flow driver is still under construction")
  }
  FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject:result,options:[.sortedKeys]))
 }
}
'''
OBSERVER = r'''
extension LearningSpeechController {
 func observedExportRevision()->UInt64{exportRevision}
 func observedActiveToken()->SpeechPlaybackToken?{activeToken}
 func observedActiveRequest()->SpeechRequest?{activeRequest}
 func observedAudioTask()->Task<Void,Never>?{audioTask}
 func observedExportToken()->SpeechPlaybackToken?{exportToken}
}
'''

FLOW = r'''
import NaturalLanguage
@MainActor private func targetValue(_ target:SpeechTarget?,_ owners:[UUID:String])->Any{
 guard let target else{return NSNull()};return ["owner":owners[target.ownerID]!,"item":target.itemID]
}
private func normalizeRoot(_ value:Any,_ root:String)->Any{
 if let value=value as? String{
  // FileManager's enumerator spells the macOS /var symlink as /private/var even
  // when URL's resolvingSymlinks path representation retains /var. Only this
  // fixture-owned temporary directory prefix is projected, longest alias first.
  var aliases=[root,URL(fileURLWithPath:root).resolvingSymlinksInPath().path]
  if root.hasPrefix("/var/"){aliases.append("/private"+root)}
  if root.hasPrefix("/private/var/"){aliases.append(String(root.dropFirst(8)))}
  return aliases.sorted{$0.count>$1.count}.reduce(value){$0.replacingOccurrences(of:$1,with:"$ASSETS")}
 }
 if let value=value as? [Any]{return value.map{normalizeRoot($0,root)}}
 if let value=value as? [String:Any]{return value.mapValues{normalizeRoot($0,root)}}
 return value
}
private func jsonValue<T:Encodable>(_ value:T)->Any{try! JSONSerialization.jsonObject(with:JSONEncoder().encode(value))}
private func diagnosticType(_ name:String)->String{
 if name.hasSuffix("FakeSpeechError"){return "provider_error"}
 if name.hasSuffix("SpeechError"){return "speech_error"}
 return name
}
private func scalarValue(_ number:Double)->Any{number.isFinite ? number : number.isNaN ? "nan" : number>0 ? "infinity" : "-infinity"}
extension PendingSpeechGenerator {
 func emitWaveAndFinish(_ token:SpeechPlaybackToken)async{
  do{let wave=try SpeechExportPolicy.wav(Data(repeating:0,count:9600),sampleRate:24000);try await receivers[token]?(.encoded(wave.data));waiters.removeValue(forKey:token)?.resume()}
  catch{waiters.removeValue(forKey:token)?.resume(throwing:error)}
 }
 func failGeneration(_ token:SpeechPlaybackToken){waiters.removeValue(forKey:token)?.resume(throwing:SpeechError.generationFailed)}
 var pendingCount:Int{waiters.count}
}
@MainActor private func waitUntil(_ condition:()async->Bool)async throws{
 for _ in 0..<1500{if await condition(){return};try await Task.sleep(for:.milliseconds(1))};throw ControllerProbeTimeout()
}
private struct ControllerProbeTimeout:Error{}
@MainActor private func controllerFlow(_ row:[String:Any])async throws->Any{
 let suite="lingrove-controller-flow-"+UUID().uuidString,d=UserDefaults(suiteName:suite)!;d.removePersistentDomain(forName:suite);defer{d.removePersistentDomain(forName:suite)}
 let root=FileManager.default.temporaryDirectory.appendingPathComponent(suite,isDirectory:true).standardizedFileURL
 try FileManager.default.createDirectory(at:root.appendingPathComponent("app"),withIntermediateDirectories:true);defer{try? FileManager.default.removeItem(at:root)}
 for name in row["files"] as? [String] ?? []{
  let url=root.appendingPathComponent(name);try FileManager.default.createDirectory(at:url.deletingLastPathComponent(),withIntermediateDirectories:true);try Data("GGUFfixture".utf8).write(to:url)
 }
 func expand(_ value:Any)->Any{
  if let value=value as? String{return value.replacingOccurrences(of:"$ASSETS",with:root.path)}
  if let value=value as? [Any]{return value.map(expand)};if let value=value as? [String:Any]{return value.mapValues(expand)};return value
 }
 let input=expand(row)as! [String:Any]
 EncodedDeviceProbe.players=[];EncodedDeviceProbe.duration=1;EncodedDeviceProbe.failInit=false;EncodedDeviceProbe.playSucceeds=true;ModelDownloadManager.creations=0
 RecognitionProbe.scores.withLock{$0=[: ]}
 for(key,value)in input["seed"] as? [String:Any] ?? [:]{d.set(value,forKey:key)}
 let settings=LearningSpeechSettings(defaults:d,credentialStore:InMemoryOpenAICompatibleCredentialStore())
 let provider=FakeSpeechProvider(),generator=PendingSpeechGenerator(),pcm=TestPCMPlayer()
 var logs:[[String:Any]]=[]
 let telemetry=SpeechTelemetry(now:{10},event:{context,phase,details in
  var details=details;if let type=details["error_type"]{details["error_type"]=diagnosticType(type)}
  logs.append(["model":context.modelName,"engine":context.engine,"phase":phase,"details":details])
 })
 let c=LearningSpeechController(settings:settings,provider:provider,generator:generator,telemetry:telemetry,pcmPlayer:pcm,modelDirectoryURL:root.appendingPathComponent("app"))
 var ownerIDs:[String:UUID]=[:],ownerNames:[UUID:String]=[:],tokens:[SpeechPlaybackToken]=[],exports:[String:SpeechExportRequest]=[:],exportJobs:[String:Task<SpeechExportAudio,Error>]=[:],exportCancellations:Set<String>=[],localFingerprints:[String:String]=[:]
 func owner(_ name:String)->UUID{if let id=ownerIDs[name]{return id};let id=UUID(uuidString:String(format:"00000000-0000-0000-0000-%012x",ownerIDs.count+1))!;ownerIDs[name]=id;ownerNames[id]=name;return id}
 func target(_ a:[String:Any])->SpeechTarget{.init(ownerID:owner(text(a,"owner","a")),itemID:text(a,"item","translation"))}
 func tokenName(_ token:SpeechPlaybackToken)->String{if let i=tokens.firstIndex(of:token){return "token-\(i+1)"};tokens.append(token);return "token-\(tokens.count)"}
 func sourceValue(_ source:SpeechAudioSource)->[String:Any]{
  switch source{
  case .local(let v):return ["kind":"local","model":v.modelURL.path,"projection":v.projectionURL.path,"reference":v.referenceAudioURL?.path as Any? ?? NSNull()]
  case .endpoint(let v,let key):return ["kind":"endpoint","baseURL":v.endpoint.baseURL.absoluteString,"model":v.endpoint.modelName,"voice":v.voice,"key":key as Any? ?? NSNull()]
  }
 }
 func requestValue(_ request:SpeechAudioRequest)->[String:Any]{["token":tokenName(request.token),"text":request.text,"language":request.language.rawValue,"source":sourceValue(request.source),"sampling":jsonValue(request.sampling)]}
 func exportValue(_ name:String,_ request:SpeechExportRequest)->[String:Any]{
  var source:[String:Any],fingerprint=request.fingerprint
  switch request.source{
  case .system(let id):source=["kind":"system","voice":id]
  case .generated(let audio,let sampling):source=["kind":"generated","audio":sourceValue(audio),"sampling":jsonValue(sampling)]
   if case .local=audio{if localFingerprints[fingerprint]==nil{localFingerprints[fingerprint]="local-fingerprint-\(localFingerprints.count+1)"};fingerprint=localFingerprints[fingerprint]!}
  }
  return ["name":name,"token":tokenName(request.token),"text":request.text,"language":request.language.rawValue,"fingerprint":fingerprint,"source":source]
 }
 func observation(_ result:Any=NSNull())async->[String:Any]{
  if let token=c.observedActiveToken(){_ = tokenName(token)}
  for start in provider.starts{_ = tokenName(start.token)}
  for key in exports.keys.sorted(){_ = tokenName(exports[key]!.token)}
  let requests=await generator.requests
  for request in requests{_ = tokenName(request.token)}
  let activeRequest:Any=c.observedActiveRequest().map{["target":targetValue($0.target,ownerNames),"text":$0.text,"language":$0.language.rawValue,"context":$0.context as Any? ?? NSNull(),"mode":$0.languageMode == .fixed ? "fixed":"automatic"] as [String:Any]} as Any? ?? NSNull()
  let protection:Any=ModelDownloadManager.creations==0 ? NSNull():c.modelDownloads.protectedModelFilenames().sorted()
  let snapshot:[String:Any]=[
   "state":["active":targetValue(c.activeTarget,ownerNames),"errorTarget":targetValue(c.errorTarget,ownerNames),"error":c.playbackError as Any? ?? NSNull(),"phase":c.phase.map{$0 == .preparing ? "preparing":"playing"} as Any? ?? NSNull(),"catalogLoaded":c.catalogIsLoaded,"catalogError":c.voiceCatalogError as Any? ?? NSNull(),"voices":c.voiceCatalog.map{["id":$0.id,"name":$0.name,"language":$0.language,"quality":$0.quality.rawValue,"preferred":$0.isSystemPreferred,"novelty":$0.isNovelty]},"activeRequest":activeRequest,"exporting":c.observedExportToken() != nil,"exportRevision":String(c.observedExportRevision())],
   "provider":["refreshes":provider.refreshCount,"starts":provider.starts.map{["text":$0.text,"voice":$0.voice.id,"rate":$0.rateMultiplier,"token":tokenName($0.token)]},"stopped":provider.stopTokens.map(tokenName)],
   "generation":["requests":requests.map(requestValue),"cancelled":await generator.cancelledCount],
   "pcm":["ended":pcm.inputEnded,"chunks":pcm.chunkCount],
   "encoded":EncodedDeviceProbe.players.map{["bytes":$0.data.count,"digest":SpeechExportPolicy.digest($0.data),"duration":scalarValue($0.duration),"rate":Double($0.rate),"enableRate":$0.enableRate,"stopped":$0.stopped]},
   "models":["created":ModelDownloadManager.creations,"available":c.availableLocalModels.map{["filename":$0.filename,"path":$0.url.path,"displayName":$0.displayName]},"protected":protection],
   "exports":exports.keys.sorted().map{exportValue($0,exports[$0]!)},"logs":logs,"result":result]
  return normalizeRoot(snapshot,root.path)as! [String:Any]
 }
 var trace:[Any]=[await observation()]
 for a in input["actions"] as! [[String:Any]]{
  var result:Any=NSNull();let previous=c.observedAudioTask(),previousToken=c.observedActiveToken()
  do{switch text(a,"op"){
  case "toggle":
   let oldCount=await generator.requests.count
   c.toggle(.init(target:target(a),text:text(a,"text","Hello."),language:.init(rawValue:text(a,"language","en"))!,context:a["context"]as? String,languageMode:text(a,"mode")=="fixed" ? .fixed:.automatic))
   if settings.selectedProvider != .system,c.activeTarget != nil,c.observedActiveToken() != previousToken{try await waitUntil{await generator.requests.count>oldCount || c.activeTarget==nil}}
  case "stop":c.stop()
  case "stopOwner":c.stop(ownerID:owner(text(a,"owner","a")))
  case "stopTarget":c.stop(target:target(a))
  case "refreshVoices":c.refreshVoices()
  case "catalog":provider.voices=(a["voices"]as! [[String:Any]]).map{.init(id:text($0,"id"),name:text($0,"name",text($0,"id")),language:text($0,"language","en-US"),quality:.init(rawValue:number($0,"quality"))!,isSystemPreferred:$0["preferred"]as? Bool ?? false,isNovelty:$0["novelty"]as? Bool ?? false)}
  case "providerFailure":provider.refreshError=a["refresh"]as? Bool==true ? FakeSpeechError.unavailable:nil;provider.speakError=a["speak"]as? Bool==true ? FakeSpeechError.unavailable:nil;provider.cancelOnStop=a["cancelOnStop"]as? Bool ?? false
  case "recognition":let scores=a["scores"]as! [String:[String:Double]];RecognitionProbe.scores.withLock{v in v=Dictionary(uniqueKeysWithValues:scores.map{($0.key,Dictionary(uniqueKeysWithValues:$0.value.map{(NLLanguage(rawValue:$0.key),$0.value)}))})}
  case "event":
   let token=tokens[number(a,"index")]
   switch text(a,"kind"){
   case "started":provider.send(.started(token));case "prepared":provider.send(.prepared(token,milliseconds:number(a,"milliseconds",3)))
   case "finished":provider.send(.finished(token));case "cancelled":provider.send(.cancelled(token));case "failed":provider.send(.failed(token,message:text(a,"message","fixture playback failed")));default:fatalError("Unknown event")}
  case "emitPCM":try await generator.emitPCM(tokens[number(a,"index")])
  case "finishGeneration":await generator.finishGeneration(tokens[number(a,"index")]);await c.observedAudioTask()?.value
  case "generationFailure":await generator.failGeneration(tokens[number(a,"index")]);await c.observedAudioTask()?.value
  case "invalidEncoded":EncodedDeviceProbe.failInit=true;await generator.complete(tokens[number(a,"index")]);await c.observedAudioTask()?.value;EncodedDeviceProbe.failInit=false
  case "encodedPlan":EncodedDeviceProbe.duration=a["duration"].map(scalar) ?? 1;EncodedDeviceProbe.failInit=a["failInit"]as? Bool ?? false;EncodedDeviceProbe.playSucceeds=a["play"]as? Bool ?? true
  case "audition":try c.audition(.init(data:Data(repeating:0,count:number(a,"bytes",8)),fileExtension:"wav"),ownerID:owner(text(a,"owner","a")),language:.init(rawValue:text(a,"language","en"))!)
  case "configuration":result=c.configurationError as Any? ?? NSNull()
  case "refreshModels":c.refreshLocalModels()
  case "selectModel":c.selectLocalModel(text(a,"path"))
  case "runtime":telemetry.receive(.init(snapshot:.init(sessionID:UUID(uuidString:"00000000-0000-0000-0000-000000000999"),generation:UInt64(number(a,"generation",1)),phase:.init(rawValue:text(a,"phase","loading"))!,processID:a["pid"].map{Int32(($0 as! NSNumber).intValue)},loadedModelFilenames:Set(a["files"]as? [String] ?? [])),token:nil))
  case "exportRequest":exports[text(a,"name","x")]=try c.exportRequest(text:text(a,"text","Hello."),language:.init(rawValue:text(a,"language","en"))!,context:a["context"]as? String)
  case "exportStart":
   let name=text(a,"name","x"),request=exports[name]!;var entered=false
   exportJobs[name]=Task{entered=true;return try await c.exportAudio(request)}
   try await waitUntil{entered}
   if case .system=request.source{_ = try? await exportJobs[name]!.value}
   if c.observedExportToken()==request.token{if case .generated=request.source{try await waitUntil{await generator.requests.contains{$0.token==request.token}}}}
  case "exportPCM":try await generator.emitPCM(exports[text(a,"name","x")]!.token)
  case "exportFinish":let name=text(a,"name","x");await generator.finishGeneration(exports[name]!.token);let audio=try await exportJobs[name]!.value;result=["bytes":audio.data.count,"digest":SpeechExportPolicy.digest(audio.data),"extension":audio.fileExtension]
  case "exportWave":let name=text(a,"name","x");await generator.emitWaveAndFinish(exports[name]!.token);let audio=try await exportJobs[name]!.value;result=["bytes":audio.data.count,"digest":SpeechExportPolicy.digest(audio.data),"extension":audio.fileExtension]
  case "exportJoin":let audio=try await exportJobs[text(a,"name","x")]!.value;result=["bytes":audio.data.count,"digest":SpeechExportPolicy.digest(audio.data),"extension":audio.fileExtension]
  case "exportCancel":let name=text(a,"name","x");exportJobs[name]!.cancel();exportCancellations.insert(name);_ = try? await exportJobs[name]!.value
  default:try apply(a,settings)
  }}catch let error as ControllerProbeTimeout{throw error}catch is CancellationError{result=["error":"cancelled"]}catch{result=["error":error.localizedDescription]}
  if previousToken != c.observedActiveToken(){await previous?.value}
  trace.append(await observation(result))
 }
 let retiringTask=c.observedAudioTask();c.shutdown();await generator.shutdown();await retiringTask?.value
 for task in exportJobs.values{task.cancel();_ = try? await task.value}
 return trace
}
'''
