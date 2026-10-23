#!/usr/bin/env python3
"""Execute the actual Mac writing capture factory, retaining translation, meaning and provenance."""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile

_spec = importlib.util.spec_from_file_location('capture_reference', Path(__file__).with_name('verify-learning-capture-macos-parity.py'))
_reference = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(_reference)
BASELINE = _reference.BASELINE
DRIVER = _reference.DRIVER
start = DRIVER.index('    var projected: Value = .null')
end = DRIVER.index('    let text = presentation.translation,', start)
DRIVER = DRIVER[:start] + r'''
    var projected: Value = .null
    func optional(_ text: String?) -> Value { text.map(Value.string) ?? .null }
    if let capture {
        var example: Value = .null
        if let value = capture.example {
            example = .object(["text": .string(value.text), "translation": .string(value.translation),
                "translationLanguage": optional(value.translationLanguage?.rawValue),
                "ranges": .array(value.ranges.map { rangeJSON($0.nsRange) }),
                "completeness": .string(value.completeness.rawValue), "meaning": .string(value.meaning),
                "meaningLanguage": optional(value.meaningLanguage?.rawValue), "meaningOrigin": .string(value.meaningOrigin.rawValue),
                "app": optional(value.appBundleIdentifier)])
        }
        projected = .object(["expression": .string(capture.expression), "dictionaryForm": .string(capture.dictionaryForm),
            "language": .string(capture.language.rawValue), "kind": .string(capture.kind.rawValue),
            "meaning": .string(capture.meaning), "meaningLanguage": optional(capture.meaningLanguage?.rawValue), "example": example,
            "input": try JSONDecoder().decode(Value.self, from: JSONEncoder().encode(LearningExplanationPolicy.input(capture)))])
    }
''' + DRIVER[end:]
marker = '    presentation.isTranslating = status == "Loading"\n'
if DRIVER.count(marker) != 1: raise RuntimeError('Capture presentation boundary must be unique')
DRIVER = DRIVER.replace(marker, marker + '    if case let .string(app) = row["app"] { presentation.vocabularyAppBundleIdentifier = app }\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, default=Path(__file__).resolve().parents[1] / 'fixtures/parity/vocabulary-card-capture-v1.json')
    parser.add_argument('--capture-output', type=Path)
    args = parser.parse_args(); corpus = json.loads(args.fixture.read_text()); rows = corpus['cases']
    _reference.validate_corpus(parser, corpus)
    if any(type(r.get('app')) is not str for r in rows): parser.error('Card capture provenance requires an explicit application identity')
    with tempfile.TemporaryDirectory(prefix='lingrove-card-capture-') as temporary:
        root = Path(temporary); inputs = []
        for index, value in enumerate(_reference.reference_sources(args.mac_repo)):
            path = root / f'source{index}.swift'; path.write_text(value); inputs.append(str(path))
        driver = root / 'main.swift'; driver.write_text(DRIVER); binary = root / 'card-capture'
        subprocess.run(['swiftc', '-O', *inputs, str(driver), '-o', str(binary)], check=True)
        observed = json.loads(subprocess.check_output([str(binary), str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x') as output: json.dump(observed, output, indent=2, ensure_ascii=False); output.write('\n')
        print(f'Captured {len(observed)} actual Mac durable-card values'); return
    if observed != [{'id': r['id'], 'expected': r['expected'], 'facts': r['facts']} for r in rows]: raise SystemExit('Original card capture mismatch')
    print(f'Verified {len(observed)} durable-card cases against original Mac factory')


if __name__ == '__main__': main()
