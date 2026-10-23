#!/usr/bin/env python3
"""Run the complete original Mac archive export snapshot with real SQLite/ImageIO memory owners."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, archive_export_sources

DRIVER = r'''
import Foundation
import CryptoKit
struct Row: Decodable { let id: String; let mode: String; let format: String; let images: Bool; let content: Bool? }
struct Corpus: Decodable { let input: String; let cases: [Row] }
func id(_ n: Int) -> UUID { UUID(uuidString: String(format:"00000000-0000-0000-0000-%012x", n))! }
let now = Date(timeIntervalSince1970: 0)
func block(_ n: Int, _ image: VocabularyImageReference) -> VocabularyContentBlock { .init(id:id(n),content:.image(image,caption:"river")) }
func seed() -> VocabularyDocument {
    let usage = VocabularyExample(id:id(3),text:"The bank.",translation:"原句",ranges:[.init(NSRange(location:4,length:4))],completeness:.sentence,meaning:"context",createdAt:now)
    let sense = VocabularySense(id:id(2),meaning:"core",note:"note",tags:["Work"],examples:[usage],mergedFrom:[id(90)],createdAt:now,updatedAt:now)
    return .init(entries:[.init(id:id(1),expression:"bank",dictionaryForm:"bank",language:.english,kind:.word,
        senses:[sense,.init(id:id(4),meaning:"second",createdAt:now,updatedAt:now)],createdAt:now,updatedAt:now)])
}
func available(_ archive: VocabularyArchive, _ image: VocabularyImageReference) async -> Bool {
    do { _ = try await archive.image(image); return true } catch { return false }
}
func facts(_ p: VocabularyExportPackage) -> [String: Any] {
    ["sha256":SHA256.hash(data:p.data).map{String(format:"%02x",$0)}.joined(),"byteCount":p.data.count,"filename":p.filename,
        "entryCount":p.entryCount,"senseCount":p.senseCount,"exampleCount":p.exampleCount,
        "imageNames":p.images.keys.sorted(),"imageHashes":p.images.mapValues{SHA256.hash(data:$0).map{String(format:"%02x",$0)}.joined()}]
}
func observe(_ row: Row, _ data: Data) async throws -> [String: Any] {
    let archive = VocabularyArchive(url:nil), image = VocabularyImageReference(digest:VocabularyImageStorage.digest(data),width:80,height:40,byteCount:data.count)
    try VocabularyImageStorage.validate(data,reference:image)
    var initial = seed()
    if row.mode.hasPrefix("budget") {
        let count = row.mode == "budgetExact" ? 32 : row.mode == "budgetPlusOne" ? 33 : 36
        initial.entries[0].senses = (0..<((count+3)/4)).map { owner in
            var s = VocabularySense(id:id(100+owner),meaning:"",createdAt:now,updatedAt:now)
            s.content = (0..<min(4,count-owner*4)).map { part in
                let index = owner*4+part
                return block(200+index,.init(digest:String(format:"%064x",index),width:1,height:1,
                    byteCount:row.mode == "budgetPlusOne" && index == 32 ? 1 : VocabularyContentPolicy.maximumImageBytes))
            }; return s
        }
    }
    _ = try await archive.commit(initial)
    var edited = initial.entries[0]
    if !row.mode.hasPrefix("budget") {
        if row.mode == "otherSense" { edited.senses[1].content = [block(21,image)] }
        else { edited.senses[0].content = [block(20,image)] }
        if row.mode == "shared" { edited.senses[0].content!.append(block(22,image)); edited.senses[1].content = [block(23,image)] }
        if ["missing","metadataOnly","otherSense"].contains(row.mode) {
            var d = initial; d.entries[0] = edited; _ = try await archive.commit(d)
        } else {
            _ = try await archive.update(edited,basedOn:initial.entries[0],importedImages:[image.filename:data])
        }
        if row.mode == "mismatch" {
            var d = try await archive.load().document
            d.entries[0].senses[0].content = [block(20,.init(digest:image.digest,width:81,height:40,byteCount:image.byteCount))]
            _ = try await archive.commit(d)
        }
    }
    var c = VocabularyExportConfiguration(); c.format = .init(rawValue:row.format)!; c.images = row.images; c.content = row.content ?? true
    var selections = [VocabularySelection(entryID:id(1),senseID:id(2))]
    switch row.mode {
    case "empty","cancelledEmpty": selections = []
    case "staleEntry": selections = [.init(entryID:id(99),senseID:id(2))]
    case "staleSense": selections = [.init(entryID:id(1),senseID:id(99))]
    case "alias": selections = [.init(entryID:id(1),senseID:id(90))]
    case "duplicate": selections.append(selections[0])
    case "shared","ordered": selections = [.init(entryID:id(1),senseID:id(4)),.init(entryID:id(1),senseID:id(2))]
    case "budget","budgetExact","budgetPlusOne": selections = initial.entries[0].senses.map{.init(entryID:id(1),senseID:$0.id)}
    default: break
    }
    let before = try await archive.load().revision
    do {
        let p: VocabularyExportPackage
        if row.mode.hasPrefix("cancelled") {
            let captured = selections, configuration = c
            // No archive call may start until the parent has set cancellation. Stream termination
            // also wakes a child that has not begun iterating yet, without depending on a timer.
            let (start, continuation) = AsyncStream<Void>.makeStream()
            let task = Task {
                for await _ in start { break }
                return try await archive.prepareExport(selections:captured,configuration:configuration)
            }
            task.cancel(); continuation.finish(); p = try await task.value
        } else { p = try await archive.prepareExport(selections:selections,configuration:c) }
        let after = try await archive.load().revision
        if row.mode == "removeAfter" {
            _ = try await archive.remove(id:id(1))
            return ["package":facts(p),"revisionUnchanged":before == after,"sourceAvailable":await available(archive,image),"entriesAfter":try await archive.load().document.entries.count]
        }
        return ["package":facts(p),"revisionUnchanged":before == after]
    } catch let error as VocabularyExportError { return ["error":String(describing:error),"revisionUnchanged":try await archive.load().revision == before] }
    catch is CancellationError { return ["error":"cancelled","revisionUnchanged":try await archive.load().revision == before] }
}
@main struct Main {
    static func main() async throws {
        let corpus = try JSONDecoder().decode(Corpus.self,from:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1])))
        let data = Data(base64Encoded:corpus.input)!; var results = [[String:Any]]()
        for row in corpus.cases { results.append(["id":row.id,"expected":try await observe(row,data)]) }
        FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject:results,options:[.sortedKeys]))
    }
}
'''


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo',type=Path,required=True)
    parser.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/vocabulary-export-snapshot-v1.json')
    parser.add_argument('--capture-output',type=Path,help='Write actual observations into a NEW file')
    args=parser.parse_args();corpus=json.loads(args.fixture.read_text(encoding='utf-8'));rows=corpus['cases'];ids=[r.get('id') for r in rows]
    if corpus.get('version')!=1 or corpus.get('macReference')!=BASELINE or not rows or any(not v for v in ids) or len(set(ids))!=len(ids):parser.error('Snapshot probes require pinned version and unique IDs')
    if any(r.get('macSource')!='Cotabby/Services/Vocabulary/VocabularyArchive+Export.swift' or r.get('provenanceKind')!='source-policy-extension' for r in rows):parser.error('Snapshot probes require exact archive source provenance')
    if not args.capture_output and any('expected' not in r for r in rows):parser.error('Every snapshot probe needs captured expected values')
    with tempfile.TemporaryDirectory(prefix='lingrove-export-snapshot-') as temporary:
        root=Path(temporary);inputs=[]
        for index,value in enumerate(archive_export_sources(args.mac_repo)):
            path=root/f'source{index}.swift';path.write_text(value,encoding='utf-8');inputs.append(str(path))
        driver=root/'main.swift';driver.write_text(DRIVER,encoding='utf-8');binary=root/'snapshot-parity'
        subprocess.run(['swiftc','-swift-version','6','-O','-parse-as-library','-lsqlite3','-lz',*inputs,str(driver),'-o',str(binary)],check=True)
        observations=json.loads(subprocess.check_output([str(binary),str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x',encoding='utf-8') as output:json.dump(observations,output,ensure_ascii=False,indent=2);output.write('\n')
        print(f'Captured {len(observations)} actual Mac archive export snapshot observations');return
    if observations!=[{'id':r['id'],'expected':r['expected']} for r in rows]:
        bad=next((r['id'] for r,o in zip(rows,observations) if o!={'id':r['id'],'expected':r['expected']}),'count')
        raise SystemExit(f'Mac export snapshot mismatch: {bad}')
    print(f'Verified {len(rows)} snapshot/media cases against the complete original Mac archive')


if __name__=='__main__':main()
