#!/usr/bin/env python3
"""Execute actual Mac export destination protection and atomic save in isolated temporary folders."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, archive_export_sources

DRIVER = r'''
import Foundation
func observe(_ row: [String: Any]) async throws -> [String: Any] {
    let fm = FileManager.default, folder = fm.temporaryDirectory.appendingPathComponent("lingrove-export-save-" + UUID().uuidString)
    try fm.createDirectory(at: folder, withIntermediateDirectories: false, attributes: [.posixPermissions: 0o700])
    defer { try? fm.removeItem(at: folder) }
    let notebook = folder.appendingPathComponent("notebook.sqlite"), media = folder.appendingPathComponent("notebook.sqlite.media")
    let archive = VocabularyArchive(url:notebook), old = Data([9,8,7]), mode = row["mode"] as! String
    var destination = folder.appendingPathComponent("export.json")
    if mode == "protected" { destination = URL(fileURLWithPath:notebook.path + (row["suffix"] as! String)); try old.write(to:destination) }
    if mode == "hardLink" || mode == "symlink" {
        try old.write(to:notebook)
        if mode == "hardLink" { try fm.linkItem(at:notebook,to:destination) }
        else { try fm.createSymbolicLink(at:destination,withDestinationURL:notebook) }
    }
    if mode == "mediaRoot" || mode == "mediaChild" {
        try fm.createDirectory(at:media,withIntermediateDirectories:false)
        try old.write(to:media.appendingPathComponent("child.json"))
        destination = mode == "mediaRoot" ? media : media.appendingPathComponent("child.json")
    }
    if row["replace"] as? Bool == true { try old.write(to:destination) }
    let protectedPaths = ["","-wal","-shm","-journal",".lock"].map { URL(fileURLWithPath:notebook.path + $0) }
    let before = protectedPaths.map { try? Data(contentsOf:$0) }
    var c = VocabularyExportConfiguration(); c.format = .init(rawValue:row["format"] as! String)!; c.images = row["images"] as! Bool
    var p = try VocabularyExportPolicy.package([],configuration:c)
    if row["attachment"] as? Bool == true { p.images[row["badImage"] as? Bool == true ? "unsafe.png" : "lg-image-" + String(repeating:"a",count:64) + ".png"] = Data([1,2,3]) }
    var failure: Any = NSNull()
    do {
        if row["cancelled"] as? Bool == true {
            let captured = p, url = destination, (start, continuation) = AsyncStream<Void>.makeStream()
            let task = Task {
                for await _ in start { break }
                try await archive.writeExport(captured,to:url)
            }
            task.cancel(); continuation.finish(); try await task.value
        } else { try await archive.writeExport(p,to:destination) }
    } catch is CancellationError { failure = "cancelled" }
    catch let error as VocabularyExportError { failure = String(describing:error) }
    catch let error as VocabularyError { failure = String(describing:error) }
    let data = try? Data(contentsOf:destination)
    return ["error":failure,"destinationBase64":data.map{$0.base64EncodedString()} as Any? ?? NSNull(),
        "protectedUnchanged":zip(before,protectedPaths).allSatisfy { $0.0 == (try? Data(contentsOf:$0.1)) },
        "notebookExists":fm.fileExists(atPath:notebook.path),
        "mediaUnchanged":mode.hasPrefix("media") ? (try? Data(contentsOf:media.appendingPathComponent("child.json"))) == old : true,
        "stages":try fm.contentsOfDirectory(atPath:folder.path).filter{$0.hasPrefix(".notebook-")}.count]
}
@main struct Main {
    static func main() async throws {
        let corpus = try JSONSerialization.jsonObject(with:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1]))) as! [String:Any]
        var observations = [[String:Any]]()
        for row in corpus["cases"] as! [[String:Any]] { observations.append(["id":row["id"]!,"expected":try await observe(row)]) }
        FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject:observations,options:[.sortedKeys]))
    }
}
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, default=Path(__file__).resolve().parents[1] / 'fixtures/parity/vocabulary-export-save-v1.json')
    parser.add_argument('--capture-output', type=Path, help='Write actual observations into a NEW file')
    args = parser.parse_args(); corpus = json.loads(args.fixture.read_text(encoding='utf-8')); rows = corpus['cases']; ids = [r.get('id') for r in rows]
    if corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or not rows or any(not value for value in ids) or len(set(ids)) != len(ids): parser.error('Save probes require pinned version and unique IDs')
    if any(row.get('macSource') != 'Cotabby/Services/Vocabulary/VocabularyArchive+Export.swift' or row.get('provenanceKind') != 'source-policy-extension' for row in rows): parser.error('Save probes require exact source provenance')
    if not args.capture_output and any('expected' not in row for row in rows): parser.error('Every save case needs actual observations')
    with tempfile.TemporaryDirectory(prefix='lingrove-export-save-driver-') as temporary:
        root = Path(temporary); inputs = []
        for index, value in enumerate(archive_export_sources(args.mac_repo)):
            path = root / f'source{index}.swift'; path.write_text(value, encoding='utf-8'); inputs.append(str(path))
        driver = root / 'main.swift'; driver.write_text(DRIVER, encoding='utf-8'); binary = root / 'export-save-parity'
        subprocess.run(['swiftc', '-swift-version', '6', '-O', '-parse-as-library', '-lsqlite3', '-lz', *inputs, str(driver), '-o', str(binary)], check=True)
        observations = json.loads(subprocess.check_output([str(binary), str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x', encoding='utf-8') as output: json.dump(observations, output, ensure_ascii=False, indent=2); output.write('\n')
        print(f'Captured {len(observations)} actual Mac export save observations'); return
    if observations != [{'id': row['id'], 'expected': row['expected']} for row in rows]:
        bad = next((r['id'] for r, o in zip(rows, observations) if o != {'id': r['id'], 'expected': r['expected']}), 'count')
        raise SystemExit(f'Mac export save mismatch: {bad}')
    print(f'Verified {len(rows)} export save/path/cancellation cases against complete original Mac effects')


if __name__ == '__main__': main()
