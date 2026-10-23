#!/usr/bin/env python3
"""Execute the complete pinned Mac media owner on fixed bytes in isolated temporary directories."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, sense_policy_sources, source

STORAGE = 'Cotabby/Services/Vocabulary/VocabularyImageStorage.swift'
OPERATIONS = ['memory-roundtrip', 'memory-missing', 'disk-inert', 'disk-roundtrip', 'disk-repair',
              'disk-remove', 'disk-invalid-bytes', 'disk-metadata', 'disk-directory-leaf',
              'disk-root-link', 'disk-leaf-link', 'disk-oversize']
DRIVER = r'''
import Foundation
struct Row: Decodable { let id: String; let operation: String }
struct Corpus: Decodable { let input: String; let cases: [Row] }
@main struct Main {
 static func main() throws {
  let corpus = try JSONDecoder().decode(Corpus.self,from:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1])))
  let data = Data(base64Encoded:corpus.input)!
  let image = VocabularyImageReference(digest:VocabularyImageStorage.digest(data),width:80,height:40,byteCount:data.count)
  try VocabularyImageStorage.validate(data,reference:image)
  let fm=FileManager.default
  let temporary=fm.temporaryDirectory.appendingPathComponent("lingrove-media-probe-"+UUID().uuidString)
  try fm.createDirectory(at:temporary,withIntermediateDirectories:true,attributes:[.posixPermissions:0o700])
  defer { try? fm.removeItem(at:temporary) }
  var output:[[String:Any]]=[]
  for row in corpus.cases {
   let parent=temporary.appendingPathComponent(row.id)
   try fm.createDirectory(at:parent,withIntermediateDirectories:true,attributes:[.posixPermissions:0o700])
   let notebook=parent.appendingPathComponent("notebook.sqlite")
   let root=parent.appendingPathComponent("notebook.sqlite.media")
   let leaf=root.appendingPathComponent(image.filename)
   let memory=row.operation.hasPrefix("memory-")
   var store=VocabularyImageStorage(notebook:memory ? nil : notebook)
   var result:[String:Any]=[:]
   func available(_ owner:VocabularyImageStorage,_ reference:VocabularyImageReference) -> Bool { (try? owner.read(reference)) != nil }
   switch row.operation {
   case "memory-roundtrip", "disk-roundtrip":
    result["created"]=try store.write(data,image:image)
    result["reused"]=try store.write(data,image:image) == false
    result["exact"]=try store.read(image) == data
    if memory {
     var detached=try store.read(image);detached[0]=0
     result["detached"]=try store.read(image) == data
     store.remove(image);result["removed"] = !available(store,image)
    } else {
     let reopened=VocabularyImageStorage(notebook:notebook)
     result["reopened"]=try reopened.read(image) == data
     result["files"]=try fm.contentsOfDirectory(atPath:root.path).count
    }
   case "memory-missing": result["available"]=available(store,image)
   case "disk-inert":
    result["before"]=fm.fileExists(atPath:root.path)
    result["available"]=available(store,image)
    result["after"]=fm.fileExists(atPath:root.path)
   case "disk-repair":
    _=try store.write(data,image:image);try Data([1,2,3]).write(to:leaf)
    result["before"]=available(store,image)
    result["created"]=try store.write(data,image:image)
    result["exact"]=try store.read(image) == data
    result["files"]=try fm.contentsOfDirectory(atPath:root.path).count
   case "disk-remove":
    _=try store.write(data,image:image);store.remove(image);store.remove(image)
    result["available"]=available(store,image);result["exists"]=fm.fileExists(atPath:leaf.path)
   case "disk-invalid-bytes":
    var wrong=data;wrong[0]=0
    do { _=try store.write(wrong,image:image);result["accepted"]=true } catch { result["accepted"]=false }
    result["root"]=fm.fileExists(atPath:root.path)
   case "disk-metadata":
    _=try store.write(data,image:image)
    let wrong=VocabularyImageReference(digest:image.digest,width:image.width+1,height:image.height,byteCount:image.byteCount)
    result["available"]=available(store,wrong)
    do { _=try store.write(data,image:wrong);result["accepted"]=true } catch { result["accepted"]=false }
    result["exact"]=try store.read(image) == data
   case "disk-directory-leaf":
    try fm.createDirectory(at:leaf,withIntermediateDirectories:true,attributes:[.posixPermissions:0o700])
    let marker=leaf.appendingPathComponent("marker");try Data([7]).write(to:marker)
    do { _=try store.write(data,image:image);result["accepted"]=true } catch { result["accepted"]=false }
    result["preserved"]=try Data(contentsOf:marker) == Data([7])
   case "disk-root-link":
    let target=parent.appendingPathComponent("target");try fm.createDirectory(at:target,withIntermediateDirectories:true,attributes:[.posixPermissions:0o700])
    try fm.createSymbolicLink(at:root,withDestinationURL:target)
    do { _=try store.write(data,image:image);result["accepted"]=true } catch { result["accepted"]=false }
    result["files"]=try fm.contentsOfDirectory(atPath:target.path).count
   case "disk-leaf-link":
    try fm.createDirectory(at:root,withIntermediateDirectories:true,attributes:[.posixPermissions:0o700])
    let target=parent.appendingPathComponent("target");try Data([7]).write(to:target)
    try fm.createSymbolicLink(at:leaf,withDestinationURL:target)
    do { _=try store.write(data,image:image);result["accepted"]=true } catch { result["accepted"]=false }
    result["preserved"]=try Data(contentsOf:target) == Data([7])
   case "disk-oversize":
    _=try store.write(data,image:image)
    try Data(repeating:1,count:VocabularyContentPolicy.maximumImageBytes+1).write(to:leaf)
    result["available"]=available(store,image)
   default: fatalError("Unknown media probe")
   }
   output.append(["id":row.id,"expected":result])
  }
  FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject:output,options:[.sortedKeys]))
 }
}
'''


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo',type=Path,required=True)
    parser.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/vocabulary-image-storage-v1.json')
    parser.add_argument('--capture-output',type=Path)
    args=parser.parse_args();corpus=json.loads(args.fixture.read_text());rows=corpus['cases']
    if corpus.get('version')!=1 or corpus.get('macReference')!=BASELINE or [r.get('id') for r in rows]!=OPERATIONS or [r.get('operation') for r in rows]!=OPERATIONS:
        parser.error('All twelve original media-owner probes are required')
    if any(r.get('macSource')!=STORAGE or r.get('provenanceKind')!='source-policy-extension' for r in rows):parser.error('Original media source provenance is required')
    with tempfile.TemporaryDirectory(prefix='lingrove-image-storage-') as temporary:
        root=Path(temporary);inputs=[]
        for index,value in enumerate(sense_policy_sources(args.mac_repo)+[source(args.mac_repo,STORAGE)]):
            path=root/f'source{index}.swift';path.write_text(value);inputs.append(str(path))
        driver=root/'main.swift';driver.write_text(DRIVER);binary=root/'probe'
        subprocess.run(['swiftc','-O','-parse-as-library',*inputs,str(driver),'-o',str(binary)],check=True)
        observed=json.loads(subprocess.check_output([str(binary),str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x') as target:json.dump(observed,target,ensure_ascii=False,indent=2);target.write('\n')
        print(f'Captured {len(observed)} original media owner observations');return
    if observed!=[{'id':r['id'],'expected':r['expected']} for r in rows]:raise SystemExit('Original media owner mismatch')
    print(f'Verified {len(observed)} cases against the complete original Mac media owner')

if __name__=='__main__':main()
