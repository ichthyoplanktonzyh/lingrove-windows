#!/usr/bin/env python3
"""Current stable editor annotation/range behavior, with an explicit per-module reference upgrade."""
import argparse,collections,hashlib,importlib.util,json,re,subprocess,tempfile
from pathlib import Path
from macos_vocabulary_reference import BASELINE,source,sense_policy_sources
CURRENT='cc468b89f5c360cc7210fac4f1a8b52197870bf2'
POLICY='Cotabby/Support/Vocabulary/VocabularyPolicy.swift'
EDITOR='Cotabby/UI/Vocabulary/VocabularyEntryEditor.swift'
TEST='CotabbyTests/Support/Vocabulary/VocabularyPolicyTests.swift:reconciledRangesReanchorExpressionAfterExampleTextEdit'
ID_HASH='cbfc4567ef7f19dfca03b72011488a4314d81c5f026afae7c4fa383a0290e608'

def editor_wrapper(repo):
 editor=source(repo,EDITOR,CURRENT)
 begin='                                    example.ranges = VocabularyPolicy.reconciledRanges('
 assert editor.count(begin)==1
 start=editor.index(begin);end=editor.index('\n                                    )',start)+len('\n                                    )');change=editor[start:end]
 begin='                                for index in draft.senses[sense].examples.indices {'
 assert editor.count(begin)==1
 start=editor.index(begin);end=editor.index('\n                            }\n                        TextField(String(localized: "Dictionary form"',start)
 expression=editor[start:end]
 return '''import Foundation
struct CurrentEditorReference {
 var draft:VocabularyEntry
 var senseID:UUID
 func change(_ original:VocabularyExample,to updatedText:String)->VocabularyExample{
  var example=original;example.text=updatedText
  // This is SwiftUI onChange's equality scheduling guard, not a replacement callback body.
  if original.text != updatedText {
'''+change+'''
  };return example
 }
 mutating func expression(_ updatedExpression:String){
  let before=draft.expression;draft.expression=updatedExpression
  guard before != updatedExpression,let sense=draft.senses.firstIndex(where:{$0.id==senseID})else{return}
'''+expression+'''
 }
}
'''

spec=importlib.util.spec_from_file_location('stable_editor_projection_seed',Path(__file__).with_name('verify-vocabulary-projection-macos-parity.py'))
builder=importlib.util.module_from_spec(spec);spec.loader.exec_module(builder)
DRIVER=builder.DRIVER[:builder.DRIVER.index('func describe(')]+r'''
func object(_ example:VocabularyExample)throws->Any{try JSONSerialization.jsonObject(with:JSONEncoder().encode(example))}
func observe(_ row:[String:Any])throws->[String:Any]{
 var entry=document().entries[0]
 let operation=row["operation"]as! String
 if operation=="ranges"{
  let text=row["text"]as! String,ranges=VocabularyPolicy.reconciledRanges(in:text,expression:row["expression"]as! String,dictionaryForm:row["form"]as! String)
  return ["ranges":ranges.map{["location":$0.location,"length":$0.length,"text":(text as NSString).substring(with:$0.nsRange)]}]
 }
 if operation=="valid"{return ["valid":VocabularyPolicy.valid(NSRange(location:row["location"]as! Int,length:row["length"]as! Int),in:row["text"]as! String)]}
 entry.expression=row["expression"]as? String ?? entry.expression;entry.dictionaryForm=row["form"]as? String ?? entry.dictionaryForm
 if operation=="change"{
  var example=entry.senses[0].examples[0];example.text=row["before"]as? String ?? example.text
  example.translationLanguage = .chinese;example.translationVariants=[.init(text:"variant",language:.french)]
  example.meaningLanguage = .english;example.meaningOrigin = .user;example.appBundleIdentifier="source.application"
  return ["example":try object(CurrentEditorReference(draft:entry,senseID:id(2)).change(example,to:row["text"]as! String))]
 }
 // Expression edits act on the selected sense only. The second sense's valid but different
 // highlight is intentionally retained so a whole-entry recompute cannot satisfy this transcript.
 entry.expression="bank"
 entry.senses[0].examples.append(.init(id:id(4),text:"river bank",translation:"second",ranges:[.init(NSRange(location:6,length:4))],completeness:.fragment,meaning:"second meaning",createdAt:now))
 entry.senses.append(.init(id:id(5),meaning:"other",examples:[.init(id:id(6),text:"river bank",translation:"other",ranges:[.init(NSRange(location:0,length:5))],completeness:.sentence,meaning:"other meaning",createdAt:now)],createdAt:now,updatedAt:now))
 var reference=CurrentEditorReference(draft:entry,senseID:id(2));reference.expression(row["expression"]as! String)
 return ["expression":reference.draft.expression,"form":reference.draft.dictionaryForm,"senses":try reference.draft.senses.map{["id":$0.id.uuidString.lowercased(),"examples":try $0.examples.map(object)]}]
}
let corpus=try JSONSerialization.jsonObject(with:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1])))as! [String:Any]
guard Locale.current.identifier=="en_US"else{fatalError("Current editor reference must use en_US")}
let actual=try(corpus["cases"]as! [[String:Any]]).map{["id":$0["id"]!,"expected":try observe($0)]}
FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject:actual,options:[.sortedKeys]))
'''

def rows(corpus,repo):
 assert corpus['version']==2 and corpus['macReference']==CURRENT and corpus['previousMacReference']==BASELINE and corpus['referenceLocale']=='en_US'
 values=corpus['cases'];ids=[r['id']for r in values]
 assert len(ids)==len(set(ids))==182 and hashlib.sha256('\n'.join(sorted(ids)).encode()).hexdigest()==ID_HASH
 assert collections.Counter(r['operation']for r in values)=={'ranges':155,'valid':14,'change':8,'expression':5}
 assert collections.Counter(r['provenanceKind']for r in values)=={'original-test-adaptation':1,'source-policy-extension':181}
 for row in values:
  assert row['macSource']==(POLICY if row['operation']in ('ranges','valid')else EDITOR)
  if row['provenanceKind']=='original-test-adaptation':
   assert row['id']=='range-00'and row['sourceTest']==TEST
   path,method=TEST.split(':');assert re.search(r'func\s+'+method+r'\s*\(',source(repo,path,CURRENT))
  else:assert not row.get('sourceTest')
 return values

def verify_text_data():
 root=Path(__file__).resolve().parents[1];resource=root/'src/Cotabby.Core/Resources/Foundation'
 data=json.loads((resource/'editor-text-data-v1.json').read_text())
 assert hashlib.sha256((resource/'editor-text-data-v1.json').read_bytes()).hexdigest()=='f74ae78a4ec4439e54c7e10776299e7a53aa98820548c8f15db788a7687a07a6'
 assert data['version']==1 and data['macReference']==CURRENT and data['captureLocale']=='en_US'
 assert data['generatorSha256']==hashlib.sha256((root/'scripts/capture-editor-foundation-data.swift').read_bytes()).hexdigest()
 assert data['unicodeSubsetSha256']==hashlib.sha256((resource/'DerivedCoreProperties-17.0.0.InCB.txt').read_bytes()).hexdigest()
 assert data['coreFoundationSource']=='883ec6b01e4bd18d8356cc3873acaf3c2feaa050'
 assert hashlib.sha256((resource/'CFUniChar.reference.h').read_bytes()).hexdigest()=='907c9eff7ec49f3c74ced5e5d39fcde42a076380926f16731b72159d44e8aacd'

def main():
 verify_text_data()
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--mac-repo',required=True,type=Path);p.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/vocabulary-editor-current-v2.json');p.add_argument('--capture-output',type=Path);a=p.parse_args();cases=rows(json.loads(a.fixture.read_text()),a.mac_repo)
 with tempfile.TemporaryDirectory(prefix='lingrove-current-editor-')as directory:
  root=Path(directory);files=[]
  for i,value in enumerate(sense_policy_sources(a.mac_repo,CURRENT)+[editor_wrapper(a.mac_repo)]):
   path=root/f'source{i}.swift';path.write_text(value);files.append(str(path))
  driver=root/'main.swift';driver.write_text(DRIVER);binary=root/'probe';subprocess.run(['swiftc','-swift-version','6','-O',*files,str(driver),'-o',str(binary)],check=True)
  actual=json.loads(subprocess.check_output([str(binary),str(a.fixture),'-AppleLocale','en_US'],timeout=60))
 assert len(actual)==len(cases) and [r['id']for r in actual]==[r['id']for r in cases]
 if a.capture_output:
  with a.capture_output.open('x',encoding='utf-8')as output:json.dump(actual,output,ensure_ascii=False,indent=2);output.write('\n')
  print(f'Captured {len(actual)} current stable editor observations');return
 for row,value in zip(cases,actual):
  if value!={'id':row['id'],'expected':row['expected']}:raise SystemExit('Current editor mismatch: '+row['id'])
 print(f'Verified {len(actual)} current stable editor observations')
if __name__=='__main__':main()
