#!/usr/bin/env python3
"""Run frozen native Codable models against shared source-shaped document probes."""
import argparse
import json
from pathlib import Path
import struct
import subprocess
import tempfile

REVISION = '35b7fbe5c788c5524936ade1dfb7c89a3a285b04'
ROOT = Path(__file__).resolve().parents[1]
PATHS = (
    'Lingrove/Models/Context/LanguageCatalog.swift',
    'Lingrove/Models/LanguageLearning/LearningLanguage.swift',
    'Lingrove/Models/Vocabulary/VocabularyModels.swift',
    'Lingrove/Models/Vocabulary/VocabularyLifecycleModels.swift',
    'Lingrove/Models/Vocabulary/VocabularySemanticModels.swift',
    'Lingrove/Models/Vocabulary/VocabularyClassificationModels.swift',
    'Lingrove/Models/Vocabulary/VocabularyContentModels.swift',
    'Lingrove/Models/Review/ReviewModels.swift',
    'Lingrove/Models/Review/ReviewLayout.swift',
    'Lingrove/Support/Vocabulary/VocabularyNotebookProjection.swift',
)
EXPLANATION = 'Lingrove/Models/LanguageLearning/LearningExplanationModels.swift'
AUDIO = 'Lingrove/Models/Review/ReviewAudioModels.swift'
ERROR = 'Lingrove/Support/Vocabulary/VocabularyPolicy.swift'


def source(repo, path):
    return subprocess.check_output(['git', '-C', str(repo), 'show', f'{REVISION}:{path}'], text=True, encoding='utf-8')


def between(value, start, end):
    if value.count(start) != 1 or value.count(end) != 1:
        raise RuntimeError('Frozen source extraction boundary changed: ' + start)
    return value[value.index(start):value.index(end)]


def sources(repo):
    values = [source(repo, path) for path in PATHS]
    explanation = source(repo, EXPLANATION)
    values.append('import Foundation\n' + between(explanation, 'nonisolated enum LearningExplanationContext:',
                  '/// UI sessions borrow this app-owned boundary.'))
    audio = source(repo, AUDIO)
    marker = '/// A preparation sheet retains only its explicitly generated audio.'
    if audio.count(marker) != 1: raise RuntimeError('Frozen audio extraction boundary changed')
    values.append(audio[:audio.index(marker)])
    error = source(repo, ERROR)
    marker = 'nonisolated enum VocabularyError: LocalizedError {'
    if error.count(marker) != 1: raise RuntimeError('Frozen error extraction boundary changed')
    values.append('import Foundation\n' + error[error.index(marker):])
    return values


DRIVER = r'''
import Foundation
func bits(_ date: Date) -> String { String(format: "%016llx", date.timeIntervalSinceReferenceDate.bitPattern) }
func dateBits(_ doc: VocabularyDocument) -> [String: String] {
    let entry = doc.catalog[0], sense = entry.senses[0], claim = doc.interpretations[0]
    return ["entry.createdAt": bits(entry.createdAt), "entry.updatedAt": bits(entry.updatedAt),
        "sense.createdAt": bits(sense.createdAt), "sense.updatedAt": bits(sense.updatedAt),
        "sense.explanation.createdAt": bits(sense.explanations[0].createdAt),
        "source0.encounteredAt": bits(doc.sources[0].encounteredAt), "source1.encounteredAt": bits(doc.sources[1].encounteredAt),
        "claim.createdAt": bits(claim.createdAt), "claim.explanation.createdAt": bits(claim.explanations[0].createdAt),
        "collection.savedAt": bits(doc.collections[0].savedAt),
        "binding.syncedAt": bits(doc.collections[0].review!.bindings[0].syncedAt!),
        "node0.createdAt": bits(doc.semantics.nodes[0].createdAt), "node0.updatedAt": bits(doc.semantics.nodes[0].updatedAt),
        "node1.createdAt": bits(doc.semantics.nodes[1].createdAt), "node1.updatedAt": bits(doc.semantics.nodes[1].updatedAt)]
}
var observations: [[String: Any]] = []
for path in CommandLine.arguments.dropFirst() {
    let bytes = try Data(contentsOf: URL(fileURLWithPath: path))
    let doc = try JSONDecoder().decode(VocabularyDocument.self, from: bytes)
    let encoded = try JSONEncoder().encode(doc)
    let encodedObject = try JSONSerialization.jsonObject(with: encoded)
    let decodedAgain = try JSONDecoder().decode(VocabularyDocument.self, from: encoded)
    observations.append(["encoded": encodedObject, "dates": dateBits(doc), "roundtripDates": dateBits(decodedAgain)])
}
FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject: observations, options: [.sortedKeys]))
'''


def expected_bits(doc):
    entry = doc['catalog'][0]; sense = entry['senses'][0]; claim = doc['interpretations'][0]
    values = {'entry.createdAt': entry['createdAt'], 'entry.updatedAt': entry['updatedAt'],
              'sense.createdAt': sense['createdAt'], 'sense.updatedAt': sense['updatedAt'],
              'sense.explanation.createdAt': sense['explanations'][0]['createdAt'],
              'source0.encounteredAt': doc['sources'][0]['encounteredAt'], 'source1.encounteredAt': doc['sources'][1]['encounteredAt'],
              'claim.createdAt': claim['createdAt'], 'claim.explanation.createdAt': claim['explanations'][0]['createdAt'],
              'collection.savedAt': doc['collections'][0]['savedAt'], 'binding.syncedAt': doc['collections'][0]['review']['bindings'][0]['syncedAt'],
              'node0.createdAt': doc['semantics']['nodes'][0]['createdAt'], 'node0.updatedAt': doc['semantics']['nodes'][0]['updatedAt'],
              'node1.createdAt': doc['semantics']['nodes'][1]['createdAt'], 'node1.updatedAt': doc['semantics']['nodes'][1]['updatedAt']}
    return {key: struct.pack('>d', value).hex() for key, value in values.items()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, default=ROOT / 'fixtures/parity/vocabulary-document-frozen-v1.json')
    args = parser.parse_args()
    corpus = json.loads(args.fixture.read_text(encoding='utf-8'))
    rows = corpus['cases']
    if corpus['macReference'] != REVISION or corpus['provenance'] != 'windows-generated-source-shaped' or not rows:
        raise SystemExit('Unexpected frozen document fixture provenance')
    if len({row['id'] for row in rows}) != len(rows): raise SystemExit('Duplicate case IDs')
    with tempfile.TemporaryDirectory(prefix='lingrove-frozen-document-') as tmp:
        root = Path(tmp); files = []
        for index, text in enumerate(sources(args.mac_repo)):
            path = root / f'source{index}.swift'; path.write_text(text, encoding='utf-8'); files.append(str(path))
        main_file = root / 'main.swift'; main_file.write_text(DRIVER, encoding='utf-8'); binary = root / 'probe'
        subprocess.run(['swiftc', '-swift-version', '6', *files, str(main_file), '-o', str(binary)], check=True)
        probes = []
        for index, case in enumerate(rows):
            path = root / f'case{index}.json'
            path.write_text(json.dumps(case['input'], ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
            probes.append(str(path))
        observed = json.loads(subprocess.check_output([str(binary), *probes]))
    if len(observed) != len(rows): raise SystemExit('Document observation count mismatch')
    for case, result in zip(rows, observed):
        bits = expected_bits(case['input'])
        if result['encoded'] != case['expected'] or result['dates'] != bits or result['roundtripDates'] != bits:
            raise SystemExit('Frozen Swift document mismatch: ' + case['id'])
    print(f'Verified {len(rows)} frozen Swift Codable document probes and all 15 timestamp paths')


if __name__ == '__main__': main()
