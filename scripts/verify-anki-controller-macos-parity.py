#!/usr/bin/env python3
"""Execute original review controller/navigation with injected synthetic transport and UI effects."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, between, sense_policy_sources, source

CONTROLLER = 'Cotabby/Services/Review/AnkiReviewController.swift'
NAVIGATION = 'Cotabby/Services/Review/AnkiReviewController+Navigation.swift'
ID_HASH = 'd33323cd4ea584cec32b504c220f01e21c7712e465846bddbd7e7ddcebfd0c77'
DRIVER = r'''
import Foundation
import CryptoKit
func id(_ n:Int)->UUID {UUID(uuidString:String(format:"00000000-0000-0000-0000-%012x",n))!}
func text(_ r:[String:AnkiValue],_ k:String,_ fallback:String="")->String {r[k]?.string ?? fallback}
func flag(_ r:[String:AnkiValue],_ k:String,_ fallback:Bool=false)->Bool {if case let .bool(v)=r[k]{return v};return fallback}
func integer(_ r:[String:AnkiValue],_ k:String,_ fallback:Int=0)->Int {if case let .number(v)=r[k]{return Int(v)};return fallback}
func project(_ v:AnkiValue)->AnkiValue {
 switch v {
 case let .string(s) where s.utf8.count>2000:return .object(["bytes":.number(Double(s.utf8.count)),"sha256":.string(SpeechExportPolicy.digest(Data(s.utf8)))])
 case let .array(a):return .array(a.map(project))
 case let .object(o):return .object(o.mapValues(project))
 default:return v
 }
}
@MainActor final class VocabularyStore {var entries:[VocabularyEntry]=[]}
@MainActor protocol SpeechExporting:AnyObject {var exportConfigurationID:String {get}}
@MainActor final class SpeechStub:SpeechExporting {var exportConfigurationID="speech"}
@MainActor func change(_ v:AnkiValue?,_ s:AnkiReviewSettings,_ speech:SpeechStub)throws {
 guard let c=v?.object else{return}
 if let t=c["address"]?.string{s.address=t};if let t=c["model"]?.string{s.model=t};if let t=c["deck"]?.string{s.deck=t}
 if let t=c["key"]?.string{try s.saveKey(t)};if let t=c["speech"]?.string{speech.exportConfigurationID=t}
 if case let .bool(v)=c["recognition"]{s.defaultRecognition=v};if case let .bool(v)=c["gap"]{s.defaultGap=v}
 if let m=c["mapping"]?.object{s.mapping=Dictionary(uniqueKeysWithValues:m.map{(ReviewField(rawValue:$0.key)!,$0.value.string!)})}
}
func binding(_ r:[String:AnkiValue]=[:])->AnkiNoteBinding {
 .init(id:id(6),originSenseID:id(2),endpoint:text(r,"endpoint","http://127.0.0.1:8765"),profile:text(r,"profile","Profile"),
 noteID:flag(r,"noNote") ? nil : 10,model:"Custom",deck:"Default",identityField:"LingroveID",mapping:AnkiReviewPolicy.presetMapping,
 fields:["LingroveID":id(2).uuidString.lowercased()])
}
func entry(_ r:[String:AnkiValue])throws->VocabularyEntry {
 let mode=text(r,"review","none"),card=flag(r,"card")
 var sense=VocabularySense(id:id(2),meaning:"meaning",isSavedWithoutContext:true,createdAt:.init(timeIntervalSince1970:0),updatedAt:.init(timeIntervalSince1970:0))
 var entry=VocabularyEntry(id:id(1),expression:"bank",dictionaryForm:"bank",language:.english,kind:.word,senses:[sense],createdAt:sense.createdAt,updatedAt:sense.updatedAt)
 if mode != "none" {
  var plan=ReviewPlan(id:id(card ? 4:3));if mode=="badContent"{plan.answer=""}
  if mode=="audioStale" || mode=="audioCurrent" {plan.audioKinds=[.expression]}
  if mode=="audioCurrent" {plan.audioReceipts=[.expression:.init(fingerprint:"ignored",textHash:SpeechExportPolicy.digest(Data("bank".utf8)),endpoint:"ignored",profile:"ignored",filename:"audio.wav")]}
  var review=VocabularyReview(plan:plan)
  if mode != "unbound" {
   var fields:[String:String]=[:]
   if let content=try? ReviewCardPolicy.content(entry:entry,sense:sense,plan:plan){fields=try AnkiReviewPolicy.fields(content:content,identity:sense.id,mapping:AnkiReviewPolicy.presetMapping,overrides:[:],audio:AnkiReviewPolicy.audioFields(plan))}
   if mode=="stale"{fields["Meaning"]="remote change"}
   let b=AnkiNoteBinding(id:id(6),originSenseID:sense.id,endpoint:"http://127.0.0.1:8765",profile:"Profile",noteID:mode=="ready" ? nil:10,model:"Custom",deck:"Default",identityField:"LingroveID",mapping:mode=="badMap" ? [:]:AnkiReviewPolicy.presetMapping,fields:fields)
   review.bindings=[b];review.activeBindingID=mode=="choice" ? nil:b.id
  }
  if card{sense.reviewCards=[review]}else{sense.review=review}
 }
 entry.senses=[sense];return entry
}
actor ScriptedClient:AnkiConnecting {
 let responses:[String:[AnkiValue]],mutate:@MainActor @Sendable(AnkiValue?)throws->Void
 var counts:[String:Int]=[:],calls:[AnkiValue]=[]
 init(_ r:[String:AnkiValue],mutate:@escaping @MainActor @Sendable(AnkiValue?)throws->Void){responses=(r["responses"]?.object ?? [:]).mapValues{$0.array!};self.mutate=mutate}
 func perform(_ action:String,params:[String:AnkiValue],connection:AnkiConnection)async throws->AnkiValue {
  calls.append(.object(["action":.string(action),"params":project(.object(params)),"endpoint":.string(connection.endpoint.absoluteString),"key":.string(connection.apiKey)]))
  let n=counts[action,default:0];counts[action]=n+1
  let spec=(responses[action]?.indices.contains(n)==true ? responses[action]![n].object:nil) ?? [:]
  try await mutate(spec["change"])
  if let error=spec["error"]?.string{throw AnkiReviewError.message(error)}
  if let value=spec["value"]{return value}
  if let t=spec["template"]?.string {return t=="legacy" ? .object(AnkiNoteTemplate.legacyTemplates):t=="current" ? .object(AnkiNoteTemplate.templates):.object(["custom":.string("custom")])}
  if let t=spec["style"]?.string {return .object(["css":.string(t=="legacy" ? AnkiNoteTemplate.legacyCSS:"custom css")])}
  switch action {
  case "getActiveProfile":return .string("Profile")
  case "deckNames":return .list(["Default","Moved"])
  case "modelNames":return .list(["Custom",AnkiReviewPolicy.presetName,ReviewLayoutPolicy.model])
  case "modelFieldNames":return .list(ReviewField.allCases.map(\.rawValue))
  case "notesInfo":return .array([.object(["noteId":.number(10),"modelName":.string("Custom"),"fields":.object(["LingroveID":.object(["value":.string(id(2).uuidString.lowercased())])])])])
  case "findCards":return .array([.number(20)])
  case "cardsInfo":return .array([.object(["deckName":.string("Moved")])])
  case "guiDeckOverview":return .bool(true)
  case "modelTemplates":return .object(AnkiNoteTemplate.templates)
  case "modelStyling":return .object(["css":.string(AnkiNoteTemplate.css)])
  default:return .null
  }
 }
 func observed()->[AnkiValue]{calls}
}
func scope(_ s:ReviewOperationScope)->AnkiValue {
 .object(["endpoint":.string(s.connection.endpoint.absoluteString),"key":.string(s.connection.apiKey),"profile":.string(s.profile),"model":.string(s.model),
 "mapping":.strings(Dictionary(uniqueKeysWithValues:s.mapping.map{($0.key.rawValue,$0.value)})),"speech":s.speechID.map(AnkiValue.string) ?? .null,
 "deck":.string(s.defaultDeck),"savedDeck":.bool(s.hadSavedDeck),"recognition":.bool(s.defaultRecognition),"gap":.bool(s.defaultGap)])
}
@MainActor func snapshot(_ c:AnkiReviewController)->AnkiValue {
 .object(["catalog":c.catalog.map{.object(["profile":.string($0.profile),"decks":.list($0.decks),"models":.list($0.models)])} ?? .null,
 "message":c.connectionMessage.map(AnkiValue.string) ?? .null,"testing":.bool(c.testing),"fields":.list(c.modelFields),
 "sendingSense":c.sendingSenseID.map{.string($0.uuidString.lowercased())} ?? .null,"sendingReview":c.sendingReviewID.map{.string($0.uuidString.lowercased())} ?? .null,
 "failures":.strings(Dictionary(uniqueKeysWithValues:c.failures.map{($0.key.uuidString.lowercased(),$0.value)})),
 "missing":.list(c.missing.map{$0.uuidString.lowercased()}.sorted()),"unresolved":.list(c.unresolved.map{$0.uuidString.lowercased()}.sorted()),"updatingTemplate":.bool(c.updatingTemplate)])
}
@main struct Main {
 @MainActor static func main()async throws {
  let corpus=try JSONDecoder().decode(AnkiValue.self,from:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1])))
  var results:[AnkiValue]=[]
  for input in corpus.object!["cases"]!.array! {
   let r=input.object!,name="lingrove-anki-controller-"+UUID().uuidString,d=UserDefaults(suiteName:name)!
   d.removePersistentDomain(forName:name);defer{d.removePersistentDomain(forName:name)}
   let s=AnkiReviewSettings(defaults:d,credentials:AnkiCredentialStore(service:nil)),speech=SpeechStub(),store=VocabularyStore()
   try s.saveKey("fixture-key");try change(r["initial"],s,speech)
   let e=try entry(r);if flag(r,"loaded"){store.entries=[e]}
   let client=ScriptedClient(r){try change($0,s,speech)};var activations=0,configures=0
   let c=AnkiReviewController(settings:s,store:store,client:client,speech:flag(r,"noSpeech") ? nil:speech,activateAnki:{activations+=1})
   c.configure={configures+=1};var saved:ReviewOperationScope?,trace:[AnkiValue]=[.object(["state":snapshot(c),"outcome":.null])]
   for v in r["actions"]!.array! {
    let a=v.object!;var outcome:AnkiValue = .null
    do {
     switch text(a,"op") {
     case "test":await c.testConnection(model:a["model"]?.string)
     case "refresh":await c.refreshFields()
     case "scope":saved=try await c.operationScope();outcome=scope(saved!)
     case "matches":outcome = .bool(c.matches(saved!))
     case "verify":try await c.verify(saved!)
     case "change":try change(a["value"],s,speech)
     case "invalidate":c.invalidateConnection()
     case "profile":outcome = .string(try await c.profile(s.connection()))
     case "catalog":let v=try await c.fetchCatalog(s.connection());outcome = .object(["profile":.string(v.profile),"decks":.list(v.decks),"models":.list(v.models)])
     case "fields":outcome = .list(try await c.fields(for:text(a,"model","Custom"),connection:s.connection(),create:flag(a,"create"),expectedProfile:a["expectedProfile"]?.string))
     case "note":outcome = try await c.note(Int64(integer(a,"id",10)),connection:s.connection()).map{.object(["id":.number(Double($0.id)),"model":.string($0.model),"fields":.strings($0.fields)])} ?? .null
     case "strings":outcome = .list(try c.strings(a["value"]!))
     case "begin":try c.begin(id(integer(a,"id",2)),cardID:a["card"] == nil ? nil:id(integer(a,"card")))
     case "finish":let error:Error?=text(a,"error")=="cancelled" ? CancellationError():a["error"]?.string.map{AnkiReviewError.message($0)};c.finish(id(integer(a,"id",2)),cardID:a["card"] == nil ? nil:id(integer(a,"card")),error:error,missingNote:flag(a,"missing"),wrote:flag(a,"wrote",true))
     case "status":outcome = .object(["status":.string(c.status(e,e.senses[0],cardID:flag(r,"card") ? id(4):nil)),"synced":.bool(c.isSynchronized(e,e.senses[0],cardID:flag(r,"card") ? id(4):nil))])
     case "current":let v=try c.current(entryID:id(integer(a,"entry",1)),senseID:id(integer(a,"sense",2)));outcome = .list([v.entry.id.uuidString.lowercased(),v.sense.id.uuidString.lowercased()])
     case "open":outcome = await c.open(binding(a)).map(AnkiValue.string) ?? .null
     case "deck":outcome = await c.reviewDeck(flag(a,"binding") ? binding(a):nil).map(AnkiValue.string) ?? .null
     case "menu":await c.openReviewFromMenu()
     case "template":try await c.updatePresetTemplate()
     case "connectionEqual":outcome = .bool(AnkiConnection(endpoint:URL(string:text(a,"left"))!,apiKey:text(a,"leftKey")) == AnkiConnection(endpoint:URL(string:text(a,"right"))!,apiKey:text(a,"rightKey")))
     default:fatalError("Unknown controller action")
     }
    }catch{outcome = .object(["error":.string(error.localizedDescription)])}
    trace.append(.object(["state":snapshot(c),"outcome":outcome]))
   }
   results.append(.object(["id":r["id"]!,"expected":.object(["trace":.array(trace),"calls":.array(await client.observed()),"activations":.number(Double(activations)),"configures":.number(Double(configures))])]))
  }
  let encoder=JSONEncoder();encoder.outputFormatting=[.sortedKeys];FileHandle.standardOutput.write(try encoder.encode(AnkiValue.array(results)))
 }
}
'''

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--mac-repo',type=Path,required=True)
    p.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/anki-controller-v1.json');p.add_argument('--capture-output',type=Path)
    a=p.parse_args();c=json.loads(a.fixture.read_text());rows=c['cases'];ids=[r['id']for r in rows]
    if c.get('version')!=1 or c.get('macReference')!=BASELINE or not rows or len(ids)!=len(set(ids)):p.error('Pinned unique controller cases required')
    if hashlib.sha256('\n'.join(sorted(ids)).encode()).hexdigest()!=ID_HASH:p.error('Reviewed controller matrix changed')
    if any(r.get('macSource')not in(CONTROLLER,NAVIGATION)or r.get('provenanceKind')!='source-policy-extension'for r in rows):p.error('Original controller provenance required')
    controller=source(a.mac_repo,CONTROLLER)
    diagnostics=['import Logging\n','            CotabbyLogger.learning.info("Anki connection verified")\n','            CotabbyLogger.learning.error("Anki connection failed")\n','        CotabbyLogger.learning.info("Anki operation finished", metadata: ["success": .stringConvertible(error == nil && wrote),\n            "cancelled": .stringConvertible(cancelled)])\n']
    for diagnostic in diagnostics:
        if controller.count(diagnostic)!=1:raise RuntimeError('Pinned diagnostic boundary changed')
        controller=controller.replace(diagnostic,'')
    values=[v for v in sense_policy_sources(a.mac_repo)[:-1]if 'nonisolated enum AnkiReviewPolicy {'not in v]
    values += [source(a.mac_repo,path)for path in('Cotabby/Support/Review/ReviewCardPolicy.swift','Cotabby/Support/Review/ReviewLayoutPolicy.swift','Cotabby/Support/Review/AnkiNoteTemplate.swift','Cotabby/Support/Review/AnkiReviewPolicy.swift','Cotabby/Models/Review/AnkiWireModels.swift','Cotabby/Models/Review/ReviewOperationScope.swift','Cotabby/Models/Review/AnkiReviewSettings.swift','Cotabby/Services/Review/AnkiConnectClient.swift')]
    units=source(a.mac_repo,'Cotabby/Models/LanguageLearning/LearningUnitModels.swift');words=source(a.mac_repo,'Cotabby/Models/LanguageLearning/LearningDictionaryWord.swift');speech=source(a.mac_repo,'Cotabby/Support/Speech/SpeechTextPolicy.swift');export=source(a.mac_repo,'Cotabby/Support/Speech/SpeechExportPolicy.swift')
    errors=source(a.mac_repo,'Cotabby/Models/Speech/LearningSpeechModels.swift')
    values += [source(a.mac_repo,'Cotabby/Services/LanguageLearning/LearningEndpointSession.swift'), 'import Foundation\n'+between(errors,'nonisolated enum SpeechError:','/// The system catalog')]
    values += ['import Foundation\n'+between(units,'nonisolated struct LearningUnit:','/// Recognition and lookup share a resource,')+between(words,'nonisolated struct LearningDictionaryWord:','/// View-local geometry exists'),
        'import Foundation\n'+between(speech,'nonisolated enum SpeechTextPolicy {',"/// Qwen's fixed language inventory"),
        'import CryptoKit\nimport Foundation\nnonisolated enum SpeechExportPolicy {\n'+between(export,'    static func digest(','    static func filename(')+'}\n',controller,source(a.mac_repo,NAVIGATION),DRIVER]
    with tempfile.TemporaryDirectory(prefix='lingrove-anki-controller-')as temp:
        root=Path(temp);files=[]
        for i,v in enumerate(values):
            f=root/f'source{i}.swift';f.write_text(v);files.append(str(f))
        binary=root/'anki-controller';subprocess.run(['swiftc','-swift-version','6','-O',*files,'-o',str(binary)],check=True)
        actual=json.loads(subprocess.check_output([str(binary),str(a.fixture)],timeout=120))
    if a.capture_output:
        with a.capture_output.open('x',encoding='utf-8')as out:json.dump(actual,out,ensure_ascii=False,indent=2);out.write('\n')
        print(f'Captured {len(actual)} original review controller/navigation traces');return
    if len(actual)!=len(rows):raise SystemExit('Controller count changed')
    for r,v in zip(rows,actual):
        if v!={'id':r['id'],'expected':r['expected']}:raise SystemExit('Mac controller mismatch: '+r['id'])
    print(f'Verified {len(rows)} original review controller/navigation traces')
if __name__=='__main__':main()
