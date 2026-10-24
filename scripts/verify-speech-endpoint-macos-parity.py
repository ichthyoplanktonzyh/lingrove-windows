#!/usr/bin/env python3
"""Complete original speech HTTP adapter over original and explicitly scripted URLProtocols."""
import argparse,collections,hashlib,json,re,subprocess,tempfile
from pathlib import Path
from macos_vocabulary_reference import BASELINE,source
from macos_speech_endpoint_wire import SpeechWireProbe
CLIENT='Cotabby/Services/Speech/SpeechEndpointClient.swift'
TEST='CotabbyTests/Services/Speech/SpeechEndpointClientTests.swift'
ID_HASH='77f0207771093fcef58a9d61ba45738b5030de358afbc862943e2e803ab0f74b'
OBSERVER=r'''
extension SpeechHTTPTransfer {
 func observedConfiguration()->[String:Any]{["maximum":Self.maximumBytes,"resource":configuration.timeoutIntervalForResource,"cookies":configuration.httpShouldSetCookies,"cookieStorage":configuration.httpCookieStorage != nil,"cache":configuration.urlCache != nil]}
}
'''
DRIVER=r'''
import CryptoKit
import Foundation
import Synchronization
func text(_ r:[String:Any],_ k:String,_ fallback:String="")->String{r[k]as? String ?? fallback}
struct EndpointPlan:Sendable{var status=200,declared:Int?,chunks=[3],contentType="audio/wav",networkError=false,waiting=false}
struct EndpointStore:Sendable{var plans:[String:EndpointPlan]=[:],requests:[String:[String:String]]=[:],started:Set<String>=[]}
nonisolated private final class ScriptedSpeechProtocol:URLProtocol,@unchecked Sendable{
 static let state=Mutex(EndpointStore())
 override static func canInit(with request:URLRequest)->Bool{true}
 override static func canonicalRequest(for request:URLRequest)->URLRequest{request}
 override func startLoading(){
  let url=request.url!,host=url.host!
  var body=request.httpBody
  if body==nil,let stream=request.httpBodyStream{stream.open();defer{stream.close()};var bytes=[UInt8](repeating:0,count:1024),result=Data();while stream.hasBytesAvailable{let n=stream.read(&bytes,maxLength:bytes.count);if n<=0{break};result.append(bytes,count:n)};body=result}
  let record=["idle":String(request.timeoutInterval),"url":url.absoluteString,"method":request.httpMethod ?? "","authorization":request.value(forHTTPHeaderField:"Authorization") ?? "","accept":request.value(forHTTPHeaderField:"Accept") ?? "","contentType":request.value(forHTTPHeaderField:"Content-Type") ?? "","body":String(data:body ?? Data(),encoding:.utf8) ?? ""]
  let plan=Self.state.withLock{s in s.requests[host]=record;return s.plans[host]!}
  if plan.networkError{client?.urlProtocol(self,didFailWithError:URLError(.networkConnectionLost));_ = Self.state.withLock{$0.started.insert(host)};return}
  var headers=["Content-Type":plan.contentType];if let length=plan.declared{headers["Content-Length"]=String(length)}
  let response=HTTPURLResponse(url:url,statusCode:plan.status,httpVersion:"HTTP/1.1",headerFields:headers)!
  client?.urlProtocol(self,didReceive:response,cacheStoragePolicy:.notAllowed);_ = Self.state.withLock{$0.started.insert(host)}
  if plan.waiting{return}
  for size in plan.chunks{client?.urlProtocol(self,didLoad:Data(repeating:90,count:size))}
  client?.urlProtocolDidFinishLoading(self)
 }
 override func stopLoading(){}
}
func parsedRecord(_ r:[String:String])->[String:Any]{var value=r.mapValues{$0 as Any};value["idle"]=Double(r["idle"]!);if let data=r["body"]?.data(using:.utf8){value["body"]=try! JSONSerialization.jsonObject(with:data)};return value}
@main struct Main{
 @MainActor static func main()async throws{
  guard Locale.current.identifier=="en_US"else{fatalError("Endpoint reference locale must be en_US")}
  let corpus=try JSONSerialization.jsonObject(with:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1])))as! [String:Any];var results:[[String:Any]]=[]
  for row in corpus["cases"]as! [[String:Any]]{
   let original=row["original"]as? Bool==true,wire=row["wire"]as? Bool==true,id=text(row,"id"),base=wire ? CommandLine.arguments[2]+"/"+id:text(row,"baseURL","https://"+id+".test"),endpoint=try OpenAICompatibleEndpointConfiguration(baseURLString:base,modelName:text(row,"model","speech-model"),apiMode:.chatCompletions),host=endpoint.baseURL.host!
   if !original && !wire{var plan=EndpointPlan();plan.status=row["status"]as? Int ?? 200;plan.chunks=row["chunks"]as? [Int] ?? [3];plan.declared=row.keys.contains("declared") ? row["declared"]as? Int:plan.chunks.reduce(0,+);plan.contentType=text(row,"contentType","audio/wav");plan.networkError=row["networkError"]as? Bool ?? false;plan.waiting=row["waiting"]as? Bool ?? false;ScriptedSpeechProtocol.state.withLock{$0.plans[host]=plan}}
   let transfer: @Sendable () -> SpeechHTTPTransfer = {let config=URLSessionConfiguration.ephemeral;if !wire{config.protocolClasses=original ? [SpeechURLProtocol.self]:[ScriptedSpeechProtocol.self]};return SpeechHTTPTransfer(configuration:config)}
   let observation=transfer().observedConfiguration(),client=SpeechEndpointClient(transfer:transfer),cancel=text(row,"cancel","none")
   let job=Task{
    if cancel=="before-start"{withUnsafeCurrentTask{$0?.cancel()}}
    return try await client.audio(text:text(row,"text","Hello."),configuration:.init(endpoint:endpoint,voice:text(row,"voice","voice-a")),apiKey:row.keys.contains("key") ? row["key"]as? String:"fixture-key")
   }
   if cancel=="after-start"{
    if original{try await Task.sleep(for:.milliseconds(30))}
    else{let deadline=ContinuousClock.now.advanced(by:.seconds(5));while !ScriptedSpeechProtocol.state.withLock({$0.started.contains(host)}){if ContinuousClock.now>deadline{fatalError("Endpoint effect never entered")};try await Task.sleep(for:.milliseconds(1))}}
    job.cancel()
   }
   var value:[String:Any]
   do{let data=try await job.value
    if original && host=="request.test"{var echo=try JSONSerialization.jsonObject(with:data)as! [String:Any];echo["body"]=try JSONSerialization.jsonObject(with:Data((echo["body"]as! String).utf8));value=["echo":echo]}
    else{value=["bytes":data.count,"digest":SHA256.hash(data:data).map{String(format:"%02x",$0)}.joined()]}
   }catch is CancellationError{value=["failure":"cancelled"]}
   catch let error as SpeechError{if case .endpointStatus(let status)=error{value=["failure":"endpointStatus","status":status,"message":error.localizedDescription]}else{value=["failure":String(describing:error),"message":error.localizedDescription]}}
   catch is URLError{value=["failure":"transport"]}
   catch{throw error}
   let request=ScriptedSpeechProtocol.state.withLock{$0.requests[host]}.map(parsedRecord)
   results.append(["id":id,"expected":["result":value,"request":request as Any? ?? NSNull(),"transfer":observation]])
  }
  FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject:results,options:[.sortedKeys]))
 }
}
'''
def rows(fixture,repo):
 assert fixture['version']==1 and fixture['macReference']==BASELINE and fixture['referenceLocale']=='en_US'
 values=fixture['cases'];ids=[r['id']for r in values]
 assert len(ids)==38 and len(set(ids))==38 and hashlib.sha256('\n'.join(sorted(ids)).encode()).hexdigest()==ID_HASH
 assert collections.Counter(r['provenanceKind']for r in values)=={'original-test-adaptation':6,'source-policy-extension':32}
 methods=set()
 for r in values:
  assert r['macSource']==CLIENT and bool(r.get('wire'))==r['id'].startswith('wire-')
  if r['provenanceKind']=='original-test-adaptation':
   assert r.get('original') is True and not r.get('wire');path,method=r['sourceTest'].split(':');assert path==TEST and re.search(r'func\s+'+re.escape(method)+r'\s*\(',source(repo,path));methods.add(method)
  else:assert not r.get('sourceTest') and not r.get('original')
 assert methods=={'sendsOnlyExplicitSpeechFields','rejectsHTTPFailuresRedirectsAndDeclaredOrStreamedOversize','cancellationStopsAResponseThatNeverCompletes'}
 return values
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--mac-repo',required=True,type=Path);p.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/speech-endpoint-v1.json');p.add_argument('--capture-output',type=Path);a=p.parse_args();cases=rows(json.loads(a.fixture.read_text()),a.mac_repo)
 paths=['Cotabby/Models/Context/LanguageCatalog.swift','Cotabby/Models/LanguageLearning/LearningLanguage.swift','Cotabby/Models/Runtime/Parameters/ModelSamplingOverrides.swift','Cotabby/Models/Runtime/OpenAICompatibleEndpointModels.swift','Cotabby/Models/Speech/LearningSpeechModels.swift']
 values=[source(a.mac_repo,path)for path in paths]+[source(a.mac_repo,CLIENT)+OBSERVER]
 original=source(a.mac_repo,TEST);fake=original[original.index('nonisolated private final class SpeechURLProtocol:'):original.index('@Suite(')];values.append(fake+DRIVER)
 with tempfile.TemporaryDirectory(prefix='lingrove-speech-endpoint-')as directory:
  root=Path(directory);files=[]
  for i,value in enumerate(values):
   f=root/f'source{i}.swift';f.write_text('import Foundation\n'+value);files.append(str(f))
  binary=root/'probe';subprocess.run(['swiftc','-swift-version','6','-O',*files,'-o',str(binary)],check=True)
  with SpeechWireProbe(cases)as wire:
   actual=json.loads(subprocess.check_output([str(binary),str(a.fixture),wire.url,'-AppleLocale','en_US'],timeout=90))
   for value in actual:
    if value['id']in wire.rows:value['expected']['request']=wire.requests[value['id']]
 assert len(actual)==len(cases) and [r['id']for r in actual]==[r['id']for r in cases]
 if a.capture_output:
  with a.capture_output.open('x',encoding='utf-8')as out:json.dump(actual,out,ensure_ascii=False,indent=2);out.write('\n')
  print(f'Captured {len(actual)} original speech HTTP observations');return
 for row,value in zip(cases,actual):
  if value!={'id':row['id'],'expected':row['expected']}:raise SystemExit('Endpoint mismatch: '+row['id'])
 print(f'Verified {len(actual)} original speech HTTP observations')
if __name__=='__main__':main()
