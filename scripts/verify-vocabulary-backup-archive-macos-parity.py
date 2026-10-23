#!/usr/bin/env python3
"""Run complete original Mac backup, SQLite, media and atomic file effects in private temp folders."""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, between, database_archive_sources

_spec = importlib.util.spec_from_file_location('backup_seed', Path(__file__).with_name('verify-vocabulary-export-snapshot-macos-parity.py'))
_seed = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(_seed)
CASES = ('plain','sense','shared','legacy','preserved','card','template','all','missing','mismatch','unreferenced','replace','media-sibling',
    'protected-database','protected-wal','protected-shm','protected-journal','protected-lock','hard-link','symbolic-link','media-root','media-child')
DRIVER = 'import Foundation\nimport CryptoKit\n' + between(_seed.DRIVER, 'func id(_ n:', 'func available(') + r'''
func layout(_ part: Int, _ blockID: Int, _ image: VocabularyImageReference) -> ReviewLayout {
    .init(front:[.init(id:id(part),source:.custom,content:[block(blockID,image)])])
}
func graph(_ mode: String, _ image: VocabularyImageReference) -> VocabularyDocument {
    var d = seed()
    if ["sense","shared","all","missing","mismatch","replace"].contains(mode) { d.entries[0].senses[0].content = [block(20,image)] }
    if ["shared","all"].contains(mode) { d.entries[0].senses[1].content = [block(21,image)] }
    if ["legacy","preserved","all"].contains(mode) {
        let binding = AnkiNoteBinding(id:id(40),originSenseID:id(2),endpoint:"http://localhost",profile:"fixture",noteID:7,
            model:"Basic",deck:"Default",identityField:"ID",mapping:[.identity:"ID"],fields:["ID":id(2).uuidString.lowercased()])
        d.entries[0].senses[0].review = .init(plan:.init(id:id(30),layout:mode == "preserved" ? nil : layout(50,22,image)),
            bindings:[binding],activeBindingID:binding.id,preservedPlans:mode == "legacy" ? [] : [.init(id:id(31),layout:layout(51,23,image))])
    }
    if ["card","all"].contains(mode) { d.entries[0].senses[0].reviewCards = [.init(plan:.init(id:id(32),layout:layout(52,24,image)))] }
    if ["template","all"].contains(mode) { d.reviewTemplates = [.init(id:id(70),name:"Template",layout:layout(53,25,image))] }
    return d
}
extension VocabularyArchive {
    // Fixture setup only: real media owner writes validated bytes, then real commit seeds SQL.
    // The exercised backup implementation and every IO/codec body remain the originals.
    func seedBackupProbe(_ d: VocabularyDocument, image: VocabularyImageReference, data: Data, writeMedia: Bool) throws {
        if writeMedia { _ = try images.write(data,image:image) }
        _ = try commit(d)
    }
}
func observe(_ row: [String:Any], _ data: Data) async throws -> [String:Any] {
    let mode = row["id"] as! String
    let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString,isDirectory:true)
    try FileManager.default.createDirectory(at:root,withIntermediateDirectories:true,attributes:[.posixPermissions:0o700])
    defer { try? FileManager.default.removeItem(at:root) }
    let notebook = root.appendingPathComponent("notebook.sqlite"), media = URL(fileURLWithPath:notebook.path + ".media",isDirectory:true)
    let archive = VocabularyArchive(url:notebook)
    let image = VocabularyImageReference(digest:VocabularyImageStorage.digest(data),width:80,height:40,byteCount:data.count)
    try VocabularyImageStorage.validate(data,reference:image)
    var destination = root.appendingPathComponent("备份.lingrove"), before: UInt64? = nil
    let old = Data([9,8,7]), protectedCase = mode.hasPrefix("protected-") || ["hard-link","symbolic-link","media-root","media-child"].contains(mode)
    if mode.hasPrefix("protected-") {
        destination = URL(fileURLWithPath:notebook.path + (row["suffix"] as! String));try old.write(to:destination)
    } else if ["hard-link","symbolic-link"].contains(mode) {
        try old.write(to:notebook)
        if mode == "hard-link" { try FileManager.default.linkItem(at:notebook,to:destination) }
        else { try FileManager.default.createSymbolicLink(at:destination,withDestinationURL:notebook) }
    } else if ["media-root","media-child"].contains(mode) {
        try FileManager.default.createDirectory(at:media,withIntermediateDirectories:true,attributes:[.posixPermissions:0o700])
        let child = media.appendingPathComponent("child");try old.write(to:child);destination = mode == "media-root" ? media : child
    } else {
        var d = graph(mode,image)
        if mode == "mismatch" { d.entries[0].senses[0].content = [block(20,.init(digest:image.digest,width:81,height:40,byteCount:data.count))] }
        try await archive.seedBackupProbe(d,image:image,data:data,writeMedia:mode != "missing" && (!VocabularyArchive.imageReferences(d).isEmpty || mode == "unreferenced"))
        before = try await archive.load().revision
        if mode == "replace" { try old.write(to:destination) }
        if mode == "media-sibling" {
            let sibling = URL(fileURLWithPath:media.path + "-keep",isDirectory:true)
            try FileManager.default.createDirectory(at:sibling,withIntermediateDirectories:true,attributes:[.posixPermissions:0o700]);destination = sibling.appendingPathComponent("backup.lingrove")
        }
    }
    var failureCode: String?
    do { try await archive.backup(to:destination) }
    catch let failure as VocabularyError { failureCode = String(describing:failure) }
    catch { failureCode = "io" } // Actual Foundation file-read failure; no path/error text is retained.
    let output = try? Data(contentsOf:destination)
    let json = output.flatMap{try? JSONSerialization.jsonObject(with:$0)}
    let unchanged: Bool = if let before { try await archive.load().revision == before } else { true }
    let protectedUnchanged = !protectedCase || (output == old || (mode == "media-root" && (try? Data(contentsOf:media.appendingPathComponent("child"))) == old))
    let stages = (FileManager.default.enumerator(atPath:root.path)?.allObjects as? [String] ?? []).filter{URL(fileURLWithPath:$0).lastPathComponent.hasPrefix(".notebook-")}.count
    return ["error":failureCode as Any? ?? NSNull(),"backup":json as Any? ?? NSNull(),"revisionUnchanged":unchanged,
        "protectedUnchanged":protectedUnchanged,"notebookExists":FileManager.default.fileExists(atPath:notebook.path),"stages":stages]
}
@main struct Main {
    static func main() async throws {
        let corpus = try JSONSerialization.jsonObject(with:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1]))) as! [String:Any]
        let input = Data(base64Encoded:corpus["input"] as! String)!;var observations = [[String:Any]]()
        for row in corpus["cases"] as! [[String:Any]] { observations.append(["id":row["id"]!,"expected":try await observe(row,input)]) }
        FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject:observations,options:[.sortedKeys]))
    }
}
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, default=Path(__file__).resolve().parents[1] / 'fixtures/parity/vocabulary-backup-archive-v1.json')
    parser.add_argument('--capture-output', type=Path, help='Write actual effects observations into a NEW file')
    args = parser.parse_args(); corpus = json.loads(args.fixture.read_text(encoding='utf-8')); rows = corpus['cases']
    if corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or [r.get('id') for r in rows] != list(CASES): parser.error('All 22 pinned archive backup cases are required')
    if any(r.get('macSource') != 'Cotabby/Services/Vocabulary/VocabularyArchive+Media.swift' or r.get('provenanceKind') != 'source-policy-extension' for r in rows): parser.error('Exact complete archive backup provenance required')
    if not args.capture_output and any('expected' not in r for r in rows): parser.error('Actual source observations required')
    with tempfile.TemporaryDirectory(prefix='lingrove-backup-archive-') as temporary:
        root = Path(temporary); inputs = []
        for index, value in enumerate(database_archive_sources(args.mac_repo)):
            path = root / f'source{index}.swift'; path.write_text(value, encoding='utf-8'); inputs.append(str(path))
        driver = root / 'main.swift'; driver.write_text(DRIVER, encoding='utf-8'); binary = root / 'backup-archive-parity'
        subprocess.run(['swiftc','-swift-version','6','-O','-parse-as-library','-lsqlite3','-lz',*inputs,str(driver),'-o',str(binary)],check=True)
        actual = json.loads(subprocess.check_output([str(binary),str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x',encoding='utf-8') as output: json.dump(actual,output,ensure_ascii=False,indent=2);output.write('\n')
        print(f'Captured {len(actual)} actual Mac full backup observations');return
    if actual != [{'id':r['id'],'expected':r['expected']} for r in rows]:
        bad = next((r['id'] for r,o in zip(rows,actual) if o != {'id':r['id'],'expected':r['expected']}),'count')
        raise SystemExit(f'Archive backup mismatch: {bad}')
    print(f'Verified {len(actual)} full backup effects against the complete original Mac archive')


if __name__ == '__main__': main()
