#!/usr/bin/env python3
"""Run the complete pinned Mac image prepare/validate boundary over fixed shared input bytes."""
import argparse
import base64
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, sense_policy_sources, source

STORAGE = 'Cotabby/Services/Vocabulary/VocabularyImageStorage.swift'
OPERATIONS = ['small', 'landscape', 'portrait', 'odd-ratio', 'thin', 'too-wide', 'empty', 'junk', 'input-budget', 'pixel-budget']
DRIVER = r'''
import Foundation
import ImageIO
struct Row: Decodable { let id: String; let operation: String; let input: String }
struct Corpus: Decodable { let cases: [Row] }
@main struct Main {
 static func main() throws {
  let corpus = try JSONDecoder().decode(Corpus.self,from:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1])))
  var output: [[String:Any]]=[]
  for row in corpus.cases {
   let input = row.operation == "input-budget" ? Data(repeating: 1, count: 24*1024*1024+1) : Data(base64Encoded:row.input)!
   var result:[String:Any]
   do {
    let prepared = try VocabularyImageStorage.prepare(input)
    try VocabularyImageStorage.validate(prepared.data,reference:prepared.reference)
    result=["valid":true,"width":prepared.reference.width,"height":prepared.reference.height,
      "matchingCount":prepared.data.count == prepared.reference.byteCount,
      "matchingDigest":VocabularyImageStorage.digest(prepared.data) == prepared.reference.digest,
      "withinByteBudget":prepared.data.count <= VocabularyContentPolicy.maximumImageBytes,
      "png":prepared.data.prefix(8) == Data([137,80,78,71,13,10,26,10])]
   } catch VocabularyError.invalidContent { result=["valid":false,"error":"invalidContent"] }
   catch { fatalError("Unexpected image failure: \(error)") }
   output.append(["id":row.id,"expected":result])
  }
  FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject:output,options:[.sortedKeys]))
 }
}
'''

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo',type=Path,required=True)
    parser.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/vocabulary-image-preparation-v1.json')
    parser.add_argument('--capture-output',type=Path)
    args=parser.parse_args();corpus=json.loads(args.fixture.read_text());rows=corpus['cases']
    if corpus.get('version')!=1 or corpus.get('macReference')!=BASELINE or [r.get('id') for r in rows]!=OPERATIONS or [r.get('operation') for r in rows]!=OPERATIONS:
        parser.error('All ten original image-preparation probes are required')
    if any(r.get('macSource')!=STORAGE or r.get('provenanceKind')!='source-policy-extension' for r in rows):parser.error('Original image source provenance is required')
    with tempfile.TemporaryDirectory(prefix='lingrove-image-preparation-') as temporary:
        root=Path(temporary);inputs=[]
        for index,value in enumerate(sense_policy_sources(args.mac_repo)+[source(args.mac_repo,STORAGE)]):
            path=root/f'source{index}.swift';path.write_text(value);inputs.append(str(path))
        driver=root/'main.swift';driver.write_text(DRIVER);binary=root/'probe'
        subprocess.run(['swiftc','-O','-parse-as-library',*inputs,str(driver),'-o',str(binary)],check=True)
        observed=json.loads(subprocess.check_output([str(binary),str(args.fixture)]))
    if args.capture_output:
        with args.capture_output.open('x') as target:json.dump(observed,target,ensure_ascii=False,indent=2);target.write('\n')
        print(f'Captured {len(observed)} original ImageIO preparation observations');return
    if observed!=[{'id':r['id'],'expected':r['expected']} for r in rows]:raise SystemExit('Original ImageIO preparation mismatch')
    print(f'Verified {len(observed)} cases against the complete original Mac image prepare/validate boundary')

if __name__=='__main__':main()
