#!/usr/bin/env python3
"""Execute complete original PCM admission and queue accounting without an audio device."""
import argparse,hashlib,json,subprocess,tempfile
from pathlib import Path
from macos_vocabulary_reference import BASELINE,source
CHUNK='Cotabby/Models/Speech/SpeechAudioChunk.swift'
STATE='Cotabby/Support/Speech/SpeechPCMPlaybackState.swift'
ID_HASH='0b44b96497061d5ee205792fd60f8e6f1f19f8e12ca82bee1859efc485fe68be'
DRIVER=r'''
import Foundation
import CryptoKit
struct Corpus:Decodable{let cases:[Row]}
struct Row:Decodable{let id:String,kind:String;let bits:[UInt32]?;let data:String?;let zeros:Int?;let sampleRate:Int?;let steps:[Step]?}
struct Step:Decodable{let op:String;let samples:Int?;let id:Int?;let sampleRate:Int?;let rate:String?}
func rate(_ value:String)->Double{switch value{case "nan":return .nan;case "infinity":return .infinity;case "-infinity":return -.infinity;default:return Double(value)!}}
func snapshot(_ s:SpeechPCMPlaybackState)->[String:Any]{["queued":s.queuedSamples,"total":s.totalSamples,"ended":s.inputEnded,"finished":s.isFinished]}
@main struct Main{
 static func main()throws{
  let corpus=try JSONDecoder().decode(Corpus.self,from:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1])))
  var results:[[String:Any]]=[]
  for row in corpus.cases{
   let result:Any
   if row.kind=="chunk"{
    var data=Data()
    if let bits=row.bits{for value in bits{var little=value.littleEndian;withUnsafeBytes(of:&little){data.append(contentsOf:$0)}}}
    else if let raw=row.data{data=Data(base64Encoded:raw)!}else{data=Data(repeating:0,count:row.zeros!)}
    do{let chunk=try SpeechPCMChunk(data:data,sampleRate:row.sampleRate!);result=["rate":chunk.sampleRate,"samples":chunk.sampleCount,"bytes":chunk.data.count,"sha256":SHA256.hash(data:chunk.data).map{String(format:"%02x",$0)}.joined()]}
    catch SpeechError.invalidAudio{result=["error":"invalidAudio"]}catch{throw error}
   }else{
    var state=SpeechPCMPlaybackState(),trace:[[String:Any]]=[["state":snapshot(state),"result":NSNull()]]
    for step in row.steps!{
     var outcome:Any=NSNull()
     do{switch step.op{
      case "enqueue":outcome=try state.enqueue(samples:step.samples!)
      case "played":state.played(step.id!)
      case "end":try state.endInput()
      case "start":outcome=state.canStart(sampleRate:step.sampleRate!,rate:rate(step.rate!))
      default:fatalError("Unknown PCM action")
     }}catch SpeechError.invalidAudio{outcome=["error":"invalidAudio"]}catch{throw error}
     trace.append(["state":snapshot(state),"result":outcome])
    };result=trace
   }
   results.append(["id":row.id,"expected":result])
  }
  FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject:results,options:[.sortedKeys]))
 }
}
'''
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--mac-repo',type=Path,required=True);p.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/speech-pcm-v1.json');p.add_argument('--capture-output',type=Path)
 a=p.parse_args();c=json.loads(a.fixture.read_text());rows=c['cases'];ids=[r['id'] for r in rows]
 if c.get('version')!=1 or c.get('macReference')!=BASELINE or len(ids)!=len(set(ids)) or hashlib.sha256('\n'.join(sorted(ids)).encode()).hexdigest()!=ID_HASH:p.error('Pinned reviewed PCM matrix required')
 for r in rows:
  if r.get('macSource')!=(CHUNK if r['kind']=='chunk' else STATE) or r.get('provenanceKind')!='source-policy-extension':p.error('Original PCM provenance required')
 chunk=source(a.mac_repo,CHUNK);chunk='import Foundation\n'+chunk[chunk.index('nonisolated struct SpeechPCMChunk:'):]
 values=['enum SpeechError:Error{case invalidAudio}',chunk,source(a.mac_repo,STATE),DRIVER]
 with tempfile.TemporaryDirectory(prefix='lingrove-speech-pcm-') as directory:
  root=Path(directory);files=[]
  for i,value in enumerate(values):
   f=root/f'source{i}.swift';f.write_text(value);files.append(str(f))
  binary=root/'probe';subprocess.run(['swiftc','-swift-version','6','-O',*files,'-o',str(binary)],check=True)
  actual=json.loads(subprocess.check_output([str(binary),str(a.fixture)]))
 if a.capture_output:
  with a.capture_output.open('x',encoding='utf-8') as out:json.dump(actual,out,ensure_ascii=False,indent=2);out.write('\n')
  print(f'Captured {len(actual)} original PCM observations');return
 if len(actual)!=len(rows):raise SystemExit('PCM count changed')
 for row,value in zip(rows,actual):
  if value!={'id':row['id'],'expected':row['expected']}:raise SystemExit('Mac PCM mismatch: '+row['id'])
 print(f'Verified {len(rows)} original PCM observations')
if __name__=='__main__':main()
