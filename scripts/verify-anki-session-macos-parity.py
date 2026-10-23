#!/usr/bin/env python3
"""Execute the complete original review session over real isolated archive/SQLite and test effects."""
import argparse,hashlib,json,subprocess,tempfile
from pathlib import Path
from macos_vocabulary_reference import BASELINE,between,source,vocabulary_store_sources
SESSION='Cotabby/Services/Review/AnkiReviewSession.swift'
IMAGES='Cotabby/Services/Review/AnkiReviewSession+Images.swift'
TEST='CotabbyTests/Services/Review/AnkiReviewTests.swift'
ID_HASH='663156fe213619f015db843864c7924f00d69b6796ca9ec9ed8ab58216a7ef22'
DRIVER=r'''
import Foundation
import CryptoKit
func id(_ n:Int)->UUID{UUID(uuidString:String(format:"00000000-0000-0000-0000-%012x",n))!}
func text(_ r:[String:AnkiValue],_ k:String,_ fallback:String="")->String{r[k]?.string ?? fallback}
func flag(_ r:[String:AnkiValue],_ k:String)->Bool{if case let .bool(v)=r[k]{return v};return false}
func number(_ r:[String:AnkiValue],_ k:String,_ fallback:Int)->Int{r[k]?.integer.map(Int.init) ?? fallback}
func image(_ r:[String:AnkiValue])->VocabularyImageReference{let data=Data(base64Encoded:r["imagePng"]!.string!)!;return .init(digest:SpeechExportPolicy.digest(data),width:80,height:40,byteCount:data.count)}
func seed(_ r:[String:AnkiValue])->VocabularyDocument{
 let time=Date(timeIntervalSince1970:0)
 let usage=VocabularyExample(id:id(3),text:"The bank opened early.",translation:"银行很早开门。",ranges:[.init(NSRange(location:4,length:4))],completeness:.sentence,meaning:"银行",createdAt:time)
 var sense=VocabularySense(id:id(2),meaning:"银行",meaningLanguage:.chinese,note:"source note",examples:flag(r,"noExample") ? []:[usage],isSavedWithoutContext:true,createdAt:time,updatedAt:time)
 if let saved=r["savedPlan"] {let plan=try! JSONDecoder().decode(ReviewPlan.self,from:JSONEncoder().encode(saved));let review=VocabularyReview(plan:plan);if flag(r,"card"){sense.reviewCards=[review]}else{sense.review=review}}
 if flag(r,"image"){sense.content=[.init(id:id(20),content:.image(image(r),caption:"river"))]}
 var senses=[sense]
 if flag(r,"secondSense"){var second=sense;second=VocabularySense(id:id(8),meaning:"河岸",meaningLanguage:.chinese,isSavedWithoutContext:false,createdAt:time,updatedAt:time);senses.append(second)}
 return .init(entries:[.init(id:id(1),expression:"bank",dictionaryForm:"bank",language:.english,kind:.word,senses:senses,createdAt:time,updatedAt:time)])
}
extension VocabularyArchive{func seedSessionProbe(_ d:VocabularyDocument,_ data:Data?)throws{if let data,let image=d.entries[0].senses[0].content.flatMap(VocabularyContentPolicy.images)?.first{_ = try commitWithImages(d,supplied:[image.filename:data])}else{_ = try commit(d)}}}
actor ObservedAnki:AnkiConnecting{
 let inner=ReviewFakeAnki();var calls:[AnkiValue]=[]
 var cancelAfterMediaProfile: (@MainActor @Sendable ()->Void)?;var sawMedia=false;var noteHook: (@MainActor @Sendable ()->Void)?;var metadataHook: (@MainActor @Sendable ()async->Void)?
 func afterNote(_ action:@escaping @MainActor @Sendable ()->Void){noteHook=action}
 func afterMetadata(_ action:@escaping @MainActor @Sendable ()async->Void){metadataHook=action}
 func cancelAfterMedia(_ action:@escaping @MainActor @Sendable ()->Void){cancelAfterMediaProfile=action}
 func perform(_ action:String,params:[String:AnkiValue],connection:AnkiConnection)async throws->AnkiValue{
  calls.append(.object(["action":.string(action),"params":.object(params)]))
  let result=try await inner.perform(action,params:params,connection:connection)
  if action=="notesInfo",let hook=noteHook{noteHook=nil;await hook()}
  if action=="modelFieldNames",let hook=metadataHook{metadataHook=nil;await hook()}
  if action=="storeMediaFile"{sawMedia=true}
  else if action=="getActiveProfile",sawMedia,let hook=cancelAfterMediaProfile{cancelAfterMediaProfile=nil;sawMedia=false;await hook()}
  return result
 }
 func observed()->[AnkiValue]{calls}
 func serverState()async->AnkiValue{
  let notes=await inner.notes,models=await inner.noteModels,decks=await inner.noteDecks,media=await inner.media,counts=await inner.counts()
  return .object(["add":.number(Double(counts.add)),"update":.number(Double(counts.update)),"notes":.array(notes.keys.sorted().map{.object(["id":.number(Double($0)),"fields":.object(notes[$0]!.mapValues(AnkiValue.string)),"model":.string(models[$0]!),"deck":.string(decks[$0]!)])}),"media":.object(media.mapValues{.object(["bytes":.number(Double($0.count)),"sha256":.string(SpeechExportPolicy.digest($0))])})])
 }
}
@MainActor final class CancellationBox {var work:Task<Bool,Never>?}
@MainActor func preferences(_ a:[String:AnkiValue],_ settings:AnkiReviewSettings){
 if let v=a["deck"]?.string{settings.deck=v};if let v=a["model"]?.string{settings.model=v};if let v=a["address"]?.string{settings.address=v}
 if case let .bool(v)=a["recognition"]{settings.defaultRecognition=v};if case let .bool(v)=a["gap"]{settings.defaultGap=v}

}
@MainActor func state(_ s:AnkiReviewSession,_ archive:VocabularyArchive,_ client:ObservedAnki,_ speech:ReviewFakeSpeechExporter)async throws->AnkiValue{
 let disk=try await archive.load()
 let document=VocabularyDocument(entries:s.controller.store.entries,reviewTemplates:disk.document.reviewTemplates)
 let plan=try JSONDecoder().decode(AnkiValue.self,from:JSONEncoder().encode(s.plan))
 func encoded(_ d:VocabularyDocument)throws->AnkiValue{try JSONDecoder().decode(AnkiValue.self,from:JSONEncoder().encode(d))}
 return .object(["plan":plan,"deck":.string(s.deck),"model":.string(s.model),"answer":.string(s.answer),"canGap":.bool(s.canGap),
 "validation":s.validationMessage.map(AnkiValue.string) ?? .null,"message":s.message.map(AnkiValue.string) ?? .null,
 "busy":.bool(s.busy),"loading":.bool(s.loading),"missing":.bool(s.missingNote),"bindingID":s.bindingID.map{.string($0.uuidString.lowercased())} ?? .null,
 "currentTemplate":.bool(s.currentPresetTemplate),"legacyTemplate":.bool(s.legacyPresetTemplate),"safeGapFront":.bool(s.safeGapFront),
 "css":.string(s.presetCSS),"fields":.list(s.availableFields),"conflicts":.array(s.conflicts.map{.object(["id":.string($0.id),"local":.string($0.local),"remote":.string($0.remote)])}),
 "store":try encoded(document),"disk":try encoded(disk.document),
 "settings":.object(["deck":.string(s.controller.settings.deck),"recognition":.bool(s.controller.settings.defaultRecognition),"gap":.bool(s.controller.settings.defaultGap),"hasSavedDeck":.bool(s.controller.settings.hasSavedDeck)]),
 "status":.string(s.current.map{s.controller.status($0.entry,$0.sense,cardID:s.cardID)} ?? "Unavailable"),
 "failures":.object(s.controller.failures.mapKeysForSession),"unresolved":.bool(s.controller.unresolved.contains(s.cardID ?? s.senseID)),"controllerMissing":.bool(s.controller.missing.contains(s.cardID ?? s.senseID)),
 "keepRemote":.object(s.keepRemote.mapValues(AnkiValue.bool)),"exports":.number(Double(speech.exports)),"generating":s.audio.generating.map{.string($0.rawValue)} ?? .null,
 "prepared":.object(s.audio.prepared.map{($0.key.rawValue,AnkiValue.object(["fingerprint":.string($0.value.fingerprint),"bytes":.number(Double($0.value.audio.data.count)),"sha256":.string(SpeechExportPolicy.digest($0.value.audio.data))]))}.reduce(into:[:]){$0[$1.0]=$1.1}),"server":await client.serverState()])
}
extension Dictionary where Key==UUID,Value==String{var mapKeysForSession:[String:AnkiValue]{Dictionary<String,AnkiValue>(uniqueKeysWithValues:map{($0.key.uuidString.lowercased(),.string($0.value))})}}
func generatedIDs(_ v:AnkiValue)->Set<String>{
 switch v{case let .object(o):return o.reduce(into:Set<String>()){result,p in
  if ["calls","server","fields","fieldOverrides","keepRemote","failures"].contains(p.key){return}
  if p.key=="id",let raw=p.value.string,UUID(uuidString:raw) != nil,!raw.lowercased().hasPrefix("00000000-0000-0000-0000-"){result.insert(raw.lowercased())};result.formUnion(generatedIDs(p.value))
 };case let .array(a):return a.reduce(into:Set<String>()){$0.formUnion(generatedIDs($1))};default:return []}
}
func canonical(_ value:AnkiValue,_ key:String="",ids:inout[String:String],times:inout[Double:String],generated:Set<String>)->AnkiValue{
 switch value{
 case let .string(s):
  let expression=try! NSRegularExpression(pattern:"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")
  let ns=s as NSString;var result=s
  for match in expression.matches(in:s,range:NSRange(location:0,length:ns.length)).reversed(){
   let raw=ns.substring(with:match.range),lower=raw.lowercased()
   if !generated.contains(lower){continue}
   if ids[lower]==nil{ids[lower]="generated-\(ids.count+1)"}
   result=(result as NSString).replacingCharacters(in:match.range,with:ids[lower]!)
  }
  if result.utf8.count>2000{return .object(["bytes":.number(Double(result.utf8.count)),"sha256":.string(SpeechExportPolicy.digest(Data(result.utf8)))])}
  return .string(result)
 case let .object(o):
  var result:[String:AnkiValue]=[:]
  for key in o.keys.sorted(){if ["syncedAt","createdAt","updatedAt"].contains(key),case let .number(n)=o[key]!,n != -978307200{if times[n]==nil{times[n]="time-\(times.count+1)"};result[key] = .string(times[n]!)}else{result[key]=canonical(o[key]!,key,ids:&ids,times:&times,generated:generated)}}
  return .object(result)
 case let .array(a):
  if ["mapping","audioReceipts"].contains(key){
   precondition(a.count%2==0);let indices=stride(from:0,to:a.count,by:2).sorted{a[$0].string!<a[$1].string!}
   return .array(indices.flatMap{[a[$0],canonical(a[$0+1],ids:&ids,times:&times,generated:generated)]})
  }
  let values=a.map{canonical($0,ids:&ids,times:&times,generated:generated)};return .array(key=="audioKinds" ? values.sorted{$0.string!<$1.string!}:values)
 default:return value
 }
}
@main struct Main{
 @MainActor static func main()async throws{
  precondition(Locale.current.identifier=="en_US","Reference error locale must be en_US")
  let input=try JSONDecoder().decode(AnkiValue.self,from:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1])));var output:[AnkiValue]=[]
  for row in input.object!["cases"]!.array!{
   var r=row.object!;r["imagePng"]=input.object!["imagePng"];let root=FileManager.default.temporaryDirectory.appendingPathComponent("lingrove-anki-session-"+UUID().uuidString),name="lingrove-session-"+UUID().uuidString
   try FileManager.default.createDirectory(at:root,withIntermediateDirectories:true,attributes:[.posixPermissions:0o700]);defer{try? FileManager.default.removeItem(at:root)}
   let defaults=UserDefaults(suiteName:name)!;defer{defaults.removePersistentDomain(forName:name)}
   let archive=VocabularyArchive(url:root.appendingPathComponent("notebook.sqlite")),store=VocabularyStore(archive:archive)
   try await archive.seedSessionProbe(seed(r),flag(r,"imageAvailable") ? Data(base64Encoded:r["imagePng"]!.string!):nil);await store.load()
   let settings=AnkiReviewSettings(defaults:defaults,credentials:AnkiCredentialStore(service:nil)),client=ObservedAnki(),speech=ReviewFakeSpeechExporter()
   if let initial=r["settings"]?.object{preferences(initial,settings)}
   let controller=AnkiReviewController(settings:settings,store:store,client:client,speech:flag(r,"speech") ? speech:nil,activateAnki:{})
   let entry=store.entries[0];var session=AnkiReviewSession(controller:controller,entry:entry,sense:entry.senses[0],cardID:flag(r,"card") ? id(4):nil)
   var trace:[AnkiValue]=[.object(["state":try await state(session,archive,client,speech),"outcome":.null])]
   for action in r["actions"]!.array!{
    let a=action.object!;var outcome:AnkiValue = .null
    switch text(a,"op"){
    case "load":await session.load()
    case "save":outcome = .bool(await session.save())
    case "send":outcome = .bool(await session.send(recreateMissing:flag(a,"recreate")))
    case "sendCancelled":let draft=session;let work=Task{@MainActor in withUnsafeCurrentTask{$0?.cancel()};return await draft.send()};outcome = .bool(await work.value)
    case "cancelAfterMedia":
     let box=CancellationBox();await client.cancelAfterMedia{box.work?.cancel()}
     let draft=session;box.work=Task{@MainActor in await draft.send()};outcome = .bool(await box.work!.value)
    case "cancelAudio":
     speech.delay = .seconds(3600);let draft=session;let work=Task{@MainActor in await draft.send()}
     for _ in 0..<10000 where speech.exports==0 {await Task.yield()};precondition(speech.exports>0);work.cancel();outcome = .bool(await work.value);speech.delay = .zero
    case "reopen":let e=store.entries[0];session=AnkiReviewSession(controller:controller,entry:e,sense:e.senses[0],cardID:flag(r,"card") ? id(4):nil)
    case "openSense":let e=store.entries[0];let sense=e.senses.first{$0.id==id(number(a,"sense",2))}!;session=AnkiReviewSession(controller:controller,entry:e,sense:sense)
    case "merge":outcome = .bool(await store.mergeSenses(entryID:id(1),source:id(number(a,"source",2)),target:id(number(a,"target",8))));let e=store.entries[0];session=AnkiReviewSession(controller:controller,entry:e,sense:e.senses[0])
    case "select":session.selectBinding(flag(a,"none") ? nil:session.savedReview!.bindings[number(a,"index",0)].id)
    case "externalSave":let e=store.entries[0];let other=AnkiReviewSession(controller:controller,entry:e,sense:e.senses.first{$0.id==session.senseID}!,cardID:session.cardID);other.plan.answer=text(a,"value","external");outcome = .bool(await other.save())
    case "loadNested":let draft=session;await client.afterMetadata{settings.model="Custom";await draft.load()};await session.load()
    case "cancelAfterNote":let box=CancellationBox();await client.afterNote{box.work?.cancel()};let draft=session;box.work=Task{@MainActor in await draft.send()};outcome = .bool(await box.work!.value)
    case "importImage":
     let reference=image(r),block=VocabularyContentBlock(id:id(21),content:.image(reference,caption:"draft"))
     session.plan.layout = .init(front:[.init(id:id(22),source:.expression)],back:[.init(id:id(23),source:.custom,content:[block])]);session.importedImages=[reference.filename:Data(base64Encoded:r["imagePng"]!.string!)!]
    case "settings":preferences(a,settings)
    case "afterMetadata":let draft=session;await client.inner.afterNextMetadata{
     preferences(a,settings);if let v=a["answer"]?.string{draft.plan.answer=v}
    }
    case "profile":await client.inner.setProfile(text(a,"value"))
    case "profileMetadata":await client.inner.switchProfileAfterNextMetadata(text(a,"value"))
    case "profileMedia":await client.inner.switchProfileAfterNextMedia(text(a,"value"))
    case "decks":await client.inner.setDecks(a["values"]!.array!.map{$0.string!})
    case "fields":await client.inner.setFields(a["values"]!.array!.map{$0.string!},for:text(a,"model",AnkiReviewPolicy.presetName))
    case "loseAdd":await client.inner.loseNextAddReply()
    case "failAdd":await client.inner.failOneAdd()
    case "edit":await client.inner.edit(Int64(number(a,"id",1001)),field:text(a,"field","Meaning"),text:text(a,"value"))
    case "delete":await client.inner.delete(Int64(number(a,"id",1001)))
    case "legacy":await client.inner.setLegacyTemplate()
    case "customize":await client.inner.customizeTemplate()
    case "unsafeFlexible":await client.inner.unsafeFlexibleTemplate()
    case "removeMedia":await client.inner.removeMedia()
    case "updateTemplate":await session.updateTemplate()
    case "keep":session.keepRemote[text(a,"field","Meaning")]=flag(a,"value")
    case "restore":var saved=session.plan;saved.recognition=false;saved.deck=text(a,"deck","Other");session.restorePlan(saved)
    case "name":session.plan.name=text(a,"value")
    case "recognition":session.plan.recognition=flag(a,"value")
    case "gap":session.plan.gap=flag(a,"value")
    case "notes":session.plan.includeNotes=flag(a,"value")
    case "audio":session.plan.audioKinds=Set(a["values"]!.array!.map{ReviewAudioKind(rawValue:$0.string!)!})
    case "prepare":await session.prepareAudio(ReviewAudioKind(rawValue:text(a,"kind","expression"))!,audition:flag(a,"audition"),force:flag(a,"force"))
    case "override":session.plan.fieldOverrides[text(a,"field")]=text(a,"value")
    case "answer":session.plan.answer=text(a,"value")
    case "deck":session.deck=text(a,"value")
    default:fatalError("Unknown original session action")
    }
    trace.append(.object(["state":try await state(session,archive,client,speech),"outcome":outcome]))
   }
   var ids:[String:String]=[:];var times:[Double:String]=[:];let observation:AnkiValue = .object(["trace":.array(trace),"calls":.array(await client.observed())])
   output.append(.object(["id":r["id"]!,"expected":canonical(observation,ids:&ids,times:&times,generated:generatedIDs(observation))]))
  }
  let encoder=JSONEncoder();encoder.outputFormatting=[.sortedKeys];FileHandle.standardOutput.write(try encoder.encode(AnkiValue.array(output)))
 }
}
'''
def sources(repo):
 values=vocabulary_store_sources(repo);store=source(repo,'Cotabby/Services/Vocabulary/VocabularyStore.swift');marker='\nextension VocabularyStore {\n    func applyProbe('
 assert values[-1].count(marker)==1
 wrapper=values[-1][:values[-1].index(marker)]+'\nextension VocabularyStore {\n'+between(store,'    func saveReview(','    func remove(_ capture:')+between(store,'    func prepareImage(_ data:','    func backup(to url:')+'}\n'
 values=values[:-1]+[wrapper]
 values=[v for v in values if 'nonisolated enum ReviewCardPolicy {'not in v and 'nonisolated enum AnkiReviewPolicy {'not in v and 'extension AnkiReviewPolicy {'not in v and 'nonisolated enum ReviewLayoutPolicy {'not in v and 'nonisolated enum SpeechExportPolicy {'not in v]
 paths=['Cotabby/Support/Review/ReviewCardPolicy.swift','Cotabby/Support/Review/ReviewLayoutPolicy.swift','Cotabby/Support/Review/AnkiNoteTemplate.swift','Cotabby/Support/Review/AnkiReviewPolicy.swift','Cotabby/Models/Review/AnkiWireModels.swift','Cotabby/Models/Review/ReviewOperationScope.swift','Cotabby/Models/Review/AnkiReviewSettings.swift','Cotabby/Services/Review/AnkiConnectClient.swift','Cotabby/Models/Runtime/OpenAICompatibleEndpointModels.swift','Cotabby/Models/Runtime/Parameters/ModelSamplingOverrides.swift','Cotabby/Models/Speech/LearningSpeechModels.swift','Cotabby/Models/Speech/SpeechExportModels.swift','Cotabby/Support/Speech/SpeechExportPolicy.swift','Cotabby/Services/Review/ReviewAudioPreparation.swift','Cotabby/Services/LanguageLearning/LearningEndpointSession.swift']
 values += [source(repo,p)for p in paths]
 audio=source(repo,'Cotabby/Models/Review/ReviewAudioModels.swift');values+=['import Foundation\n'+audio[audio.index('/// A preparation sheet retains only'):]]
 units=source(repo,'Cotabby/Models/LanguageLearning/LearningUnitModels.swift');words=source(repo,'Cotabby/Models/LanguageLearning/LearningDictionaryWord.swift');speech=source(repo,'Cotabby/Support/Speech/SpeechTextPolicy.swift')
 values+=['import Foundation\n'+between(units,'nonisolated struct LearningUnit:','/// Recognition and lookup share a resource,')+between(words,'nonisolated struct LearningDictionaryWord:','/// View-local geometry exists'),speech]
 controller=source(repo,'Cotabby/Services/Review/AnkiReviewController.swift')
 diagnostics=['import Logging\n','            CotabbyLogger.learning.info("Anki connection verified")\n','            CotabbyLogger.learning.error("Anki connection failed")\n','        CotabbyLogger.learning.info("Anki operation finished", metadata: ["success": .stringConvertible(error == nil && wrote),\n            "cancelled": .stringConvertible(cancelled)])\n']
 for diagnostic in diagnostics:
  assert controller.count(diagnostic)==1;controller=controller.replace(diagnostic,'')
 test=source(repo,TEST)
 values+=[controller,source(repo,'Cotabby/Services/Review/AnkiReviewController+Navigation.swift'),source(repo,SESSION),source(repo,IMAGES),'import Foundation\n'+between(test,'actor ReviewFakeAnki:','@Suite(.serialized)'),'import Foundation\n'+test[test.index('@MainActor\nfinal class ReviewFakeSpeechExporter:'):],DRIVER]
 return values

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--mac-repo',type=Path,required=True);p.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/anki-session-v1.json');p.add_argument('--capture-output',type=Path)
 a=p.parse_args();c=json.loads(a.fixture.read_text());rows=c['cases'];ids=[r['id']for r in rows]
 if c.get('referenceLocale')!='en_US' or c.get('version')!=1 or c.get('macReference')!=BASELINE or len(ids)!=len(set(ids)) or hashlib.sha256('\n'.join(sorted(ids)).encode()).hexdigest()!=ID_HASH:p.error('Pinned reviewed session matrix required')
 if any(r.get('macSource')!=SESSION or r.get('provenanceKind')!='source-policy-extension'for r in rows):p.error('Original session provenance required')
 with tempfile.TemporaryDirectory(prefix='lingrove-anki-session-source-')as directory:
  root=Path(directory);files=[]
  for i,value in enumerate(sources(a.mac_repo)):
   f=root/f'source{i}.swift';f.write_text(value);files.append(str(f))
  binary=root/'probe';subprocess.run(['swiftc','-swift-version','6','-O','-whole-module-optimization','-parse-as-library','-lsqlite3',*files,'-o',str(binary)],check=True)
  actual=json.loads(subprocess.check_output([str(binary),str(a.fixture),"-AppleLocale","en_US","-AppleLanguages","(en)"],timeout=120))
 if a.capture_output:
  with a.capture_output.open('x',encoding='utf-8')as out:json.dump(actual,out,ensure_ascii=False,indent=2);out.write('\n')
  print(f'Captured {len(actual)} original session traces');return
 mismatches=[r['id']for r,v in zip(rows,actual)if v!={'id':r['id'],'expected':r['expected']}]
 if len(actual)!=len(rows)or mismatches:raise SystemExit(f'Original session mismatch: count={len(actual)}/{len(rows)}, cases={mismatches}')
 print(f'Verified {len(rows)} complete original session traces')
if __name__=='__main__':main()
