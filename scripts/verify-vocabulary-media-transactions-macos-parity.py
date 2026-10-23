#!/usr/bin/env python3
"""Run media admission and SQL acknowledgement through the complete pinned Mac archive actor."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, database_archive_sources

MEDIA = 'Cotabby/Services/Vocabulary/VocabularyArchive+Media.swift'
OPERATIONS = ['draft-only', 'import', 'missing', 'extra', 'remove', 'shared', 'conflict',
              'sql-rollback', 'existing-missing', 'prune', 'budget', 'filename-retention']
DRIVER = r'''
import Foundation
struct Row:Decodable { let id:String;let operation:String }
struct Corpus:Decodable { let input:String;let cases:[Row] }
extension VocabularyArchive {
 func mediaSqlProbe(_ sql:String) throws { try persistence().connection.execute(sql) }
}
func available(_ archive:VocabularyArchive,_ image:VocabularyImageReference) async -> Bool {
 do { _=try await archive.image(image);return true } catch { return false }
}
func block(_ image:VocabularyImageReference) -> VocabularyContentBlock { .init(content:.image(image,caption:"river")) }
func capture() -> VocabularyCapture { .init(expression:"bank",dictionaryForm:"bank",language:.english,kind:.word,meaning:"core") }
func observe(_ mode:String,_ data:Data) async throws -> [String:Any] {
 let archive=VocabularyArchive(url:nil)
 let image=VocabularyImageReference(digest:VocabularyImageStorage.digest(data),width:80,height:40,byteCount:data.count)
 try VocabularyImageStorage.validate(data,reference:image)
 if mode == "draft-only" {
  let prepared=try await archive.prepareImage(data)
  return ["width":prepared.reference.width,"height":prepared.reference.height,"available":await available(archive,prepared.reference),"revision":try await archive.load().revision]
 }
 let original=try await archive.save(capture()).document.entries[0]
 var edited=original;edited.senses[0].content=[block(image)]
 if mode == "missing" {
  do { _=try await archive.update(edited,basedOn:original);fatalError("Missing media admitted") }
  catch let error as VocabularyError { return ["error":String(describing:error),"revision":try await archive.load().revision,"available":await available(archive,image)] }
 }
 if mode == "extra" {
  var text=original;text.senses[0].note="edited"
  let result=try await archive.update(text,basedOn:original,importedImages:[image.filename:data])
  return ["revision":result.revision,"note":result.document.entries[0].senses[0].note,"available":await available(archive,image)]
 }
 if mode == "conflict" {
  var first=original;first.senses[0].content=[.init(content:.text(.init(text:"winner")))]
  _=try await archive.update(first,basedOn:original)
  do { _=try await archive.update(edited,basedOn:original,importedImages:[image.filename:data]);fatalError("Conflict admitted") }
  catch let error as VocabularyError { return ["error":String(describing:error),"revision":try await archive.load().revision,"available":await available(archive,image),"preserved":try await archive.load().document.entries[0].senses[0].content == first.senses[0].content] }
 }
 if mode == "sql-rollback" {
  try await archive.mediaSqlProbe("CREATE TRIGGER fail_sense BEFORE UPDATE ON senses BEGIN SELECT RAISE(ABORT,'fixture'); END")
  do { _=try await archive.update(edited,basedOn:original,importedImages:[image.filename:data]);fatalError("Trigger ignored") }
  catch let error as VocabularyError { return ["error":String(describing:error),"revision":try await archive.load().revision,"available":await available(archive,image),"preserved":try await archive.load().document.entries[0] == original] }
 }
 if mode == "existing-missing" {
  var seeded=try await archive.load().document;seeded.entries[0]=edited;_=try await archive.commit(seeded)
  var note=edited;note.senses[0].note="metadata"
  let result=try await archive.update(note,basedOn:edited)
  return ["revision":result.revision,"note":result.document.entries[0].senses[0].note,"available":await available(archive,image)]
 }
 if mode == "prune" {
  let input=LearningExplanationPolicy.input(LearningExplanationPolicy.capture(original,sense:original.senses[0],example:nil))
  let record=LearningExplanationRecord(id:UUID(),input:input,language:.english,result:.init(meaning:"generated",explanation:"detail"),model:"fixture",createdAt:Date())
  let baseline=try await archive.saveExplanation(record).document.entries[0]
  var changed=baseline;changed.expression="riverbank";changed.dictionaryForm="riverbank";changed.senses[0].content=[block(image)]
  let result=try await archive.update(changed,basedOn:baseline,importedImages:[image.filename:data])
  return ["revision":result.revision,"before":baseline.senses[0].explanations.count,"after":result.document.entries[0].senses[0].explanations.count,"available":await available(archive,image)]
 }
 if mode == "budget" {
  var value=original;value.senses=[]
  for owner in 0..<9 {
   var sense=owner == 0 ? original.senses[0] : VocabularySense(id:UUID(),meaning:"",createdAt:Date(),updatedAt:Date())
   sense.content=(0..<4).map { part in block(.init(digest:String(format:"%064x",owner*4+part),width:1,height:1,byteCount:VocabularyContentPolicy.maximumImageBytes)) }
   value.senses.append(sense)
  }
  do { _=try await archive.update(value,basedOn:original);fatalError("Media budget ignored") }
  catch let error as VocabularyError { return ["error":String(describing:error),"revision":try await archive.load().revision,"senses":try await archive.load().document.entries[0].senses.count] }
 }
 if mode == "shared" {
  let second=UUID();let baseline=try await archive.createSense(entryID:original.id,senseID:second).document.entries[0]
  var both=baseline;both.senses[0].content=[block(image)];both.senses[1].content=[block(image)]
  let before=try await archive.update(both,basedOn:baseline,importedImages:[image.filename:data])
  let ids=Set(before.document.entries[0].senses.flatMap{$0.content ?? []}.map(\.id))
  let merged=try await archive.mergeSenses(entryID:original.id,source:second,target:original.senses[0].id)
  let retained=await available(archive,image),same=Set((merged.document.entries[0].senses[0].content ?? []).map(\.id)) == ids
  let removed=try await archive.removeSense(entryID:original.id,senseID:original.senses[0].id)
  return ["revision":removed.revision,"blocksRetained":same,"sharedAvailable":retained,"available":await available(archive,image),"entries":removed.document.entries.count]
 }
 let imported=try await archive.update(edited,basedOn:original,importedImages:[image.filename:data])
 if mode == "import" {
  return ["revision":imported.revision,"content":imported.document.entries[0].senses[0].content == edited.senses[0].content,"exact":try await archive.image(image) == data]
 }
 if mode == "filename-retention" {
  let mismatch=VocabularyImageReference(digest:image.digest,width:81,height:40,byteCount:image.byteCount)
  var seeded=imported.document;seeded.entries[0].senses[0].content=[block(image),block(mismatch)]
  let before=try await archive.commit(seeded).document.entries[0]
  var changed=before;changed.senses[0].content=[before.senses[0].content![1]]
  let result=try await archive.update(changed,basedOn:before)
  return ["revision":result.revision,"validAvailable":await available(archive,image),"mismatchAvailable":await available(archive,mismatch)]
 }
 if mode == "remove" {
  let before=imported.document.entries[0];var changed=before;changed.senses[0].content=[]
  let result=try await archive.update(changed,basedOn:before)
  return ["revision":result.revision,"available":await available(archive,image),"blocks":result.document.entries[0].senses[0].content!.count]
 }
 fatalError("Unknown media transaction")
}
@main struct Main {
 static func main() async throws {
  let corpus=try JSONDecoder().decode(Corpus.self,from:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1])))
  let data=Data(base64Encoded:corpus.input)!;var output:[[String:Any]]=[]
  for row in corpus.cases { output.append(["id":row.id,"expected":try await observe(row.operation,data)]) }
  FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject:output,options:[.sortedKeys]))
 }
}
'''

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo',type=Path,required=True)
    parser.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/vocabulary-media-transactions-v1.json')
    parser.add_argument('--capture-output',type=Path)
    args=parser.parse_args();corpus=json.loads(args.fixture.read_text());rows=corpus['cases']
    if corpus.get('version')!=1 or corpus.get('macReference')!=BASELINE or [r.get('id') for r in rows]!=OPERATIONS or [r.get('operation') for r in rows]!=OPERATIONS:
        parser.error('All twelve media transaction probes are required')
    if any(r.get('macSource')!=MEDIA or r.get('provenanceKind')!='source-policy-extension' for r in rows):parser.error('Original media transaction provenance is required')
    with tempfile.TemporaryDirectory(prefix='lingrove-media-transactions-') as temporary:
        root=Path(temporary);inputs=[]
        for index,value in enumerate(database_archive_sources(args.mac_repo)):
            path=root/f'source{index}.swift';path.write_text(value);inputs.append(str(path))
        driver=root/'main.swift';driver.write_text(DRIVER);binary=root/'probe'
        subprocess.run(['swiftc','-swift-version','6','-O','-parse-as-library','-lsqlite3',*inputs,str(driver),'-o',str(binary)],check=True)
        observed=json.loads(subprocess.check_output([str(binary),str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x') as target:json.dump(observed,target,ensure_ascii=False,indent=2);target.write('\n')
        print(f'Captured {len(observed)} original media transaction observations');return
    if observed!=[{'id':r['id'],'expected':r['expected']} for r in rows]:raise SystemExit('Original media transaction mismatch')
    print(f'Verified {len(observed)} cases against the complete original Mac media/SQLite archive actor')

if __name__=='__main__':main()
