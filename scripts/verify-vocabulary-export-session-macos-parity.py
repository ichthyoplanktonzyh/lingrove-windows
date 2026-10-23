#!/usr/bin/env python3
"""Execute the original UI export session with real Store/archive and controlled task ordering."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, export_session_sources

DRIVER = r'''
import Foundation
import CryptoKit
func id(_ n:Int) -> UUID { UUID(uuidString:String(format:"00000000-0000-0000-0000-%012x",n))! }
let now = Date(timeIntervalSince1970:0)
func seed() -> VocabularyDocument {
    .init(entries:[.init(id:id(1),expression:"bank",dictionaryForm:"bank",language:.english,kind:.word,
        senses:[.init(id:id(2),meaning:"core",createdAt:now,updatedAt:now),.init(id:id(4),meaning:"second",createdAt:now,updatedAt:now)],createdAt:now,updatedAt:now)])
}
final class ArchiveGate: @unchecked Sendable {
    let entered = DispatchSemaphore(value:0), release = DispatchSemaphore(value:0)
    func hold() { entered.signal(); precondition(release.wait(timeout:.now()+20) == .success) }
    func waitForEntry() -> Bool { entered.wait(timeout:.now()+10) == .success }
}
extension VocabularyArchive { func holdProbe(_ gate:ArchiveGate) { gate.hold() } }
@MainActor func until(_ condition:() -> Bool) async {
    let deadline = Date().addingTimeInterval(10)
    while !condition() { precondition(Date() < deadline); await Task.yield() }
}
@MainActor func state(_ s:VocabularyExportSession, saved:Bool?, file:URL, store:VocabularyStore) -> [String:Any] {
    var result:[String:Any] = ["scope":s.scope.rawValue,"format":s.configuration.format.rawValue,"loading":s.loading,"saving":s.saving,
        "active":s.isActive,"canExport":s.canExport,"message":s.message as Any? ?? NSNull(),"saved":saved as Any? ?? NSNull(),
        "written":FileManager.default.fileExists(atPath:file.path),"storeError":store.errorMessage as Any? ?? NSNull(),"storeEntries":store.entries.count]
    if let p = s.package { result["package"] = ["format":p.configuration.format.rawValue,"filename":p.filename,"byteCount":p.data.count,
        "sha256":SHA256.hash(data:p.data).map{String(format:"%02x",$0)}.joined(),"entryCount":p.entryCount,"senseCount":p.senseCount] }
    else { result["package"] = NSNull() }
    return result
}
@MainActor func observe(_ mode:String) async throws -> [String:Any] {
    let archive=VocabularyArchive(url:nil), store=VocabularyStore(archive:archive)
    _ = try await archive.commit(seed()); await store.load()
    let all=[VocabularySelection(entryID:id(1),senseID:id(2))], filtered=[VocabularySelection(entryID:id(1),senseID:id(4))]
    let request=VocabularyExportRequest(all:mode == "stale" ? [.init(entryID:id(1),senseID:id(99))] : all,
        filtered:filtered,selected:[],initialScope:mode == "empty" || mode == "cancelledEmpty" ? .selected : .all)
    let s=VocabularyExportSession(store:store,request:request), file=FileManager.default.temporaryDirectory.appendingPathComponent("lingrove-session-"+UUID().uuidString)
    defer { try? FileManager.default.removeItem(at:file) }
    var saved:Bool?
    if mode == "initial" { return state(s,saved:saved,file:file,store:store) }
    if mode == "filtered" { s.scope = .filtered }
    if ["closeDuringPrepare","scopeDuringPrepare","latestWins"].contains(mode) {
        let gate=ArchiveGate(), blocker=Task { await archive.holdProbe(gate) }
        let entered = await Task.detached { gate.waitForEntry() }.value
        precondition(entered)
        let first=Task { await s.prepare() }; await until { s.loading }
        if mode == "closeDuringPrepare" { s.cancel() }
        else { s.scope = .filtered }
        var second:Task<Void,Never>?
        if mode == "latestWins" { let old=s.generationProbe; second=Task { await s.prepare() }; await until { s.generationProbe != old } }
        gate.release.signal(); await blocker.value; await first.value; await second?.value
    } else if mode == "cancelledPrepare" || mode == "cancelledEmpty" {
        let (start,continuation)=AsyncStream<Void>.makeStream()
        let task=Task { for await _ in start { break }; await s.prepare() }
        task.cancel(); continuation.finish(); await task.value
    } else { await s.prepare() }
    if mode == "scopeChanged" { s.scope = .filtered }
    if mode == "configurationChanged" { s.configuration.format = .json }
    if ["wrongPackage","save","closedSave","cancelledSave"].contains(mode), let p=s.package {
        if mode == "wrongPackage" { saved=await s.save(try VocabularyExportPolicy.package(seed().entries,configuration:s.configuration),to:file) }
        else if mode == "closedSave" { s.cancel(); saved=await s.save(p,to:file) }
        else if mode == "cancelledSave" {
            let (start,continuation)=AsyncStream<Void>.makeStream()
            let task=Task { () -> Bool in for await _ in start { break }; return await s.save(p,to:file) }
            task.cancel(); continuation.finish(); saved=await task.value
        } else { saved=await s.save(p,to:file) }
    }
    return state(s,saved:saved,file:file,store:store)
}
@main struct Main {
    static func main() async throws {
        let corpus=try JSONSerialization.jsonObject(with:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1]))) as! [String:Any]
        var observations=[[String:Any]]()
        for row in corpus["cases"] as! [[String:Any]] { observations.append(["id":row["id"]!,"expected":try await observe(row["mode"] as! String)]) }
        FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject:observations,options:[.sortedKeys]))
    }
}
'''


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo',type=Path,required=True)
    parser.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/vocabulary-export-session-v1.json')
    parser.add_argument('--capture-output',type=Path,help='Write actual observations into a NEW file')
    args=parser.parse_args();corpus=json.loads(args.fixture.read_text(encoding='utf-8'));rows=corpus['cases'];ids=[r.get('id')for r in rows]
    if corpus.get('version')!=1 or corpus.get('macReference')!=BASELINE or not rows or any(not value for value in ids) or len(set(ids))!=len(ids):parser.error('Session probes require pinned version and unique IDs')
    if any(row.get('macSource')!='Cotabby/Services/Vocabulary/VocabularyExportSession.swift' or row.get('provenanceKind')!='source-policy-extension' for row in rows):parser.error('Session probes require exact provenance')
    if not args.capture_output and any('expected'not in row for row in rows):parser.error('Every session probe needs actual observations')
    with tempfile.TemporaryDirectory(prefix='lingrove-export-session-')as temporary:
        root=Path(temporary);inputs=[]
        for index,value in enumerate(export_session_sources(args.mac_repo)):
            path=root/f'source{index}.swift';path.write_text(value,encoding='utf-8');inputs.append(str(path))
        driver=root/'main.swift';driver.write_text(DRIVER,encoding='utf-8');binary=root/'export-session-parity'
        subprocess.run(['swiftc','-swift-version','6','-O','-parse-as-library','-lsqlite3','-lz',*inputs,str(driver),'-o',str(binary)],check=True)
        observations=json.loads(subprocess.check_output([str(binary),str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x',encoding='utf-8')as output:json.dump(observations,output,ensure_ascii=False,indent=2);output.write('\n')
        print(f'Captured {len(observations)} actual Mac export session observations');return
    if observations!=[{'id':row['id'],'expected':row['expected']}for row in rows]:
        bad=next((r['id']for r,o in zip(rows,observations)if o!={'id':r['id'],'expected':r['expected']}),'count')
        raise SystemExit(f'Mac export session mismatch: {bad}')
    print(f'Verified {len(rows)} scope/options/package/cancel session cases against original Mac owner')


if __name__=='__main__':main()
