#!/usr/bin/env python3
"""Execute the complete original speech export actor with synthetic in-memory chunks/recordings."""
import argparse,hashlib,json,subprocess,tempfile
from pathlib import Path
from macos_vocabulary_reference import BASELINE,between,source
BUFFER='Cotabby/Services/Speech/SpeechExportBuffer.swift'
ID_HASH='07e231693506f2b5a4bb27e9fb65d76fae4232c076c0a9bfda596d93576b8e61'
OBSERVER=r'''
extension SpeechExportBuffer {
 func observed()->Snapshot {Snapshot(dataBytes:data.count,dataSHA256:SpeechExportPolicy.digest(data),sampleRate:sampleRate,encoded:encoded.map(AudioInfo.init))}
}
'''
DRIVER=r'''
import Foundation
struct Corpus:Decodable {let cases:[Row]}
struct Row:Decodable {let id:String;let actions:[Action]}
struct Action:Decodable {let op:String;let bits:[UInt32]?;let zeros:Int?;let prefix:String?;let rate:Int?;let cancel:Bool?}
struct AudioInfo:Codable,Sendable {let bytes:Int,sha256:String,format:String;init(_ audio:SpeechExportAudio){bytes=audio.data.count;sha256=SpeechExportPolicy.digest(audio.data);format=audio.fileExtension}}
struct Snapshot:Codable,Sendable {let dataBytes:Int,dataSHA256:String;let sampleRate:Int?;let encoded:AudioInfo?}
struct Outcome:Codable,Sendable {var error:String?;var audio:AudioInfo?}
struct Trace:Codable,Sendable {let state:Snapshot;let result:Outcome?}
struct Result:Codable,Sendable {let id:String;let expected:[Trace]}
func bytes(_ a:Action)->Data {
 var data=a.prefix.map{Data(base64Encoded:$0)!} ?? Data()
 if let bits=a.bits{for value in bits{var little=value.littleEndian;withUnsafeBytes(of:&little){data.append(contentsOf:$0)}}}
 else{data.append(Data(repeating:0,count:a.zeros ?? 0))};return data
}
func run<T:Sendable>(_ cancel:Bool,_ operation:@escaping @Sendable ()async throws->T)async throws->T {
 if !cancel{return try await operation()}
 return try await Task{withUnsafeCurrentTask{$0?.cancel()};return try await operation()}.value
}
@main struct Main {
 static func main()async throws {
  let corpus=try JSONDecoder().decode(Corpus.self,from:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1])))
  var results:[Result]=[]
  for row in corpus.cases {
   let buffer=SpeechExportBuffer();var trace=[Trace(state:await buffer.observed(),result:nil)]
   for a in row.actions {
    var result:Outcome?
    do {
     switch a.op {
     case "pcm":
      let chunk=SpeechAudioChunk.pcm(try SpeechPCMChunk(data:bytes(a),sampleRate:a.rate!))
      _ = try await run(a.cancel ?? false){try await buffer.append(chunk);return true}
     case "encoded":
      let chunk=SpeechAudioChunk.encoded(bytes(a))
      _ = try await run(a.cancel ?? false){try await buffer.append(chunk);return true}
     case "package":
      let recording=SystemSpeechRecording(bytes:bytes(a),sampleRate:a.rate!)
      result=Outcome(audio:AudioInfo(try await run(a.cancel ?? false){try await buffer.package(recording)}))
     case "finish":result=Outcome(audio:AudioInfo(try await run(a.cancel ?? false){try await buffer.finish()}))
     default:fatalError("Unknown export buffer action")
     }
    }catch SpeechError.invalidAudio{result=Outcome(error:"invalidAudio")}
     catch SpeechError.responseTooLarge{result=Outcome(error:"responseTooLarge")}
     catch is CancellationError{result=Outcome(error:"cancelled")}
     catch{throw error}
    trace.append(Trace(state:await buffer.observed(),result:result))
   }
   results.append(Result(id:row.id,expected:trace))
  }
  let encoder=JSONEncoder();encoder.outputFormatting=[.sortedKeys];FileHandle.standardOutput.write(try encoder.encode(results))
 }
}
'''
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--mac-repo',type=Path,required=True);p.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/speech-buffer-v1.json');p.add_argument('--capture-output',type=Path)
 a=p.parse_args();c=json.loads(a.fixture.read_text());rows=c['cases'];ids=[r['id'] for r in rows]
 if c.get('version')!=1 or c.get('macReference')!=BASELINE or len(ids)!=len(set(ids)) or hashlib.sha256('\n'.join(sorted(ids)).encode()).hexdigest()!=ID_HASH:p.error('Pinned reviewed speech buffer matrix required')
 if any(r.get('macSource')!=BUFFER or r.get('provenanceKind')!='source-policy-extension' for r in rows):p.error('Original actor provenance required')
 models=source(a.mac_repo,'Cotabby/Models/Speech/SpeechExportModels.swift');recording=source(a.mac_repo,'Cotabby/Services/Speech/SystemSpeechAudioExporter.swift');errors=source(a.mac_repo,'Cotabby/Models/Speech/LearningSpeechModels.swift')
 values=[source(a.mac_repo,'Cotabby/Models/Speech/SpeechAudioChunk.swift'),
  'import Foundation\n'+models[models.index('nonisolated struct SpeechExportAudio:'):],
  'import Foundation\n'+between(recording,'nonisolated struct SystemSpeechRecording:','/// AVSpeech\'s callback'),
  'import Foundation\n'+between(errors,'nonisolated enum SpeechError:','/// The system catalog'),
  source(a.mac_repo,'Cotabby/Support/Speech/SpeechExportPolicy.swift'),source(a.mac_repo,BUFFER)+OBSERVER,DRIVER]
 with tempfile.TemporaryDirectory(prefix='lingrove-speech-buffer-') as directory:
  root=Path(directory);files=[]
  for i,value in enumerate(values):
   f=root/f'source{i}.swift';f.write_text(value);files.append(str(f))
  binary=root/'probe';subprocess.run(['swiftc','-swift-version','6','-O',*files,'-o',str(binary)],check=True)
  actual=json.loads(subprocess.check_output([str(binary),str(a.fixture)]))
 # Codable omission becomes explicit null in the cross-language observation envelope only.
 for row in actual:
  for t in row['expected']:
   t.setdefault('result',None);t['state'].setdefault('sampleRate',None);t['state'].setdefault('encoded',None)
   if t['result'] is not None:t['result'].setdefault('error',None);t['result'].setdefault('audio',None)
 if a.capture_output:
  with a.capture_output.open('x',encoding='utf-8') as out:json.dump(actual,out,ensure_ascii=False,indent=2);out.write('\n')
  print(f'Captured {len(actual)} original speech export actor traces');return
 if len(actual)!=len(rows):raise SystemExit('Speech buffer count changed')
 for row,value in zip(rows,actual):
  if value!={'id':row['id'],'expected':row['expected']}:raise SystemExit('Mac speech buffer mismatch: '+row['id'])
 print(f'Verified {len(rows)} original speech export actor traces')
if __name__=='__main__':main()
