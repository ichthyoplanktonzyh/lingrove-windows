#!/usr/bin/env python3
"""Execute actual Mac Codable document/backup declarations, without archive or filesystem effects."""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, sense_policy_sources

CASES = (
    'empty', 'empty-templates', 'rich', 'media', 'content-absent', 'content-empty',
    'cards-absent', 'cards-empty', 'future-version', 'future-schema', 'empty-data',
    'all-byte-values', 'unknown-fields', 'missing-version', 'null-version', 'fraction-version',
    'missing-document', 'null-document', 'missing-images', 'null-images', 'images-array',
    'bad-base64', 'whitespace-base64', 'unpadded-base64', 'url-base64', 'image-number',
    'missing-schema', 'missing-entries', 'document-only', 'document-rich',
    'document-missing-schema', 'document-missing-entries', 'document-null-templates',
    'duplicate-version', 'duplicate-images', 'root-array', 'utf16-le', 'utf8-bom', 'invalid-utf8',
    'integral-version', 'exponent-version', 'large-version', 'large-schema',
    'large-fraction', 'large-integral-decimal',
    'rounded-small-fraction',
    'rounded-large-neighbor', 'exact-large-decimal',
    'truncated-large-fraction', 'truncated-negative-fraction',
    'minimum-version', 'minimum-decimal', 'near-minimum-fraction',
)
_spec = importlib.util.spec_from_file_location('backup_inputs', Path(__file__).with_name('verify-vocabulary-projection-macos-parity.py'))
_inputs = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(_inputs)
if _inputs.DRIVER.count('func describe(') != 1: raise RuntimeError('Fixed input graph boundary changed')
DRIVER = _inputs.DRIVER.split('func describe(')[0] + r'''
func sorted(_ value: Any) throws -> Data { try JSONSerialization.data(withJSONObject:value,options:[.sortedKeys,.fragmentsAllowed]) }
func observe(_ mode: String) throws -> [String: Any] {
    var d = VocabularyDocument()
    if ["rich","content-absent","content-empty","cards-absent","cards-empty","document-rich"].contains(mode) { d = document() }
    if mode == "empty-templates" { d.reviewTemplates = [] }
    if mode == "content-absent" { d.entries[0].senses[0].content = nil }
    if mode == "content-empty" { d.entries[0].senses[0].content = [] }
    if mode == "cards-absent" { d.entries[0].senses[0].reviewCards = nil }
    if mode == "cards-empty" { d.entries[0].senses[0].reviewCards = [] }
    if mode == "future-schema" { d.schemaVersion = 99 }
    var images: [String: Data] = [:]
    if mode == "media" { images = ["lg-image-fixture.png":Data([0,1,2,254,255]),"other":Data([3,4])] }
    if mode == "empty-data" { images = ["empty":Data()] }
    if mode == "all-byte-values" { images = ["all":Data((0...255).map{UInt8($0)})] }
    let documentOnly = mode.hasPrefix("document-")
    let encoder = JSONEncoder()
    var backup = VocabularyBackup(document:d,images:images)
    if mode == "future-version" { backup.version = 2 }
    var root = try JSONSerialization.jsonObject(with:documentOnly ? encoder.encode(d) : encoder.encode(backup)) as! [String:Any]
    switch mode {
    case "unknown-fields": root["future"] = ["ignored":true]
    case "missing-version": root.removeValue(forKey:"version")
    case "null-version": root["version"] = NSNull()
    case "fraction-version": root["version"] = 1.5
    case "missing-document": root.removeValue(forKey:"document")
    case "null-document": root["document"] = NSNull()
    case "missing-images": root.removeValue(forKey:"images")
    case "null-images": root["images"] = NSNull()
    case "images-array": root["images"] = [] as [String]
    case "bad-base64": root["images"] = ["x":"@@@="]
    case "whitespace-base64": root["images"] = ["x":"AQ ID\n"]
    case "unpadded-base64": root["images"] = ["x":"AQ"]
    case "url-base64": root["images"] = ["x":"-_8="]
    case "image-number": root["images"] = ["x":123]
    case "large-version": root["version"] = Int64.max
    case "minimum-version": root["version"] = Int64.min
    case "large-schema":
        var value = root["document"] as! [String:Any];value["schemaVersion"] = Int64.max;root["document"] = value
    case "missing-schema","missing-entries":
        var value = root["document"] as! [String:Any];value.removeValue(forKey:mode == "missing-schema" ? "schemaVersion" : "entries");root["document"] = value
    case "document-missing-schema": root.removeValue(forKey:"schemaVersion")
    case "document-missing-entries": root.removeValue(forKey:"entries")
    case "document-null-templates": root["reviewTemplates"] = NSNull()
    default: break
    }
    var input = try sorted(root)
    if mode == "integral-version" || mode == "exponent-version" {
        input = Data(String(data:input,encoding:.utf8)!.replacingOccurrences(of:"\"version\":1",with:mode == "integral-version" ? "\"version\":1.0" : "\"version\":1e0").utf8)
    }
    if mode == "large-fraction" || mode == "large-integral-decimal" {
        input = Data(String(data:input,encoding:.utf8)!.replacingOccurrences(of:"\"version\":1",with:mode == "large-fraction" ? "\"version\":9223372036854775807.0000000001" : "\"version\":9223372036854775807.0").utf8)
    }
    if mode == "rounded-small-fraction" { input = Data(String(data:input,encoding:.utf8)!.replacingOccurrences(of:"\"version\":1",with:"\"version\":4503599627370495.1").utf8) }
    if mode == "rounded-large-neighbor" || mode == "exact-large-decimal" {
        input = Data(String(data:input,encoding:.utf8)!.replacingOccurrences(of:"\"version\":1",with:mode == "rounded-large-neighbor" ? "\"version\":9007199254740993.0" : "\"version\":9007199254740992.0").utf8)
    }
    if mode == "truncated-large-fraction" || mode == "truncated-negative-fraction" {
        input = Data(String(data:input,encoding:.utf8)!.replacingOccurrences(of:"\"version\":1",with:mode == "truncated-large-fraction" ? "\"version\":9007199254740993.1" : "\"version\":-9007199254740993.9").utf8)
    }
    if mode == "minimum-decimal" || mode == "near-minimum-fraction" {
        input = Data(String(data:input,encoding:.utf8)!.replacingOccurrences(of:"\"version\":1",with:mode == "minimum-decimal" ? "\"version\":-9223372036854775808.0" : "\"version\":-9223372036854775807.9").utf8)
    }
    if mode == "duplicate-version" { input = Data(("{\"version\":2," + String(data:input,encoding:.utf8)!.dropFirst()).utf8) }
    if mode == "duplicate-images" {
        let doc = String(data:try sorted(JSONSerialization.jsonObject(with:encoder.encode(d))),encoding:.utf8)!
        input = Data(("{\"images\":{\"same\":\"AQ==\"},\"images\":{\"same\":\"Ag==\"},\"document\":" + doc + ",\"version\":1}").utf8)
    }
    if mode == "root-array" { input = Data("[]".utf8) }
    if mode == "utf16-le" { input = Data([0xff,0xfe]) + String(data:input,encoding:.utf8)!.data(using:.utf16LittleEndian)! }
    if mode == "utf8-bom" { input = Data([0xef,0xbb,0xbf]) + input }
    if mode == "invalid-utf8" { input = Data([0xff]) + input }
    let expected: [String:Any]
    do {
        let bytes = try documentOnly ? encoder.encode(JSONDecoder().decode(VocabularyDocument.self,from:input)) : encoder.encode(JSONDecoder().decode(VocabularyBackup.self,from:input))
        expected = ["valid":true,"json":try JSONSerialization.jsonObject(with:bytes)]
    } catch is DecodingError { expected = ["valid":false] }
    return ["inputBase64":input.base64EncodedString(),"expected":expected]
}
let corpus = try JSONSerialization.jsonObject(with:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1]))) as! [String:Any]
let observations = try (corpus["cases"] as! [[String:Any]]).map { row -> [String:Any] in
    var result = try observe(row["id"] as! String);result["id"] = row["id"]!;return result
}
FileHandle.standardOutput.write(try sorted(observations))
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, default=Path(__file__).resolve().parents[1] / 'fixtures/parity/vocabulary-backup-codec-v1.json')
    parser.add_argument('--capture-output', type=Path, help='Write actual source observations into a NEW file')
    args = parser.parse_args(); corpus = json.loads(args.fixture.read_text(encoding='utf-8')); rows = corpus['cases']
    if corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or [row.get('id') for row in rows] != list(CASES):
        parser.error('All 53 pinned codec coverage cases are required in source order')
    for row in rows:
        path = 'Cotabby/Models/Vocabulary/VocabularyModels.swift' if row['id'].startswith('document-') else 'Cotabby/Models/Vocabulary/VocabularyContentModels.swift'
        if row.get('macSource') != path or row.get('provenanceKind') != 'source-policy-extension': parser.error('Exact original Codable provenance is required')
        if not args.capture_output and ('inputBase64' not in row or 'expected' not in row): parser.error('Actual source observations are required')
    with tempfile.TemporaryDirectory(prefix='lingrove-backup-codec-') as temporary:
        root = Path(temporary); inputs = []
        for index, value in enumerate(sense_policy_sources(args.mac_repo)):
            path = root / f'source{index}.swift'; path.write_text(value, encoding='utf-8'); inputs.append(str(path))
        driver = root / 'main.swift'; driver.write_text(DRIVER, encoding='utf-8'); binary = root / 'backup-codec-parity'
        subprocess.run(['swiftc', '-swift-version', '6', '-O', *inputs, str(driver), '-o', str(binary)], check=True)
        actual = json.loads(subprocess.check_output([str(binary), str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x', encoding='utf-8') as output: json.dump(actual, output, ensure_ascii=False, indent=2); output.write('\n')
        print(f'Captured {len(actual)} actual Mac document/backup codec observations'); return
    expected = [{'id': row['id'], 'inputBase64': row['inputBase64'], 'expected': row['expected']} for row in rows]
    if actual != expected:
        bad = next((row['id'] for row, observed in zip(rows, actual) if observed != {'id': row['id'], 'inputBase64': row['inputBase64'], 'expected': row['expected']}), 'count')
        raise SystemExit(f'Backup codec mismatch: {bad}')
    print(f'Verified {len(actual)} document/backup codecs against actual pinned Mac Codable declarations')


if __name__ == '__main__': main()
