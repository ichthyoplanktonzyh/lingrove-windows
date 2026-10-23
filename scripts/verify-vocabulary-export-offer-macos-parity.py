#!/usr/bin/env python3
"""Execute original notebook selections/beginExport with actual browse projections."""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, export_offer_sources

REQUIRED_CASES = {
    'default-all', 'oldest-first', 'batch-uses-all-order', 'explicit-filtered',
    'explicit-empty-selected', 'query-filtered', 'whitespace-is-filtered', 'tag-filtered',
    'language-filtered', 'batch-includes-hidden', 'single-overrides-forced-scope',
    'stale-batch-ignored', 'stale-single-preserved', 'forced-all-keeps-selected-list',
    'equal-dates-preserve-input',
}

_spec=importlib.util.spec_from_file_location('session_inputs',Path(__file__).with_name('verify-vocabulary-export-session-macos-parity.py'))
_inputs=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(_inputs)
if _inputs.DRIVER.count('final class ArchiveGate')!=1:raise RuntimeError('Fixed offer input boundary changed')
DRIVER=_inputs.DRIVER.split('final class ArchiveGate')[0]+r'''
func pair(_ value:[Int]) -> VocabularySelection { .init(entryID:id(value[0]),senseID:id(value[1])) }
func describe(_ values:[VocabularySelection]) -> [[String]] { values.map{[$0.entryID.uuidString.lowercased(),$0.senseID.uuidString.lowercased()]} }
func observe(_ row:[String:Any]) -> [String:Any] {
    var d=seed();d.entries[0].senses[0].tags=["Work"];d.entries[0].senses[1].tags=["Home"]
    d.entries.append(.init(id:id(10),expression:"café",dictionaryForm:"café",language:.french,kind:.word,
        senses:[.init(id:id(12),meaning:"coffee",tags:["Work"],createdAt:now,updatedAt:now)],createdAt:now,
        updatedAt:row["tie"] as? Bool == true ? now : now.addingTimeInterval(10)))
    let r=NotebookBrowseReference(store:d);r.newestFirst=row["newestFirst"] as? Bool ?? true;r.query=row["query"] as? String ?? "";r.selectedTag=row["tag"] as? String ?? ""
    if let language=row["language"] as? String { r.language=LearningLanguage(rawValue:language)! }
    r.batchSelection=Set((row["batch"] as? [[Int]] ?? []).map(pair))
    let scope=(row["scope"] as? String).map{VocabularyExportScope(rawValue:$0)!}
    let p=r.exportOfferProbe(scope:scope,single:(row["single"] as? [Int]).map(pair))
    return ["initialScope":p.initialScope.rawValue,"all":describe(p.all),"filtered":describe(p.filtered),"selected":describe(p.selected)]
}
let corpus=try JSONSerialization.jsonObject(with:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1]))) as! [String:Any]
let observations=(corpus["cases"] as! [[String:Any]]).map{["id":$0["id"]!,"expected":observe($0)]}
FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject:observations,options:[.sortedKeys]))
'''


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo',type=Path,required=True)
    parser.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/vocabulary-export-offer-v1.json')
    parser.add_argument('--capture-output',type=Path,help='Write actual observations into a NEW file')
    args=parser.parse_args();corpus=json.loads(args.fixture.read_text(encoding='utf-8'));rows=corpus['cases'];ids=[r.get('id')for r in rows]
    if corpus.get('version')!=1 or corpus.get('macReference')!=BASELINE or not rows or any(not value for value in ids) or len(set(ids))!=len(ids):parser.error('Offer probes require pinned version and unique IDs')
    if set(ids) != REQUIRED_CASES: parser.error('All 15 pinned offer coverage cases are required')
    if any(row.get('macSource')!='Cotabby/UI/Vocabulary/VocabularyNotebookView.swift' or row.get('provenanceKind')!='source-policy-extension' for row in rows):parser.error('Offer probes require exact source provenance')
    if not args.capture_output and any('expected'not in row for row in rows):parser.error('Every offer needs actual observations')
    with tempfile.TemporaryDirectory(prefix='lingrove-export-offer-')as temporary:
        root=Path(temporary);inputs=[]
        for index,value in enumerate(export_offer_sources(args.mac_repo)):
            path=root/f'source{index}.swift';path.write_text(value,encoding='utf-8');inputs.append(str(path))
        driver=root/'main.swift';driver.write_text(DRIVER,encoding='utf-8');binary=root/'export-offer-parity'
        subprocess.run(['swiftc','-swift-version','6','-O',*inputs,str(driver),'-o',str(binary)],check=True)
        observations=json.loads(subprocess.check_output([str(binary),str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x',encoding='utf-8')as output:json.dump(observations,output,ensure_ascii=False,indent=2);output.write('\n')
        print(f'Captured {len(observations)} actual Mac notebook export offers');return
    if observations!=[{'id':row['id'],'expected':row['expected']}for row in rows]:
        bad=next((r['id']for r,o in zip(rows,observations)if o!={'id':r['id'],'expected':r['expected']}),'count')
        raise SystemExit(f'Mac export offer mismatch: {bad}')
    print(f'Verified {len(rows)} notebook export scope/order offers against actual pinned Mac methods')


if __name__=='__main__':main()
