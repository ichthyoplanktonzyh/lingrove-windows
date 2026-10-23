#!/usr/bin/env python3
"""Run the complete original Mac graph worker/actor and actual pinned Grape package."""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, between, source
from macos_graph_reference import build_graph_reference

_spec = importlib.util.spec_from_file_location('solver_observations',Path(__file__).with_name('verify-vocabulary-graph-layout-macos-parity.py'))
_solver = importlib.util.module_from_spec(_spec);_spec.loader.exec_module(_solver)
SOURCE = 'Cotabby/Services/Vocabulary/VocabularyGraphLayoutWorker.swift'
CASES = ('empty','head-seeds','sense-seed','first-expanded','expand-cached','advance','cosmetic-cold','labels-cold',
    'force-reheat','edge-reheat','reorder','filter-return','pin-release','stale-configure','stale-advance','suspend-reopen',
    'stale-suspend','suspend-final','suspend-empty','dangling-edge','drop-pin','normalized-forces','new-epoch','cache-at-limit',
    'cache-over-limit','suspend-cache-bound','thousand-heads')
DRIVER = r'''
import Foundation
import CoreGraphics
func uuid(_ n:Int)->UUID { UUID(uuidString:String(format:"00000000-0000-0000-0000-%012x",n))! }
func identity(_ key:String)->VocabularyGraphNodeID {
    let parts = key.split(separator:"_");let entry = Int(parts[0].dropFirst())!
    return parts.count == 1 ? .headword(uuid(entry)) : .sense(.init(entryID:uuid(entry),senseID:uuid(Int(parts[1])!)))
}
func key(_ id:VocabularyGraphNodeID)->String {
    let n = Int(id.entryID.uuidString.suffix(12),radix:16)!
    if let s = id.selection { return "s\(n)_\(Int(s.senseID.uuidString.suffix(12),radix:16)!)" };return "h\(n)"
}
func settings(_ input:[String:Any])->VocabularyGraphSettings {
    var s = VocabularyGraphSettings()
    if let v = input["nodeSize"] as? Double { s.nodeSize = v };if let v = input["linkWidth"] as? Double { s.linkWidth = v }
    if let v = input["center"] as? Double { s.center = v };if let v = input["repulsion"] as? Double { s.repulsion = v }
    if let v = input["linkStrength"] as? Double { s.linkStrength = v };if let v = input["linkDistance"] as? Double { s.linkDistance = v }
    if let v = input["labels"] as? String { s.labels = VocabularyGraphSettings.Labels(rawValue:v)! };return s
}
func graph(_ action:[String:Any])->VocabularyGraphSnapshot {
    let keys = (action["nodes"] as? [String]) ?? (0..<(action["count"] as! Int)).map { "h\($0 + (action["start"] as? Int ?? 1))" }
    let labels = action["labels"] as? [String:String] ?? [:]
    return .init(nodes:keys.map{.init(id:identity($0),label:labels[$0] ?? $0)},
        edges:((action["edges"] as? [[String]]) ?? []).map{.init(source:identity($0[0]),target:identity($0[1]))},
        headwordCount:keys.filter{!$0.contains("_")}.count,
        matches:Set(((action["matches"] as? [String]) ?? []).map(identity)),searchQuery:action["query"] as? String ?? "")
}
func describe(_ frame:VocabularyGraphFrame?,probes:Set<String>?)->Any {
    guard let frame else { return NSNull() };var values = [String:[Double]]()
    for (id,p) in frame.positions { let name = key(id);if probes == nil || probes!.contains(name) { values[name] = [p.x,p.y] } }
    return ["positions":values,"count":frame.positions.count,"running":frame.isRunning,"finite":frame.positions.values.allSatisfy{$0.x.isFinite && $0.y.isFinite}] as [String:Any]
}
@main struct Main {
    static func main() async throws {
        let corpus = try JSONSerialization.jsonObject(with:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1]))) as! [String:Any]
        var rows = [[String:Any]]()
        for row in corpus["cases"] as! [[String:Any]] {
            let worker = VocabularyGraphLayoutWorker();var observations = [Any]()
            let probes = (row["probes"] as? [String]).map{Set($0)}
            for action in row["actions"] as! [[String:Any]] {
                let epoch = (action["epoch"] as! NSNumber).uint64Value
                switch action["op"] as! String {
                case "configure":observations.append(describe(await worker.configure(graph(action),settings:settings(action["settings"] as? [String:Any] ?? [:]),epoch:epoch),probes:probes))
                case "advance":
                    let pinned = (action["pinned"] as? String).map(identity)
                    let point = (action["position"] as? [Double]).map{CGPoint(x:$0[0],y:$0[1])}
                    var frame:VocabularyGraphFrame?
                    for _ in 0..<(action["count"] as? Int ?? 1) { frame = await worker.advance(epoch:epoch,pinned:pinned,position:point) }
                    observations.append(describe(frame,probes:probes))
                default:
                    let final = (action["finalNode"] as? String).map(identity)
                    let point = (action["finalPosition"] as? [Double]).map{CGPoint(x:$0[0],y:$0[1])}
                    await worker.suspend(epoch:epoch,finalNode:final,finalPosition:point);observations.append(NSNull())
                }
            }
            rows.append(["id":row["id"]!,"expected":observations])
        }
        FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject:rows,options:[.sortedKeys]))
    }
}
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo',type=Path,required=True)
    parser.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/vocabulary-graph-worker-v1.json')
    parser.add_argument('--capture-output',type=Path,help='Write actual observations to a NEW file')
    args = parser.parse_args();corpus = json.loads(args.fixture.read_text());rows = corpus['cases']
    if corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or [r.get('id') for r in rows] != list(CASES):parser.error('All 27 original worker cases required')
    if any(r.get('macSource') != SOURCE or r.get('provenanceKind') != 'source-policy-extension' for r in rows):parser.error('Exact worker provenance required')
    if not args.capture_output and any('expected' not in r for r in rows):parser.error('Actual original actor observations required')
    selection = 'import Foundation\n' + between(source(args.mac_repo,'Cotabby/Models/Vocabulary/VocabularyModels.swift'),'nonisolated struct VocabularySelection:','/// The archive records')
    declarations = [selection,source(args.mac_repo,'Cotabby/Models/Vocabulary/VocabularyGraphModels.swift'),source(args.mac_repo,SOURCE)]
    with tempfile.TemporaryDirectory(prefix='lingrove-original-graph-worker-') as temporary:
        binary = build_graph_reference(args.mac_repo,Path(temporary),DRIVER,declarations)
        actual = json.loads(subprocess.check_output([str(binary),str(args.fixture.resolve())]))
    if args.capture_output:
        with args.capture_output.open('x') as output:json.dump(actual,output,indent=2);output.write('\n')
        print(f'Captured {len(actual)} original graph worker/Grape observations');return
    expected = [{'id':r['id'],'expected':r['expected']} for r in rows]
    if not _solver.same(expected,actual):
        bad = next((r['id'] for r,a in zip(expected,actual) if not _solver.same(r,a)),'count');raise SystemExit('Original graph worker mismatch: '+bad)
    print(f'Verified {len(actual)} observations against complete original graph actor and Grape package')


if __name__ == '__main__':main()
