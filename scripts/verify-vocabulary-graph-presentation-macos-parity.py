#!/usr/bin/env python3
"""Run complete original graph presentation and its Codable preference with private UserDefaults."""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, between, source
from macos_graph_reference import build_graph_reference

_spec = importlib.util.spec_from_file_location('graph_solver_cases',Path(__file__).with_name('verify-vocabulary-graph-layout-macos-parity.py'))
_solver = importlib.util.module_from_spec(_spec);_spec.loader.exec_module(_solver)
MODELS = 'Cotabby/Models/Vocabulary/VocabularyGraphModels.swift'
PRESENTATION = 'Cotabby/UI/Vocabulary/VocabularyGraphPresentation.swift'
CASES = ('c-default','c-custom','c-clamp','c-partial','c-missing-node','c-wrong-root','c-bool-number','c-string-number','c-null-number',
    'c-overflow','c-unknown-label','c-case-label','c-number-label','c-null-label','c-extra','c-duplicate','c-duplicate-invalid-first',
    'c-duplicate-invalid-last','c-utf8-bom','c-utf16-be','p-default','p-valid-init','p-invalid-init','p-normalized-init','p-toggle',
    'p-toggle-other','p-reveal','p-reveal-nil','p-collapse','p-clear','p-commands','p-epochs','p-update-settings','p-restart')
DRIVER = r'''
import Foundation
func uuid(_ n:Int)->UUID { UUID(uuidString:String(format:"00000000-0000-0000-0000-%012x",n))! }
func object<T:Encodable>(_ value:T)throws->Any { try JSONSerialization.jsonObject(with:JSONEncoder().encode(value)) }
@MainActor func describe(_ p:VocabularyGraphPresentation,_ defaults:UserDefaults,_ returned:UInt64?)throws->[String:Any] {
    let data = defaults.data(forKey:VocabularyGraphPresentation.settingsKey)
    let command:String = switch p.command { case .fit:"fit";case .locate:"locate";case .zoomIn:"zoomIn";case .zoomOut:"zoomOut" }
    return ["expanded":p.expanded.map{$0.uuidString.lowercased()}.sorted(),"focused":p.focusedEntryID?.uuidString.lowercased() as Any? ?? NSNull(),
        "serial":p.commandSerial,"command":command,"settings":try object(p.settings),"hasCamera":p.hasCamera,"fitsInitial":p.fitsInitialLayout,
        "camera":["scale":p.camera.scale,"offset":[p.camera.offset.x,p.camera.offset.y]],"epoch":returned as Any? ?? NSNull(),
        "hasData":data != nil,"saved":data.flatMap{try? JSONSerialization.jsonObject(with:$0)} as Any? ?? NSNull()]
}
@main struct Main {
    @MainActor static func main()throws {
        let corpus = try JSONSerialization.jsonObject(with:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1]))) as! [String:Any]
        var rows = [[String:Any]]()
        for row in corpus["cases"] as! [[String:Any]] {
            let mode = row["id"] as! String
            let data = (row["data"] as? String).flatMap{Data(base64Encoded:$0)}
            if mode.hasPrefix("c-") {
                let decoded = data.flatMap{try? JSONDecoder().decode(VocabularyGraphSettings.self,from:$0)}
                rows.append(["id":mode,"expected":["decoded":try decoded.map{try object($0)} as Any? ?? NSNull(),
                    "normalized":try decoded.map{try object($0.normalized)} as Any? ?? NSNull()]])
                continue
            }
            let name = "lingrove-graph-parity-" + UUID().uuidString, defaults = UserDefaults(suiteName:name)!
            defaults.removePersistentDomain(forName:name)
            defer { defaults.removePersistentDomain(forName:name) }
            if let data { defaults.set(data,forKey:VocabularyGraphPresentation.settingsKey) }
            var p = VocabularyGraphPresentation(defaults:defaults), values = [try describe(p,defaults,nil)]
            for action in row["actions"] as! [[String:Any]] {
                var returned:UInt64?
                switch action["op"] as! String {
                case "toggle":p.toggle(uuid(action["entry"] as! Int))
                case "reveal":p.reveal((action["entry"] as? Int).map{.init(entryID:uuid($0),senseID:uuid(action["sense"] as! Int))})
                case "collapse":p.collapse()
                case "clear":p.clearFocus()
                case "send":
                    let command:VocabularyGraphPresentation.Command = switch action["command"] as! String { case "zoomIn":.zoomIn;case "zoomOut":.zoomOut;case "locate":.locate;default:.fit }
                    p.send(command)
                case "epoch":returned = p.nextEpoch()
                case "settings":
                    let input = Data(base64Encoded:action["data"] as! String)!
                    p.settings = try JSONDecoder().decode(VocabularyGraphSettings.self,from:input)
                case "camera":p.camera.scale = 2;p.camera.offset = .init(x:30,y:-40);p.hasCamera = true;p.fitsInitialLayout = false
                default:p = VocabularyGraphPresentation(defaults:defaults)
                }
                values.append(try describe(p,defaults,returned))
            }
            rows.append(["id":mode,"expected":values])
        }
        FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject:rows,options:[.sortedKeys]))
    }
}
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo',type=Path,required=True)
    parser.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/vocabulary-graph-presentation-v1.json')
    parser.add_argument('--capture-output',type=Path,help='Write actual observations to a NEW file')
    args = parser.parse_args();corpus = json.loads(args.fixture.read_text());rows = corpus['cases']
    if corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or [r.get('id') for r in rows] != list(CASES):parser.error('All 34 original preference/presentation cases required')
    if any(r.get('macSource') != (MODELS if r['id'].startswith('c-') else PRESENTATION) or r.get('provenanceKind') != 'source-policy-extension' for r in rows):parser.error('Exact original provenance required')
    if not args.capture_output and any('expected' not in r for r in rows):parser.error('Source observations required')
    selection = 'import Foundation\n' + between(source(args.mac_repo,'Cotabby/Models/Vocabulary/VocabularyModels.swift'),'nonisolated struct VocabularySelection:','/// The archive records')
    declarations = [selection,source(args.mac_repo,MODELS),source(args.mac_repo,'Cotabby/Services/Vocabulary/VocabularyGraphLayoutWorker.swift'),source(args.mac_repo,PRESENTATION)]
    with tempfile.TemporaryDirectory(prefix='lingrove-original-graph-presentation-') as temporary:
        binary = build_graph_reference(args.mac_repo,Path(temporary),DRIVER,declarations)
        actual = json.loads(subprocess.check_output([str(binary),str(args.fixture.resolve())]))
    if args.capture_output:
        with args.capture_output.open('x') as output:json.dump(actual,output,indent=2);output.write('\n')
        print(f'Captured {len(actual)} original graph presentation/Codable observations');return
    expected = [{'id':r['id'],'expected':r['expected']} for r in rows]
    if not _solver.same(expected,actual):
        bad = next((r['id'] for r,a in zip(expected,actual) if not _solver.same(r,a)),'count');raise SystemExit('Original graph presentation mismatch: '+bad)
    print(f'Verified {len(actual)} observations against complete original graph presentation and Codable settings')


if __name__ == '__main__':main()
