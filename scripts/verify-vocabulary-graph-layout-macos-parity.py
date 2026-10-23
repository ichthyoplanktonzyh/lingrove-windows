#!/usr/bin/env python3
"""Run the complete pinned GraphSimulation and unchanged vendored Grape package, without UI/model/network."""
import argparse
import json
import math
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE
from macos_graph_reference import build_graph_reference

SOURCE = 'Packages/LingroveGraphLayout/Sources/LingroveGraphLayout/GraphSimulation.swift'
CASES = ('single-origin','single-offset','single-velocity','pair-unlinked','pair-linked','pair-close','pair-coincident',
    'pair-horizontal','pair-vertical','cluster-near','square','star-degree','chain','duplicate-links','self-link',
    'skip-outside-edge','no-center','no-repulsion','no-link-strength','low-forces','high-forces','moving-particles',
    'tree-quadrants','tree-near-threshold','pin-first','pin-change','pin-no-position','unpin-cool','cooled-idle','reheat-cooled','tree-64')
DRIVER = r'''
import Foundation
import LingroveGraphLayout
func points(_ value:Any)->[SIMD2<Double>] { (value as! [[NSNumber]]).map { SIMD2($0[0].doubleValue,$0[1].doubleValue) } }
func observation(_ value:GraphSimulation.Snapshot)->[String:Any] {
    ["positions":value.positions.map{[$0.x,$0.y]},"velocities":value.velocities.map{[$0.x,$0.y]},"running":value.isRunning]
}
let data = try Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1]))
let corpus = try JSONSerialization.jsonObject(with:data) as! [String:Any]
let rows = (corpus["cases"] as! [[String:Any]]).map { row -> [String:Any] in
    let input = row["input"] as! [String:Any], p = input["parameters"] as! [String:NSNumber]
    let simulation = GraphSimulation(positions:points(input["positions"]!),velocities:points(input["velocities"]!),
        edges:(input["edges"] as! [[Int]]).map{SIMD2($0[0],$0[1])},
        parameters:.init(center:p["center"]!.doubleValue,repulsion:p["repulsion"]!.doubleValue,
                         linkStrength:p["linkStrength"]!.doubleValue,linkDistance:p["linkDistance"]!.doubleValue))
    var snapshots = [observation(simulation.snapshot)]
    for step in input["steps"] as! [[String:Any]] {
        let pinned = step["pinned"] as? Int
        let point = (step["position"] as? [NSNumber]).map { SIMD2($0[0].doubleValue,$0[1].doubleValue) }
        var snapshot = simulation.snapshot
        for _ in 0..<(step["count"] as! Int) { snapshot = simulation.step(pinned:pinned,at:point) }
        snapshots.append(observation(snapshot))
    }
    return ["id":row["id"]!,"expected":snapshots]
}
FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject:rows,options:[.sortedKeys]))
'''


def same(a, b):
    if isinstance(a, bool) or isinstance(b, bool): return type(a) is type(b) and a == b
    if isinstance(a, (float, int)) and isinstance(b, (float, int)):
        return math.isfinite(a) and math.isfinite(b) and abs(a-b) <= max(1e-8, abs(a)*1e-10)
    if isinstance(a, list) and isinstance(b, list): return len(a) == len(b) and all(same(x,y) for x,y in zip(a,b))
    if isinstance(a, dict) and isinstance(b, dict): return a.keys() == b.keys() and all(same(a[k],b[k]) for k in a)
    return a == b


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, default=Path(__file__).resolve().parents[1]/'fixtures/parity/vocabulary-graph-layout-v1.json')
    parser.add_argument('--capture-output', type=Path, help='Write actual results to a NEW file')
    args = parser.parse_args(); corpus = json.loads(args.fixture.read_text(encoding='utf-8')); rows = corpus['cases']
    if corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or [r.get('id') for r in rows] != list(CASES):parser.error('All 31 original solver case IDs are required')
    if any(r.get('macSource') != SOURCE or r.get('provenanceKind') != 'source-policy-extension' for r in rows):parser.error('Exact simulation provenance required')
    if not args.capture_output and any('expected' not in r for r in rows):parser.error('Source observations required')
    with tempfile.TemporaryDirectory(prefix='lingrove-original-grape-') as temporary:
        binary = build_graph_reference(args.mac_repo,Path(temporary),DRIVER)
        actual = json.loads(subprocess.check_output([str(binary),str(args.fixture.resolve())]))
    if args.capture_output:
        with args.capture_output.open('x',encoding='utf-8') as output:json.dump(actual,output,ensure_ascii=False,indent=2);output.write('\n')
        print(f'Captured {len(actual)} original GraphSimulation/Grape observations');return
    expected = [{'id':r['id'],'expected':r['expected']} for r in rows]
    if not same(expected,actual):
        bad = next((r['id'] for r,a in zip(expected,actual) if not same(r,a)),'count');raise SystemExit('Original graph simulation mismatch: '+bad)
    print(f'Verified {len(actual)} observations against complete pinned GraphSimulation and unchanged Grape package')


if __name__ == '__main__':main()
