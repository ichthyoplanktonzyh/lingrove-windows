#!/usr/bin/env python3
"""Run original review/template Store methods over the complete real Mac archive and SQLite."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, between, source, vocabulary_store_sources

STORE='Cotabby/Services/Vocabulary/VocabularyStore.swift'
ID_HASH='2fa8710b2fcbf1f6015aa5590e86c1812b2241bac26fa3366be7fae53bdfa155'
DRIVER=r'''
import Foundation
func id(_ n:Int)->UUID {UUID(uuidString:String(format:"00000000-0000-0000-0000-%012x",n))!}
func text(_ r:[String:AnkiValue],_ k:String,_ fallback:String="")->String{r[k]?.string ?? fallback}
func flag(_ r:[String:AnkiValue],_ k:String)->Bool{if case let .bool(v)=r[k]{return v};return false}
func number(_ r:[String:AnkiValue],_ k:String,_ fallback:Int)->Int{if case let .number(v)=r[k]{return Int(v)};return fallback}
func layout(_ n:Int)->ReviewLayout{.init(front:[.init(id:id(n),source:.expression)],back:[.init(id:id(n+1),source:.meaning)])}
func review(_ n:Int,_ answer:String="original",_ flexible:Bool=false)->VocabularyReview{
 .init(plan:.init(id:id(n),layout:flexible ? layout(n+100):nil,answer:answer))
}
func binding(_ r:[String:AnkiValue])->AnkiNoteBinding{
 let origin=id(number(r,"origin",2))
 return .init(id:id(number(r,"binding",60)),originSenseID:origin,endpoint:"http://localhost",profile:"Profile",noteID:Int64(number(r,"note",10)),model:"Basic",deck:"Default",identityField:"ID",mapping:[.identity:"ID"],fields:["ID":origin.uuidString.lowercased(),"Q":text(r,"field","acknowledged")])
}
func seed(_ r:[String:AnkiValue])->VocabularyDocument{
 let mode=text(r,"seed","both"),time=Date(timeIntervalSince1970:0)
 var sense=VocabularySense(id:id(2),meaning:"meaning",note:"source note",isSavedWithoutContext:true,mergedFrom:flag(r,"alias") ? [id(11)]:[],createdAt:time,updatedAt:time)
 if mode=="legacy" || mode=="both"{sense.review=review(20)}
 if mode=="cards" || mode=="both"{sense.reviewCards=[review(21,"first",true),review(22,"second",true)]}
 if mode=="empty-cards"{sense.reviewCards=[]}
 if flag(r,"bound") || flag(r,"twoBindings") || flag(r,"preserved") {
  var v=flag(r,"baseCard") ? sense.reviewCards![0]:sense.review!
  if flag(r,"bound") || flag(r,"twoBindings"){v.bindings=[binding([:])];v.activeBindingID=id(60)}
  if flag(r,"twoBindings"){v.bindings.append(binding(["binding":.number(61),"note":.number(11)]))}
  if flag(r,"preserved"){v.preservedPlans=[.init(id:id(25),name:"preserved",answer:"keep")]}
  if flag(r,"baseCard"){sense.reviewCards![0]=v}else{sense.review=v}
 }
 var d=VocabularyDocument(entries:[.init(id:id(1),expression:"bank",dictionaryForm:"bank",language:.english,kind:.word,senses:[sense],createdAt:time,updatedAt:time)])
 if flag(r,"emptyTemplates"){d.reviewTemplates=[]}
 if flag(r,"templates") {d.reviewTemplates=[.init(id:id(70),name:"First",layout:layout(170)),.init(id:id(71),name:"Second",layout:layout(172))]}
 if flag(r,"maxTemplates"){d.reviewTemplates=(0..<64).map{.init(id:id(1000+$0),name:"T\($0)",layout:layout(2000+2*$0))}}
 return d
}
func canonical(_ v:AnkiValue,_ key:String="")->AnkiValue{
 if let o=v.object{return .object(o.mapValuesWithKey{canonical($1,$0)})}
 if let a=v.array {
  if ["mapping","audioReceipts"].contains(key) {
   precondition(a.count%2==0)
   let pairs=stride(from:0,to:a.count,by:2).map{(a[$0],canonical(a[$0+1]))}.sorted{$0.0.string! < $1.0.string!}
   return .array(pairs.flatMap{[$0.0,$0.1]})
  }
  if key=="audioKinds"{return .array(a.sorted{$0.string! < $1.string!})}
  return .array(a.map{canonical($0)})
 }
 return v
}
extension Dictionary {func mapValuesWithKey<T>(_ f:(Key,Value)throws->T)rethrows->[Key:T]{try Dictionary<Key,T>(uniqueKeysWithValues:map{($0.key,try f($0.key,$0.value))})}}
func encoded(_ d:VocabularyDocument)throws->AnkiValue{canonical(try JSONDecoder().decode(AnkiValue.self,from:JSONEncoder().encode(d)))}
extension VocabularyArchive {func seedReviewProbe(_ d:VocabularyDocument)throws{_ = try commit(d)}}
@MainActor func snapshot(_ s:VocabularyStore,_ archive:VocabularyArchive,_ revision:UInt64)async throws->AnkiValue {
 let disk=try await archive.load()
 return .object(["store":try encoded(.init(entries:s.entries,reviewTemplates:s.reviewTemplates.isEmpty && disk.document.reviewTemplates==nil ? nil:s.reviewTemplates)),
 "disk":try encoded(disk.document),"revision":.number(Double(disk.revision-revision)),"loaded":.bool(s.isLoaded),"error":.bool(s.errorMessage != nil),"pending":.number(Double(s.pending.count))])
}
@main struct Main {
 @MainActor static func main()async throws {
  let corpus=try JSONDecoder().decode(AnkiValue.self,from:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1])));var output:[AnkiValue]=[]
  for item in corpus.object!["cases"]!.array! {
   let r=item.object!,root=FileManager.default.temporaryDirectory.appendingPathComponent("lingrove-review-"+UUID().uuidString,isDirectory:true)
   try FileManager.default.createDirectory(at:root,withIntermediateDirectories:true,attributes:[.posixPermissions:0o700]);defer{try? FileManager.default.removeItem(at:root)}
   let archive=VocabularyArchive(url:root.appendingPathComponent("notebook.sqlite")),s=VocabularyStore(archive:archive)
   try await archive.seedReviewProbe(seed(r));await s.load();let initial=try await archive.load(),revision=initial.revision
   var expected:VocabularyReview?,trace:[AnkiValue]=[.object(["state":try await snapshot(s,archive,revision),"outcome":.null])]
   func live(_ card:UUID?)->VocabularyReview?{s.entries.first?.senses.first?.reviewValue(card)}
   for item in r["actions"]!.array! {
    let a=item.object!,card=a["card"] == nil ? nil:id(number(a,"card",21)),entryID=id(number(a,"entry",1)),senseID=id(number(a,"sense",2));var outcome:AnkiValue = .null
    do {
     switch text(a,"op") {
     case "capture":expected=live(card)
     case "save","external":
      var value=flag(a,"fresh") ? review(number(a,"valueID",card==nil ? 20:number(a,"card",21)),text(a,"answer","edited"),card != nil && !flag(a,"noLayout")) : expected ?? review(number(a,"valueID",card==nil ? 20:number(a,"card",21)),"original",card != nil && !flag(a,"noLayout"))
      value.plan.answer=text(a,"answer","edited");if a["valueID"] != nil{value.plan.id=id(number(a,"valueID",20))};if flag(a,"noLayout"){value.plan.layout=nil}
      if flag(a,"badBinding"){value.bindings=[binding([:])];value.bindings[0].fields=[:];value.activeBindingID=value.bindings[0].id}
      if text(a,"op")=="external"{_ = try await archive.saveReview(entryID:entryID,senseID:senseID,value:value,expected:live(card),cardID:card)}
      else{try await s.saveReview(entryID:entryID,senseID:senseID,value:value,expected:expected,cardID:card)}
     case "record":try await s.recordAnkiBinding(entryID:entryID,senseID:senseID,binding:binding(a),cardID:card)
     case "remove":try await s.removeReview(entryID:entryID,senseID:senseID,cardID:card!,expected:expected!)
     case "template","cancelTemplate":
      let template=ReviewLayoutTemplate(id:id(number(a,"id",70)),name:text(a,"name","Saved"),layout:flag(a,"badLayout") ? .init():layout(number(a,"part",3000)))
      if text(a,"op")=="cancelTemplate"{let work=Task{try await s.saveTemplate(template)};work.cancel();try await work.value}
      else{try await s.saveTemplate(template)}
     case "removeTemplate":try await s.removeTemplate(id(number(a,"id",70)))
     case "sharedError":_ = await s.removeSense(entryID:id(999),senseID:id(888))
     case "reload":await s.load()
     default:fatalError("Unknown review transaction action")
     }
    }catch is CancellationError{outcome = .object(["error":.string("cancelled")])}
    catch{outcome = .object(["error":.string(error.localizedDescription)])}
    trace.append(.object(["state":try await snapshot(s,archive,revision),"outcome":outcome]))
   }
   output.append(.object(["id":r["id"]!,"expected":.array(trace)]))
  }
  let e=JSONEncoder();e.outputFormatting=[.sortedKeys];FileHandle.standardOutput.write(try e.encode(AnkiValue.array(output)))
 }
}
'''

def sources(repo):
    values=vocabulary_store_sources(repo);marker='\nextension VocabularyStore {\n    func applyProbe('
    if values[-1].count(marker)!=1:raise RuntimeError('Store test extension seam changed')
    wrapper=values[-1][:values[-1].index(marker)]
    wrapper+='\nextension VocabularyStore {\n'+between(source(repo,STORE),'    func saveReview(','    func remove(_ capture:')+'}\n'
    return values[:-1]+[wrapper,source(repo,'Cotabby/Models/Review/AnkiWireModels.swift')]

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--mac-repo',type=Path,required=True)
    p.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/vocabulary-review-v1.json');p.add_argument('--capture-output',type=Path)
    a=p.parse_args();c=json.loads(a.fixture.read_text());rows=c['cases'];ids=[r['id']for r in rows]
    if c.get('version')!=1 or c.get('macReference')!=BASELINE or not rows or len(ids)!=len(set(ids)):p.error('Pinned unique review transaction cases required')
    if hashlib.sha256('\n'.join(sorted(ids)).encode()).hexdigest()!=ID_HASH:p.error('Reviewed transaction matrix changed')
    if any(r.get('macSource')!=STORE or r.get('provenanceKind')!='source-policy-extension'for r in rows):p.error('Original review Store provenance required')
    with tempfile.TemporaryDirectory(prefix='lingrove-review-store-')as temporary:
        root=Path(temporary);files=[]
        for i,v in enumerate(sources(a.mac_repo)+[DRIVER]):
            f=root/f'source{i}.swift';f.write_text(v);files.append(str(f))
        binary=root/'review-store';subprocess.run(['swiftc','-swift-version','6','-O','-parse-as-library','-lsqlite3',*files,'-o',str(binary)],check=True)
        actual=json.loads(subprocess.check_output([str(binary),str(a.fixture)],timeout=120))
    if a.capture_output:
        with a.capture_output.open('x',encoding='utf-8')as out:json.dump(actual,out,ensure_ascii=False,indent=2);out.write('\n')
        print(f'Captured {len(actual)} original review Store/archive transaction traces');return
    if len(actual)!=len(rows)or any(v!={'id':r['id'],'expected':r['expected']}for r,v in zip(rows,actual)):raise SystemExit('Original review transaction mismatch')
    print(f'Verified {len(rows)} original review Store/archive transaction traces')
if __name__=='__main__':main()
