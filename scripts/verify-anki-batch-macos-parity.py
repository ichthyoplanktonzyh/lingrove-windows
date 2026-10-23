#!/usr/bin/env python3
"""Execute the complete original serial batch over the original single-session/archive graph."""
import argparse,hashlib,importlib.util,json,subprocess,tempfile
from pathlib import Path
from macos_vocabulary_reference import BASELINE,source
BATCH='Cotabby/Services/Review/AnkiReviewBatchSession.swift'
MODELS='Cotabby/Models/Review/ReviewBatchModels.swift'
ID_HASH='223d91348f88aa8d2b4158802bcdc0dbf0573da2cee05859fafb8c68692aea02'
spec=importlib.util.spec_from_file_location('anki_session_reference',Path(__file__).with_name('verify-anki-session-macos-parity.py'));shared=importlib.util.module_from_spec(spec);spec.loader.exec_module(shared)
DRIVER=r'''
@MainActor func batchState(_ batch:AnkiReviewBatchSession,_ archive:VocabularyArchive,_ client:ObservedAnki,_ speech:ReviewFakeSpeechExporter)async throws->AnkiValue{
 let disk=try await archive.load();let c=batch.controller,s=c.settings
 func encoded(_ d:VocabularyDocument)throws->AnkiValue{try JSONDecoder().decode(AnkiValue.self,from:JSONEncoder().encode(d))}
 let items=batch.items.map{item->AnkiValue in
  let resolved=batch.resolve(item)
  return .object(["id":.string(item.id.uuidString.lowercased()),"entryID":.string(item.selection.entryID.uuidString.lowercased()),"senseID":.string(item.selection.senseID.uuidString.lowercased()),"cardID":item.cardID.map{.string($0.uuidString.lowercased())} ?? .null,
   "expression":.string(item.expression),"meaning":.string(item.meaning),"status":.string(String(describing:item.status)),"label":.string(item.status.label),"message":item.message.map(AnkiValue.string) ?? .null,
   "canonicalSenseID":resolved.map{.string($0.1.id.uuidString.lowercased())} ?? .null])
 }
 return .object(["items":.array(items),"busy":.bool(batch.busy),"work":.bool(batch.work != nil),"message":batch.message.map(AnkiValue.string) ?? .null,"empty":.number(Double(batch.emptySenseCount)),"complete":.number(Double(batch.completeCount)),
 "settings":.object(["deck":.string(s.deck),"recognition":.bool(s.defaultRecognition),"gap":.bool(s.defaultGap),"hasSavedDeck":.bool(s.hasSavedDeck)]),"store":try encoded(.init(entries:c.store.entries,reviewTemplates:disk.document.reviewTemplates)),"disk":try encoded(disk.document),"server":await client.serverState(),"exports":.number(Double(speech.exports)),"failures":.object(c.failures.mapKeysForSession)])
}
@main struct Main{
 @MainActor static func main()async throws{
  precondition(Locale.current.identifier=="en_US")
  let input=try JSONDecoder().decode(AnkiValue.self,from:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1])));var results:[AnkiValue]=[]
  for value in input.object!["cases"]!.array!{
   var row=value.object!;if row["secondSense"]==nil{row["secondSense"] = .bool(true)}
   let root=FileManager.default.temporaryDirectory.appendingPathComponent("lingrove-batch-"+UUID().uuidString),name="lingrove-batch-"+UUID().uuidString
   try FileManager.default.createDirectory(at:root,withIntermediateDirectories:true,attributes:[.posixPermissions:0o700]);defer{try? FileManager.default.removeItem(at:root)}
   let archive=VocabularyArchive(url:root.appendingPathComponent("notebook.sqlite")),store=VocabularyStore(archive:archive)
   var document=seed(row)
   if let cards=row["cards"]?.array{document.entries[0].senses[0].reviewCards=try cards.map{VocabularyReview(plan:try JSONDecoder().decode(ReviewPlan.self,from:JSONEncoder().encode($0)))}}
   if flag(row,"alias"){document.entries[0].senses[0].mergedFrom=[id(9)]}
   try await archive.seedSessionProbe(document,nil);await store.load()
   let defaults=UserDefaults(suiteName:name)!;defer{defaults.removePersistentDomain(forName:name)}
   let settings=AnkiReviewSettings(defaults:defaults,credentials:AnkiCredentialStore(service:nil)),client=ObservedAnki(),speech=ReviewFakeSpeechExporter()
   if let initial=row["settings"]?.object{preferences(initial,settings)}
   let controller=AnkiReviewController(settings:settings,store:store,client:client,speech:flag(row,"speech") ? speech:nil,activateAnki:{})
   if flag(row,"prime"){let e=store.entries[0];let s=AnkiReviewSession(controller:controller,entry:e,sense:e.senses[0]);let ok=await s.send();precondition(ok,s.message ?? "Prime failed")}
   if flag(row,"primeCards"){let e=store.entries[0];for card in e.senses[0].reviewCards ?? []{let s=AnkiReviewSession(controller:controller,entry:e,sense:store.entries[0].senses[0],cardID:card.plan.id);let ok=await s.send();precondition(ok,s.message ?? "Prime failed")}}
   let selections=(row["selections"]?.array ?? [.number(2),.number(8)]).map{VocabularySelection(entryID:id(1),senseID:id(Int($0.integer!)))}
   var batch=AnkiReviewBatchSession(controller:controller,selections:selections,flexible:flag(row,"flexible"))
   var trace:[AnkiValue]=[.object(["state":try await batchState(batch,archive,client,speech),"outcome":.null])]
   for value in row["actions"]!.array!{
    let a=value.object!;var outcome:AnkiValue = .null
    switch text(a,"op"){
    case "start":batch.start(only:a["only"] == nil ? nil:batch.items[number(a,"only",0)].id);await batch.work?.value
    case "startTwice":batch.start();let work=batch.work;outcome = .bool(batch.busy);batch.start();batch.startApply(deck:"Ignored",cards:2,audio:4);await work?.value
    case "apply":batch.startApply(deck:text(a,"deck"),cards:number(a,"cards",0),audio:number(a,"audio",0));await batch.work?.value
    case "stopBeforeRun":batch.start();let work=batch.work;batch.stop();await work?.value
    case "stopBeforeApply":batch.startApply(deck:"Other",cards:2,audio:4);let work=batch.work;batch.stop();await work?.value
    case "stopAudio":
     let previous=speech.exports;speech.delay = .seconds(3600);batch.start();let work=batch.work
     for _ in 0..<10000 where speech.exports==previous && batch.busy{await Task.yield()};precondition(speech.exports>previous)
     batch.stop();await work?.value;speech.delay = .zero
    case "stopAfterFirst":let current=batch;await client.afterNote{current.stop()};batch.start();await batch.work?.value
    case "removeMedia":await client.inner.removeMedia()
    case "newBatch":batch=AnkiReviewBatchSession(controller:controller,selections:selections,flexible:flag(row,"flexible"))
    case "settings":preferences(a,settings)
    case "afterMetadata":await client.inner.afterNextMetadata{preferences(a,settings)}
    case "profileMetadata":await client.inner.switchProfileAfterNextMetadata(text(a,"value"))
    case "decks":await client.inner.setDecks(a["values"]!.array!.map{$0.string!})
    case "failAdd":await client.inner.failOneAdd()
    case "loseAdd":await client.inner.loseNextAddReply()
    case "edit":await client.inner.edit(Int64(number(a,"id",1001)),field:text(a,"field","Meaning"),text:text(a,"value"))
    case "begin":batch.beginPreparation(batch.items[number(a,"index",0)].id)
    case "refresh":batch.refresh(batch.items[number(a,"index",0)].id)
    case "prepareSave":let item=batch.items[number(a,"index",0)],current=batch.resolve(item)!;let draft=AnkiReviewSession(controller:controller,entry:current.0,sense:current.1,cardID:item.cardID);draft.plan.answer=text(a,"value","edited");outcome = .bool(await draft.save())
    case "merge":outcome = .bool(await store.mergeSenses(entryID:id(1),source:id(2),target:id(8)))
    case "remove":outcome = .bool(await store.removeSense(entryID:id(1),senseID:id(number(a,"sense",2))))
    default:fatalError("Unknown batch action")
    }
    trace.append(.object(["state":try await batchState(batch,archive,client,speech),"outcome":outcome]))
   }
   let observation:AnkiValue = .object(["trace":.array(trace),"calls":.array(await client.observed())]);var ids:[String:String]=[:];var times:[Double:String]=[:]
   results.append(.object(["id":row["id"]!,"expected":canonical(observation,ids:&ids,times:&times,generated:generatedIDs(observation))]))
  }
  let encoder=JSONEncoder();encoder.outputFormatting=[.sortedKeys];FileHandle.standardOutput.write(try encoder.encode(AnkiValue.array(results)))
 }
}
'''
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--mac-repo',type=Path,required=True);p.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/anki-batch-v1.json');p.add_argument('--capture-output',type=Path)
 a=p.parse_args();c=json.loads(a.fixture.read_text());rows=c['cases'];ids=[r['id']for r in rows]
 if c.get('referenceLocale')!='en_US'or c.get('version')!=1 or c.get('macReference')!=BASELINE or len(ids)!=len(set(ids))or hashlib.sha256('\n'.join(sorted(ids)).encode()).hexdigest()!=ID_HASH:p.error('Pinned reviewed batch matrix required')
 if any(r.get('macSource')!=BATCH or r.get('provenanceKind')!='source-policy-extension'for r in rows):p.error('Original batch provenance required')
 prefix=shared.DRIVER[:shared.DRIVER.index('@main struct Main{')]
 values=shared.sources(a.mac_repo)[:-1]+[source(a.mac_repo,MODELS),source(a.mac_repo,BATCH),prefix+DRIVER]
 with tempfile.TemporaryDirectory(prefix='lingrove-batch-source-')as directory:
  root=Path(directory);files=[]
  for i,value in enumerate(values):
   f=root/f'source{i}.swift';f.write_text(value);files.append(str(f))
  binary=root/'probe';subprocess.run(['swiftc','-swift-version','6','-O','-whole-module-optimization','-parse-as-library','-lsqlite3',*files,'-o',str(binary)],check=True)
  actual=json.loads(subprocess.check_output([str(binary),str(a.fixture),'-AppleLocale','en_US','-AppleLanguages','(en)'],timeout=120))
 if a.capture_output:
  with a.capture_output.open('x',encoding='utf-8')as out:json.dump(actual,out,ensure_ascii=False,indent=2);out.write('\n')
  print(f'Captured {len(actual)} complete original batch traces');return
 mismatches=[r['id']for r,v in zip(rows,actual)if v!={'id':r['id'],'expected':r['expected']}]
 if len(actual)!=len(rows)or mismatches:raise SystemExit(f'Original batch mismatch: {mismatches}')
 print(f'Verified {len(rows)} complete original batch traces')
if __name__=='__main__':main()
