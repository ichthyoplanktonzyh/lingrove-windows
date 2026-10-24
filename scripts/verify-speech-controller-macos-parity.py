#!/usr/bin/env python3
"""Complete original speech controller/export over explicit in-memory platform boundaries."""
import argparse,collections,hashlib,importlib.util,json,re,subprocess,tempfile
from pathlib import Path
from macos_vocabulary_reference import BASELINE,between,source
CONTROLLER='Cotabby/Services/Speech/LearningSpeechController.swift'
EXPORT='Cotabby/Services/Speech/LearningSpeechController+Export.swift'
PROFILE='Cotabby/Models/Speech/SpeechGenerationProfile.swift'
ID_HASH='0131513ed0b40ac58344f6e1aa28bb459042e9e381d872f654f21113a193d6f7'
METHOD_HASH='0b9fc5c031d8d8886935611c48128173bc4c45ba8bdcb482d0e9659fefe92eac'
def validate_fixture(fixture,repo):
 # Lock both the observation matrix and its provenance before compiling the original source.
 assert fixture['version']==1 and fixture['macReference']==BASELINE and fixture['referenceLocale']=='en_US'
 rows=fixture['cases'];ids=[row['id'] for row in rows]
 assert len(rows)==104 and len(set(ids))==104
 assert hashlib.sha256('\n'.join(sorted(ids)).encode()).hexdigest()==ID_HASH
 assert collections.Counter(row['scope'] for row in rows)=={'profile':42,'identity':14,'controller':48}
 assert collections.Counter(row['provenanceKind'] for row in rows)=={'original-test-adaptation':22,'source-policy-extension':82}
 methods=set()
 for row in rows:
  expected_source=PROFILE if row['scope']=='profile' else EXPORT if row['scope']=='identity' or 'export' in row['id'] else CONTROLLER
  assert row['macSource']==expected_source,row['id']
  if row['provenanceKind']=='original-test-adaptation':
   assert row['scope']=='controller'
   reference=row['sourceTest'];path,method=reference.split(':');methods.add(reference)
   assert path in ('CotabbyTests/Services/Speech/LearningSpeechControllerTests.swift','CotabbyTests/Services/Speech/GeneratedSpeechControllerTests.swift')
   assert re.search(r'func\s+'+re.escape(method)+r'\s*\(',source(repo,path)),reference
  else:assert not row.get('sourceTest'),row['id']
 assert len(methods)==21 and hashlib.sha256('\n'.join(sorted(methods)).encode()).hexdigest()==METHOD_HASH
 return rows
def load_reference(name):
 spec=importlib.util.spec_from_file_location('speech_'+name,Path(__file__).with_name('verify-speech-'+name+'-macos-parity.py'));module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module);return module
PLATFORM=r'''
import AVFoundation
import Combine
import Foundation
import NaturalLanguage
import Synchronization
// Shadow only the SDK effect types. All controller/encoded-facade/language policy bodies stay intact.
nonisolated enum RecognitionProbe {
 static let scores=Mutex<[String:[NLLanguage:Double]]>([:])
}
private final class NLLanguageRecognizer {
 private var sample=""
 func processString(_ text:String){sample=text}
 func languageHypotheses(withMaximum:Int)->[NLLanguage:Double]{RecognitionProbe.scores.withLock{$0[sample] ?? [:]}}
}
@MainActor enum EncodedDeviceProbe {
 static var duration=1.0,failInit=false,playSucceeds=true
 static var players:[AVAudioPlayer]=[]
}
protocol AVAudioPlayerDelegate:AnyObject{
 func audioPlayerDidFinishPlaying(_ player:AVAudioPlayer,successfully:Bool)
 func audioPlayerDecodeErrorDidOccur(_ player:AVAudioPlayer,error:(any Error)?)
}
@MainActor final class AVAudioPlayer {
 let data:Data,duration:Double
 var delegate:(any AVAudioPlayerDelegate)?
 var enableRate=false,rate:Float=1
 private(set) var stopped=false
 init(data:Data)throws{
  if EncodedDeviceProbe.failInit{throw SpeechError.invalidAudio}
  self.data=data;duration=EncodedDeviceProbe.duration;EncodedDeviceProbe.players.append(self)
 }
 func play()->Bool{EncodedDeviceProbe.playSucceeds}
 func stop(){stopped=true}
}
@MainActor final class SystemSpeechProvider:SpeechProviding{
 var eventHandler:(@MainActor @Sendable(SpeechProviderEvent)->Void)?
 func refreshVoices()throws->[SpeechVoice]{fatalError("Reference must inject system provider")}
 func speak(_ text:String,voice:SpeechVoice,rateMultiplier:Double,token:SpeechPlaybackToken)throws{fatalError("Unused native speech")}
 func stop(token:SpeechPlaybackToken){fatalError("Unused native speech")}
}
actor SpeechAudioGenerator:SpeechAudioGenerating{
 init(runtimeEvents:@escaping @Sendable(SpeechRuntimeEvent)async->Void={_ in}){}
 func generate(for request:SpeechAudioRequest,receive:@escaping SpeechAudioReceiver)async throws{fatalError("Reference must inject generator")}
 func cancel(_ token:SpeechPlaybackToken)async{fatalError("Unused native generator")}
 func shutdown()async{fatalError("Unused native generator")}
}
@MainActor final class SystemSpeechAudioExporter{
 struct Request{let text:String,voice:String}
 private(set) var requests:[Request]=[]
 func audio(text:String,voiceID:String)async throws->SpeechExportAudio{
  requests.append(.init(text:text,voice:voiceID));try Task.checkCancellation()
  return try SpeechExportPolicy.wav(Data(repeating:0,count:9600),sampleRate:24000)
 }
}
'''
# This wrapper owns only filesystem/catalog observations; no transfer implementation is exercised.
MODEL_WRAPPER=r'''
import Foundation
@MainActor final class ModelDownloadManager{
 static var creations=0
 let runtimeDirectoryURL:URL
 var onModelDirectoryChanged:(()->Void)?
 var protectedModelFilenames:()->Set<String>={[]}
 init(runtimeDirectoryURL:URL,assetCatalog:[DownloadableRuntimeModel]){self.runtimeDirectoryURL=runtimeDirectoryURL;Self.creations+=1}
 __INSTALLED__
 __IN_USE__
}
enum BundledRuntimeLocator{
 static func userRuntimeDirectoryURL()->URL{FileManager.default.temporaryDirectory.appendingPathComponent("UNUSED-SPEECH-REFERENCE")}
 __DISCOVERY__
}
'''
from macos_speech_controller_reference import DRIVER,OBSERVER,FLOW
def sources(repo):
 telemetry=load_reference('telemetry');player=load_reference('player')
 paths=['Cotabby/Models/Context/LanguageCatalog.swift','Cotabby/Models/LanguageLearning/LearningLanguage.swift','Cotabby/Models/Runtime/LlamaRuntimeModels.swift','Cotabby/Models/Runtime/OpenAICompatibleEndpointModels.swift','Cotabby/Models/Runtime/Parameters/ModelSamplingOverrides.swift','Cotabby/Models/Runtime/Parameters/ModelParameterSettings.swift','Cotabby/Models/Speech/LearningSpeechModels.swift','Cotabby/Models/Speech/SpeechGenerationProfile.swift','Cotabby/Models/Speech/SpeechAudioChunk.swift','Cotabby/Models/Speech/SpeechExportModels.swift','Cotabby/Models/Speech/SpeechModelCatalog.swift','Cotabby/Support/Speech/SpeechModelPairingPolicy.swift','Cotabby/Support/Speech/SpeechTextPolicy.swift','Cotabby/Support/Speech/SpeechLanguageResolver.swift','Cotabby/Support/Speech/SpeechExportPolicy.swift','Cotabby/Support/Speech/SpeechPCMPlaybackState.swift','Cotabby/Support/Prompting/TokenCountEstimator.swift','Cotabby/Support/LanguageLearning/LearningFailure.swift','Cotabby/Support/Prompting/ModelContextBudget.swift','Cotabby/Services/Runtime/OpenAICompatible/OpenAICompatibleCredentialStore.swift','Cotabby/Models/Speech/LearningSpeechSettings.swift','Cotabby/Services/Speech/SpeechExportBuffer.swift','Cotabby/Services/Speech/SpeechAudioPlayer.swift','Cotabby/Services/Speech/SpeechPCMPlayer.swift',CONTROLLER,EXPORT]
 values=[source(repo,path)for path in paths]+telemetry.sources(repo)
 values[values.index(source(repo,CONTROLLER))]+=OBSERVER
 units=source(repo,'Cotabby/Models/LanguageLearning/LearningUnitModels.swift');words=source(repo,'Cotabby/Models/LanguageLearning/LearningDictionaryWord.swift')
 values.append('import Foundation\n'+between(units,'nonisolated struct LearningUnit:','/// Recognition and lookup share a resource,')+between(words,'nonisolated struct LearningDictionaryWord:','/// View-local geometry exists'))
 recording=source(repo,'Cotabby/Services/Speech/SystemSpeechAudioExporter.swift');values.append('import Foundation\n'+between(recording,'nonisolated struct SystemSpeechRecording:',"/// AVSpeech's callback"))
 renderer=source(repo,'Cotabby/Services/Speech/SpeechPCMRenderer.swift');values += [renderer[renderer.index('nonisolated protocol SpeechPCMRendering:'):renderer.index('/// One renderer belongs')],player.STUB]
 generator=source(repo,'Cotabby/Services/Speech/SpeechAudioGenerator.swift');values.append('import Foundation\n'+generator[generator.index('nonisolated protocol SpeechAudioGenerating:'):generator.index('actor SpeechAudioGenerator:')])
 manager=source(repo,'Cotabby/Services/ModelManagement/ModelDownloadManager.swift');locator=source(repo,'Cotabby/Support/Runtime/BundledRuntimeLocator.swift')
 values.append(MODEL_WRAPPER.replace('__INSTALLED__',between(manager,'    func installedModelOptions()','    /// The path shown')).replace('__IN_USE__',between(manager,'    func isModelInUse(filename:','    /// Removes one model')).replace('__DISCOVERY__',between(locator,'    static func discoverGGUFModelURLs(','    /// Resolves a specific model')))
 # The private SDK shadow and complete encoded facade must share a file, like private fake/driver.
 encoded=values.pop(values.index(source(repo,'Cotabby/Services/Speech/SpeechAudioPlayer.swift')))
 resolver=values.pop(values.index(source(repo,'Cotabby/Support/Speech/SpeechLanguageResolver.swift')))
 export=values.pop(values.index(source(repo,EXPORT)))
 platform=PLATFORM+'\n'+encoded+'\n'+resolver+'\n'+export
 tests=source(repo,'CotabbyTests/Services/Speech/LearningSpeechControllerTests.swift');generated=source(repo,'CotabbyTests/Services/Speech/GeneratedSpeechControllerTests.swift')
 fake=tests[tests.index('@MainActor\nprivate final class FakeSpeechProvider:'):]
 fake+='\n'+generated[generated.index('private actor PendingSpeechGenerator:'):]
 return list(dict.fromkeys(values))+[platform,fake+FLOW+DRIVER]
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--mac-repo',type=Path,required=True);p.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/speech-controller-v1.json');p.add_argument('--capture-output',type=Path);a=p.parse_args()
 rows=validate_fixture(json.loads(a.fixture.read_text()),a.mac_repo)
 with tempfile.TemporaryDirectory(prefix='lingrove-speech-controller-')as directory:
  root=Path(directory);files=[]
  for i,value in enumerate(sources(a.mac_repo)):
   f=root/f'source{i}.swift';f.write_text('import Foundation\n'+value);files.append(str(f))
  binary=root/'probe';subprocess.run(['swiftc','-swift-version','6','-O',*files,'-o',str(binary)],check=True)
  actual=json.loads(subprocess.check_output([str(binary),str(a.fixture),'-AppleLocale','en_US'],timeout=90))
 assert isinstance(actual,list) and len(actual)==len(rows),'Truncated original observations'
 assert [value['id'] for value in actual]==[row['id'] for row in rows],'Original observation IDs/order changed'
 if a.capture_output:
  with a.capture_output.open('x',encoding='utf-8')as out:json.dump(actual,out,ensure_ascii=False,indent=2);out.write('\n')
  print(f'Captured {len(actual)} original controller/profile/identity observations');return
 for row,value in zip(rows,actual):
  if value!={'id':row['id'],'expected':row['expected']}:raise SystemExit('Controller mismatch: '+row['id'])
 print(f'Verified {len(rows)} original controller/profile/identity observations')
if __name__=='__main__':main()
