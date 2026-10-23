#!/usr/bin/env python3
"""Run the complete pinned Mac restore, SQLite, PNG validation and real file effects."""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, between, database_archive_sources

_spec = importlib.util.spec_from_file_location('backup_archive', Path(__file__).with_name('verify-vocabulary-backup-archive-macos-parity.py'))
_backup = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(_backup)
CASES = ('empty','plain','sense','shared','legacy','preserved','card','template','all','replace','repair',
    'missing-media','extra-media','traversal-key','corrupt-bytes','wrong-digest','wrong-dimensions','wrong-byte-count',
    'unsupported-version','invalid-schema','invalid-graph','malformed-json','directory','oversized','symbolic-link','hard-link','missing-source')
DRIVER = 'import Foundation\nimport CryptoKit\n' + between(_backup.DRIVER, 'func id(_ n:', 'func observe(') + r'''
func object<T: Encodable>(_ value: T) throws -> Any { try JSONSerialization.jsonObject(with:JSONEncoder().encode(value)) }
func observeRestore(_ mode: String, _ data: Data) async throws -> [String:Any] {
    let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString,isDirectory:true)
    try FileManager.default.createDirectory(at:root,withIntermediateDirectories:true,attributes:[.posixPermissions:0o700])
    defer { try? FileManager.default.removeItem(at:root) }
    let notebook = root.appendingPathComponent("notebook.sqlite"), source = root.appendingPathComponent("source.lingrove")
    let mediaFile = URL(fileURLWithPath:notebook.path + ".media").appendingPathComponent("lg-image-" + VocabularyImageStorage.digest(data) + ".png")
    let archive = VocabularyArchive(url:notebook)
    let image = VocabularyImageReference(digest:VocabularyImageStorage.digest(data),width:80,height:40,byteCount:data.count)
    let before = graph(["replace","repair"].contains(mode) ? "all" : "plain",image)
    try await archive.seedBackupProbe(before,image:image,data:data,writeMedia:!VocabularyArchive.imageReferences(before).isEmpty)
    let revision = try await archive.load().revision
    if mode == "repair" { try Data([0,1,2]).write(to:mediaFile) }
    var candidate = graph(mode,image)
    if ["empty","replace"].contains(mode) { candidate = .init() }
    if mode == "repair" { candidate = graph("all",image) }
    if ["missing-media","extra-media","traversal-key","corrupt-bytes","wrong-digest","wrong-dimensions","wrong-byte-count"].contains(mode) {
        var ref = image
        if mode == "wrong-digest" { ref = .init(digest:String(repeating:"b",count:64),width:80,height:40,byteCount:data.count) }
        if mode == "wrong-dimensions" { ref = .init(digest:image.digest,width:81,height:40,byteCount:data.count) }
        if mode == "wrong-byte-count" { ref = .init(digest:image.digest,width:80,height:40,byteCount:data.count + 1) }
        candidate = graph("sense",ref)
    }
    if mode == "invalid-schema" || mode == "unsupported-version" { candidate.schemaVersion = 99 }
    if mode == "invalid-graph" { candidate.entries.append(candidate.entries[0]) }
    var images = Dictionary(VocabularyArchive.imageReferences(candidate).map{($0.filename,data)},uniquingKeysWith: { a,_ in a })
    if mode == "missing-media" { images = [:] }
    if mode == "extra-media" { images["extra.png"] = data }
    if mode == "traversal-key" { images = ["../outside.png":data] }
    if mode == "corrupt-bytes" { images[image.filename] = Data([0,1,2]) }
    var backup = VocabularyBackup(document:candidate,images:images)
    if mode == "unsupported-version" { backup.version = 2 }
    var package = try JSONSerialization.data(withJSONObject:object(backup),options:[.sortedKeys])
    if mode == "malformed-json" { package = Data("{not JSON".utf8) }
    var chosen = source
    if mode == "directory" { try FileManager.default.createDirectory(at:source,withIntermediateDirectories:true) }
    else if mode == "oversized" {
        FileManager.default.createFile(atPath:source.path,contents:nil)
        let h = try FileHandle(forWritingTo:source); try h.truncate(atOffset:UInt64(VocabularyArchive.maximumBackupBytes + 1));try h.close()
    } else if mode != "missing-source" {
        try package.write(to:source)
        if ["symbolic-link","hard-link"].contains(mode) {
            chosen = root.appendingPathComponent("alias.lingrove")
            if mode == "symbolic-link" { try FileManager.default.createSymbolicLink(at:chosen,withDestinationURL:source) }
            else { try FileManager.default.linkItem(at:source,to:chosen) }
        }
    }
    var failureCode: String?
    do { _ = try await archive.restore(from:chosen) }
    catch let error as VocabularyError { failureCode = String(describing:error) }
    catch is DecodingError { failureCode = "decode" }
    catch { failureCode = "io" }
    let after = try await archive.load(), reopened = try await VocabularyArchive(url:notebook).load()
    let readable = (try? await archive.image(image)) == data
    let stages = (FileManager.default.enumerator(atPath:root.path)?.allObjects as? [String] ?? []).filter{URL(fileURLWithPath:$0).lastPathComponent.hasPrefix(".notebook-")}.count
    return ["package":package.base64EncodedString(),"before":try object(before),"error":failureCode as Any? ?? NSNull(),
        "document":try object(after.document),"reopened":try object(reopened.document),"revisionDelta":after.revision - revision,
        "imageReadable":readable,"mediaExists":FileManager.default.fileExists(atPath:mediaFile.path),"stages":stages]
}
@main struct Main {
    static func main() async throws {
        let corpus = try JSONSerialization.jsonObject(with:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1]))) as! [String:Any]
        let data = Data(base64Encoded:corpus["input"] as! String)!; var result = [[String:Any]]()
        for row in corpus["cases"] as! [[String:Any]] { let mode = row["id"] as! String;result.append(["id":mode,"expected":try await observeRestore(mode,data)]) }
        FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject:result,options:[.sortedKeys]))
    }
}
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, default=Path(__file__).resolve().parents[1] / 'fixtures/parity/vocabulary-restore-v1.json')
    parser.add_argument('--capture-output', type=Path, help='Write actual observations to a NEW file')
    args = parser.parse_args(); corpus = json.loads(args.fixture.read_text(encoding='utf-8')); rows = corpus['cases']
    if corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or [r.get('id') for r in rows] != list(CASES): parser.error('All 27 pinned restore cases required')
    if any(r.get('macSource') != 'Cotabby/Services/Vocabulary/VocabularyArchive+Media.swift' or r.get('provenanceKind') != 'source-policy-extension' for r in rows): parser.error('Complete original restore provenance required')
    if not args.capture_output and any('expected' not in row for row in rows): parser.error('Actual source observations required')
    with tempfile.TemporaryDirectory(prefix='lingrove-restore-') as temporary:
        root = Path(temporary); inputs = []
        for i, source in enumerate(database_archive_sources(args.mac_repo)):
            path = root / f'source{i}.swift'; path.write_text(source,encoding='utf-8');inputs.append(str(path))
        driver = root / 'main.swift';driver.write_text(DRIVER,encoding='utf-8');binary = root / 'restore-parity'
        subprocess.run(['swiftc','-swift-version','6','-O','-parse-as-library','-lsqlite3','-lz',*inputs,str(driver),'-o',str(binary)],check=True)
        actual = json.loads(subprocess.check_output([str(binary),str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x',encoding='utf-8') as output: json.dump(actual,output,ensure_ascii=False,indent=2);output.write('\n')
        print(f'Captured {len(actual)} actual Mac restore observations');return
    if actual != [{'id':r['id'],'expected':r['expected']} for r in rows]:
        bad = next((r['id'] for r,a in zip(rows,actual) if a != {'id':r['id'],'expected':r['expected']}),'count')
        raise SystemExit(f'Actual restore mismatch: {bad}')
    print(f'Verified {len(actual)} restore effects against the complete original Mac archive')


if __name__ == '__main__': main()
