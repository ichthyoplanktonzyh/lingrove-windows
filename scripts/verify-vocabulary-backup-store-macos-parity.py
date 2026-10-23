#!/usr/bin/env python3
"""Run original Mac Store backup/restore publication with the complete real archive and media."""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, between, source, vocabulary_store_sources

_spec = importlib.util.spec_from_file_location('backup_seed', Path(__file__).with_name('verify-vocabulary-backup-archive-macos-parity.py'))
_seed = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(_seed)
CASES = ('fresh-backup','backup','backup-error','backup-preserves-error','backup-failure-then-success',
    'restore','restore-all','restore-error','restore-after-error','restore-repair','restore-empty','restore-twice','backup-after-restore')
DRIVER = 'import Foundation\nimport CryptoKit\n' + between(_seed.DRIVER,'func id(_ n:','func observe(') + r'''
func object<T:Encodable>(_ value:T) throws -> Any { try JSONSerialization.jsonObject(with:JSONEncoder().encode(value)) }
@MainActor func observeStore(_ mode:String,_ data:Data) async throws -> [String:Any] {
    let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString,isDirectory:true)
    try FileManager.default.createDirectory(at:root,withIntermediateDirectories:true,attributes:[.posixPermissions:0o700])
    defer { try? FileManager.default.removeItem(at:root) }
    let notebook = root.appendingPathComponent("notebook.sqlite"), output = root.appendingPathComponent("backup.lingrove"), input = root.appendingPathComponent("incoming.lingrove")
    let archive = VocabularyArchive(url:notebook), store = VocabularyStore(archive:archive)
    let image = VocabularyImageReference(digest:VocabularyImageStorage.digest(data),width:80,height:40,byteCount:data.count)
    if mode != "fresh-backup" { try await archive.seedBackupProbe(graph("all",image),image:image,data:data,writeMedia:true);await store.load() }
    let revision = store.revisionProbe, media = store.mediaRevision
    if ["backup-preserves-error","restore-after-error"].contains(mode) { _ = await store.removeSense(entryID:id(999),senseID:id(888)) }
    if (mode.hasPrefix("backup") && mode != "backup-after-restore") || mode == "fresh-backup" {
        if mode == "backup-error" || mode == "backup-failure-then-success" { await store.backup(to:notebook) }
        if mode != "backup-error" { await store.backup(to:output) }
    } else {
        let candidate = ["restore-all","restore-repair","restore-twice"].contains(mode) ? graph("all",image) : mode == "restore-empty" ? VocabularyDocument() : graph("plain",image)
        let images = VocabularyArchive.imageReferences(candidate).isEmpty ? [:] : [image.filename:data]
        if mode == "restore-repair" { try Data([0,1,2]).write(to:URL(fileURLWithPath:notebook.path + ".media").appendingPathComponent(image.filename)) }
        let bytes = mode == "restore-error" ? Data("{private invalid input".utf8) : try JSONEncoder().encode(VocabularyBackup(document:candidate,images:images))
        try bytes.write(to:input); await store.restore(from:input)
        if mode == "restore-twice" { await store.restore(from:input) }
        if mode == "backup-after-restore" { await store.backup(to:output) }
    }
    let backup = (try? Data(contentsOf:output)).flatMap { try? JSONSerialization.jsonObject(with:$0) }
    return ["loaded":store.isLoaded,"entries":try object(store.entries),"templates":try object(store.reviewTemplates),
        "error":store.errorMessage != nil,"pending":store.pending.count,"mediaChanged":store.mediaRevision != media,
        "revisionDelta":store.revisionProbe - revision,"imageReadable":(try? await archive.image(image)) == data,"backup":backup as Any? ?? NSNull()]
}
@main struct Main {
    static func main() async throws {
        let corpus = try JSONSerialization.jsonObject(with:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1]))) as! [String:Any]
        let input = Data(base64Encoded:corpus["input"] as! String)!;var rows = [[String:Any]]()
        for row in corpus["cases"] as! [[String:Any]] { let mode = row["id"] as! String;rows.append(["id":mode,"expected":try await observeStore(mode,input)]) }
        FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject:rows,options:[.sortedKeys]))
    }
}
'''


def store_sources(repo):
    values = vocabulary_store_sources(repo)
    marker = '\nextension VocabularyStore {\n    func applyProbe('
    if values[-1].count(marker) != 1: raise RuntimeError('Store test-extension seam changed')
    wrapper = values[-1][:values[-1].index(marker)]
    original = source(repo,'Cotabby/Services/Vocabulary/VocabularyStore.swift')
    wrapper += '\nextension VocabularyStore {\n' + between(original,'    func backup(to url:','    func remove(id:')
    wrapper += '\n    var revisionProbe: UInt64 { revision }\n}\n'
    return values[:-1] + [wrapper]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo',type=Path,required=True)
    parser.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1] / 'fixtures/parity/vocabulary-backup-store-v1.json')
    parser.add_argument('--capture-output',type=Path,help='Write actual observations to a NEW file')
    args = parser.parse_args();corpus = json.loads(args.fixture.read_text(encoding='utf-8'));rows = corpus['cases']
    if corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or [r.get('id') for r in rows] != list(CASES): parser.error('All 13 pinned Store backup/restore cases required')
    if any(r.get('macSource') != 'Cotabby/Services/Vocabulary/VocabularyStore.swift' or r.get('provenanceKind') != 'source-policy-extension' for r in rows): parser.error('Original Store provenance required')
    if not args.capture_output and any('expected' not in r for r in rows): parser.error('Actual observations required')
    with tempfile.TemporaryDirectory(prefix='lingrove-backup-store-') as temporary:
        root = Path(temporary);inputs = []
        for i,value in enumerate(store_sources(args.mac_repo)):
            p = root / f'source{i}.swift';p.write_text(value,encoding='utf-8');inputs.append(str(p))
        driver = root / 'main.swift';driver.write_text(DRIVER,encoding='utf-8');binary = root / 'store-backup-parity'
        subprocess.run(['swiftc','-swift-version','6','-O','-parse-as-library','-lsqlite3','-lz',*inputs,str(driver),'-o',str(binary)],check=True)
        actual = json.loads(subprocess.check_output([str(binary),str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x',encoding='utf-8') as output: json.dump(actual,output,ensure_ascii=False,indent=2);output.write('\n')
        print(f'Captured {len(actual)} actual Mac Store backup/restore observations');return
    if actual != [{'id':r['id'],'expected':r['expected']} for r in rows]: raise SystemExit('Original Store backup/restore mismatch')
    print(f'Verified {len(actual)} backup/restore observations against original Mac Store and archive')


if __name__ == '__main__':main()
