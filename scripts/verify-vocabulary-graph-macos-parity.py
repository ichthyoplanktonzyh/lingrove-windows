#!/usr/bin/env python3
"""Execute complete pinned Mac graph values/projection/camera without a solver or UI window."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, between, sense_policy_sources, source

CASES = ('p-overview','p-expand-first','p-expand-all','p-head-query','p-head-expanded','p-form-query','p-sense-query','p-note-query',
    'p-usage-query','p-no-match','p-trim-query','p-blank-query','p-head-accent','p-sense-accent','p-order','p-reverse-senses','p-empty',
    'p-duplicate-labels','p-projected-sense','p-title-content','s-default','s-low','s-high','s-finite','s-nonfinite','s-label-all','s-label-focused',
    'c-screen','c-world','c-zoom-anchor','c-zoom-min','c-zoom-max','c-zoom-zero','c-zoom-negative','c-zoom-nan','c-zoom-infinite',
    'c-fit-empty','c-fit-single','c-fit-pair','c-fit-reverse','c-fit-small','c-fit-zero-width','c-fit-zero-height','c-fit-large','c-fit-repeated')
DRIVER = r'''
import Foundation
import CoreGraphics
func id(_ n:Int)->UUID { UUID(uuidString:String(format:"00000000-0000-0000-0000-%012x",n))! }
func object<T:Encodable>(_ value:T)throws->Any { try JSONSerialization.jsonObject(with:JSONEncoder().encode(value)) }
func identity(_ id:VocabularyGraphNodeID)->[String:Any] { ["entry":id.entryID.uuidString.lowercased(),"sense":id.selection?.senseID.uuidString.lowercased() as Any? ?? NSNull()] }
func key(_ id:VocabularyGraphNodeID)->String { id.entryID.uuidString + (id.selection?.senseID.uuidString ?? "") }
func describe(_ graph:VocabularyGraphSnapshot)->[String:Any] {
    ["nodes":graph.nodes.map{["id":identity($0.id),"label":$0.label]},"edges":graph.edges.map{["source":identity($0.source),"target":identity($0.target)]},
     "headwordCount":graph.headwordCount,"matches":graph.matches.sorted{key($0)<key($1)}.map(identity),"query":graph.searchQuery]
}
func point(_ value:CGPoint)->[Double] { [value.x,value.y] }
func camera(_ value:VocabularyGraphCamera)->[String:Any] { ["scale":value.scale,"offset":point(value.offset)] }
func seed()->VocabularyDocument {
    let now = Date(timeIntervalSince1970:0)
    let usage = VocabularyExample(id:id(3),text:"The bank.",translation:"河岸",ranges:[.init(NSRange(location:4,length:4))],completeness:.sentence,meaning:"context",createdAt:now)
    return .init(entries:[.init(id:id(1),expression:"bank",dictionaryForm:"banking",language:.english,kind:.word,
        senses:[.init(id:id(2),meaning:"银行",note:"Workplace",tags:["Finance"],createdAt:now,updatedAt:now),
                .init(id:id(4),meaning:"河岸",note:"river",examples:[usage],createdAt:now,updatedAt:now)],createdAt:now,updatedAt:now),
        .init(id:id(10),expression:"café",dictionaryForm:"café",language:.french,kind:.word,
        senses:[.init(id:id(12),meaning:"coffee",createdAt:now,updatedAt:now)],createdAt:now,updatedAt:now)])
}
func observe(_ mode:String)throws->[String:Any] {
    if mode.hasPrefix("p-") {
        var doc = seed(), expanded = Set<UUID>();var query = ""
        switch mode {
        case "p-expand-first":expanded = [id(1)]
        case "p-expand-all":expanded = [id(1),id(10)]
        case "p-head-query":query = "BANK"
        case "p-head-expanded":query = "bank";expanded = [id(1)]
        case "p-form-query":query = "banking"
        case "p-sense-query":query = "河岸"
        case "p-note-query":query = "Workplace"
        case "p-usage-query":query = "context"
        case "p-no-match":query = "not found"
        case "p-trim-query":query = "\u{200B} BANK\n"
        case "p-blank-query":query = "\u{200B} \n\t"
        case "p-head-accent":doc.entries = [doc.entries[1]];query = "cafe"
        case "p-sense-accent":doc.entries[0].senses[0].meaning = "café";query = "cafe"
        case "p-order":doc.entries.reverse();expanded = [id(1),id(10)]
        case "p-reverse-senses":doc.entries[0].senses.reverse();expanded = [id(1)]
        case "p-empty":doc.entries = []
        case "p-duplicate-labels":doc.entries[1].expression = "bank";doc.entries[0].senses[0].meaning = "same";doc.entries[1].senses[0].meaning = "same";expanded = [id(1),id(10)]
        case "p-projected-sense":doc.entries[0].senses = [doc.entries[0].senses[1]];expanded = [id(1)]
        case "p-title-content":doc.entries[0].senses[0].meaning = "";doc.entries[0].senses[0].content = [.init(id:id(20),content:.text(.init(text:"  authored title  ")))];expanded = [id(1)]
        default:break
        }
        let graph = VocabularyGraphProjection.make(entries:doc.entries,expanded:expanded,query:query), trimmed = graph.searchQuery
        let heads = doc.entries.flatMap { e in [e.expression,e.dictionaryForm].map { ["field":$0,"term":trimmed,"contains":$0.localizedCaseInsensitiveContains(trimmed)] as [String:Any] } }
        let facts = doc.entries.flatMap { e in e.senses.flatMap { s in var p = e;p.senses = [s];return graphSearchFacts(p,query:trimmed) } }
        return ["expected":describe(graph),"document":try object(doc),"expanded":expanded.map{$0.uuidString.lowercased()}.sorted(),"query":query,"headFacts":heads,"senseFacts":facts]
    }
    if mode.hasPrefix("s-") {
        var s = VocabularyGraphSettings()
        switch mode {
        case "s-low":s.nodeSize = -1;s.linkWidth = -1;s.center = -1;s.repulsion = -1;s.linkStrength = -1;s.linkDistance = -1
        case "s-high":s.nodeSize = 9999;s.linkWidth = 9999;s.center = 9999;s.repulsion = 9999;s.linkStrength = 9999;s.linkDistance = 9999
        case "s-finite":s.nodeSize = 21.5;s.linkWidth = 2.3;s.center = 0.04;s.repulsion = 333;s.linkStrength = 0.3;s.linkDistance = 140
        case "s-nonfinite":s.nodeSize = .infinity;s.linkWidth = -.infinity;s.center = .nan;s.repulsion = .nan;s.linkStrength = .infinity;s.linkDistance = -.infinity
        case "s-label-all":s.labels = .all
        case "s-label-focused":s.labels = .focused
        default:break
        }
        return ["expected":try object(s.normalized)]
    }
    var c = VocabularyGraphCamera(), size = CGSize(width:900,height:600);let pointer = CGPoint(x:123,y:233)
    if mode == "c-screen" { return ["expected":["point":point(c.screen(CGPoint(x:10,y:-20),in:size))]] }
    if mode == "c-world" { c.scale = 2;c.offset = CGPoint(x:30,y:-40);return ["expected":["point":point(c.world(pointer,in:size))]] }
    if mode.hasPrefix("c-zoom") {
        let factor:CGFloat = switch mode { case "c-zoom-min":0.0001;case "c-zoom-max":100;case "c-zoom-zero":0;case "c-zoom-negative":-1;case "c-zoom-nan":.nan;case "c-zoom-infinite":.infinity;default:2 }
        let before = c.world(pointer,in:size);c.zoom(by:factor,at:pointer,in:size)
        return ["expected":["camera":camera(c),"before":point(before),"after":point(c.world(pointer,in:size))]]
    }
    var points:[CGPoint] = [CGPoint(x:-100,y:50),CGPoint(x:200,y:-75)]
    switch mode {
    case "c-fit-empty":points = [];c.scale = 1.3;c.offset = CGPoint(x:12,y:34)
    case "c-fit-single":points = [CGPoint(x:400,y:-50)]
    case "c-fit-reverse":points.reverse()
    case "c-fit-small":size = CGSize(width:100,height:80)
    case "c-fit-zero-width":size.width = 0
    case "c-fit-zero-height":size.height = 0
    case "c-fit-large":points = [CGPoint(x:-10000,y:-5000),CGPoint(x:10000,y:5000)]
    case "c-fit-repeated":points = [CGPoint(x:400,y:-50),CGPoint(x:400,y:-50)]
    default:break
    }
    c.fit(points,in:size);return ["expected":["camera":camera(c),"screen":points.map{point(c.screen($0,in:size))}]]
}
@main struct Main {
    static func main()throws {
        let corpus = try JSONSerialization.jsonObject(with:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1]))) as! [String:Any]
        let rows = try (corpus["cases"] as! [[String:Any]]).map { row -> [String:Any] in var value = try observe(row["id"] as! String);value["id"] = row["id"];return value }
        FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject:rows,options:[.sortedKeys]))
    }
}
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo',type=Path,required=True)
    parser.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1] / 'fixtures/parity/vocabulary-graph-v1.json')
    parser.add_argument('--capture-output',type=Path,help='Write actual observations to a NEW file')
    args = parser.parse_args();corpus = json.loads(args.fixture.read_text(encoding='utf-8'));rows = corpus['cases']
    if corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or [r.get('id') for r in rows] != list(CASES):parser.error('All 45 pinned graph value cases required')
    if any(r.get('provenanceKind') != 'source-policy-extension' or r.get('macSource') != ('Cotabby/Support/Vocabulary/VocabularyGraphProjection.swift' if r['id'].startswith('p-') else 'Cotabby/Models/Vocabulary/VocabularyGraphModels.swift') for r in rows):parser.error('Exact original graph provenance required')
    if not args.capture_output and any('expected' not in r for r in rows):parser.error('Actual source observations required')
    policy = source(args.mac_repo,'Cotabby/Support/Vocabulary/VocabularyPolicy.swift')
    facts = 'import Foundation\nfunc graphSearchFacts(_ entry:VocabularyEntry,query:String)->[[String:Any]] {\n' + between(policy,'        let terms = query.split(whereSeparator:','        return terms.allSatisfy')
    facts += 'return terms.flatMap { term in fields.map { ["field":$0,"term":String(term),"contains":$0.localizedStandardContains(term)] as [String:Any] } }\n}\n'
    with tempfile.TemporaryDirectory(prefix='lingrove-graph-values-') as temporary:
        root = Path(temporary);inputs = []
        values = sense_policy_sources(args.mac_repo) + [source(args.mac_repo,p) for p in ('Cotabby/Models/Vocabulary/VocabularyGraphModels.swift','Cotabby/Support/Vocabulary/VocabularyGraphProjection.swift')] + [facts]
        for i,value in enumerate(values):
            p = root / f'source{i}.swift';p.write_text(value,encoding='utf-8');inputs.append(str(p))
        driver = root / 'main.swift';driver.write_text(DRIVER,encoding='utf-8');binary = root / 'graph-values'
        subprocess.run(['swiftc','-swift-version','6','-O','-parse-as-library',*inputs,str(driver),'-o',str(binary)],check=True)
        actual = json.loads(subprocess.check_output([str(binary),str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x',encoding='utf-8') as output:json.dump(actual,output,ensure_ascii=False,indent=2);output.write('\n')
        print(f'Captured {len(actual)} actual Mac graph value observations');return
    compared = [{k:v for k,v in r.items() if k not in ('macSource','provenanceKind')} for r in rows]
    if actual != compared:
        bad = next((r['id'] for r,a in zip(compared,actual) if r != a),'count');raise SystemExit(f'Actual graph value mismatch: {bad}')
    print(f'Verified {len(actual)} graph value observations against complete original Mac models/projection')


if __name__ == '__main__':main()
