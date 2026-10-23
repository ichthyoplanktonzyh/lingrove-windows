#!/usr/bin/env python3
"""Execute the complete pinned speech pipe frame decoder with synthetic fragmented byte streams."""
import argparse,hashlib,json,subprocess,tempfile
from pathlib import Path
from macos_vocabulary_reference import BASELINE,source
FRAME='Cotabby/Support/Speech/SpeechAudioFrameDecoder.swift'
CHUNK='Cotabby/Models/Speech/SpeechAudioChunk.swift'
ID_HASH='55f380d9291a369a45fd0e801f7bb377fb446798af15379ceed3d2a897af3f6c'
DRIVER=r'''
import Foundation
import CryptoKit
struct Corpus:Decodable{let cases:[Row]}
struct Row:Decodable{let id:String;let raw:String?;let wires:[Wire]?;let steps:[Step]}
struct Wire:Decodable{let header:String?;let headerBytes:String?;let payload:String?;let zeros:Int?}
struct Step:Decodable{let op:String;let offset:Int?;let count:Int?;let zeros:Int?}
func header(_ h:SpeechAudioFrameDecoder.Header)->[String:Any]{[
 "event":h.event,"protocolVersion":h.protocolVersion as Any? ?? NSNull(),"id":h.id as Any? ?? NSNull(),"sequence":h.sequence as Any? ?? NSNull(),
 "sampleRate":h.sampleRate as Any? ?? NSNull(),"samples":h.samples as Any? ?? NSNull(),"payloadBytes":h.payloadBytes as Any? ?? NSNull(),
 "ok":h.ok as Any? ?? NSNull(),"format":h.format as Any? ?? NSNull(),"channels":h.channels as Any? ?? NSNull()]}
@main struct Main{
 static func main()throws{
  let corpus=try JSONDecoder().decode(Corpus.self,from:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1])))
  var results:[[String:Any]]=[]
  for row in corpus.cases{
   var wire=row.raw.map{Data(base64Encoded:$0)!} ?? Data()
   for spec in row.wires ?? []{
    let bytes=spec.headerBytes.map{Data(base64Encoded:$0)!} ?? Data(spec.header!.utf8);var length=UInt32(bytes.count).bigEndian
    withUnsafeBytes(of:&length){wire.append(contentsOf:$0)};wire.append(bytes)
    wire.append(spec.payload.map{Data(base64Encoded:$0)!} ?? Data(repeating:0,count:spec.zeros ?? 0))
   }
   var decoder=SpeechAudioFrameDecoder(),trace:[Any]=[]
   for step in row.steps{
    var result:Any=NSNull()
    do{switch step.op{
     case "append":let bytes=step.zeros.map{Data(repeating:0,count:$0)} ?? wire.subdata(in:(step.offset ?? 0)..<((step.offset ?? 0)+(step.count ?? wire.count)))
      try decoder.append(bytes)
     case "next":if let frame=try decoder.next(){result=["header":header(frame.header),"bytes":frame.payload.count,"sha256":SHA256.hash(data:frame.payload).map{String(format:"%02x",$0)}.joined()]}
     case "finish":try decoder.finish()
     default:fatalError("Unknown decoder action")
    }}catch SpeechError.invalidAudio{result=["error":"invalidAudio"]}catch is DecodingError{result=["error":"decode"]}catch{throw error}
    trace.append(result)
   }
   results.append(["id":row.id,"expected":trace])
  }
  FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject:results,options:[.sortedKeys]))
 }
}
'''
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--mac-repo',type=Path,required=True);p.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/speech-frame-v1.json');p.add_argument('--capture-output',type=Path)
 a=p.parse_args();c=json.loads(a.fixture.read_text());rows=c['cases'];ids=[r['id'] for r in rows]
 if c.get('version')!=1 or c.get('macReference')!=BASELINE or len(ids)!=len(set(ids)) or hashlib.sha256('\n'.join(sorted(ids)).encode()).hexdigest()!=ID_HASH:p.error('Pinned reviewed speech frame matrix required')
 if any(r.get('macSource')!=FRAME or r.get('provenanceKind')!='source-policy-extension' for r in rows):p.error('Original decoder provenance required')
 chunk=source(a.mac_repo,CHUNK);chunk='import Foundation\n'+chunk[chunk.index('nonisolated struct SpeechPCMChunk:'):]
 values=['enum SpeechError:Error{case invalidAudio}',chunk,source(a.mac_repo,FRAME),DRIVER]
 with tempfile.TemporaryDirectory(prefix='lingrove-speech-frame-') as directory:
  root=Path(directory);files=[]
  for i,value in enumerate(values):
   f=root/f'source{i}.swift';f.write_text(value);files.append(str(f))
  binary=root/'probe';subprocess.run(['swiftc','-swift-version','6','-O',*files,'-o',str(binary)],check=True)
  actual=json.loads(subprocess.check_output([str(binary),str(a.fixture)]))
 if a.capture_output:
  with a.capture_output.open('x',encoding='utf-8') as out:json.dump(actual,out,ensure_ascii=False,indent=2);out.write('\n')
  print(f'Captured {len(actual)} original speech frame traces');return
 if len(actual)!=len(rows):raise SystemExit('Speech frame count changed')
 for row,value in zip(rows,actual):
  if value!={'id':row['id'],'expected':row['expected']}:raise SystemExit('Mac speech frame mismatch: '+row['id'])
 print(f'Verified {len(rows)} original speech frame traces')
if __name__=='__main__':main()
