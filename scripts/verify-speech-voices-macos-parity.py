#!/usr/bin/env python3
"""Run complete original voice ranking/selection and observe its localized ID dependency."""
import argparse,hashlib,json,platform,subprocess,tempfile
from pathlib import Path
from macos_vocabulary_reference import BASELINE,between,source
SOURCE='Cotabby/Support/Speech/SpeechTextPolicy.swift'
TEST='CotabbyTests/Support/Speech/SpeechVoicePolicyTests.swift'
ID_HASH='18e462748c2c712e83b14559eb0188548f9e5e3883e8f12e95d7628a57878cfb'
DRIVER=r'''
import Foundation
struct Corpus:Decodable{let cases:[Row]}
struct Row:Decodable{let id:String;let language:String;let preferred:String?;let catalog:[Voice]?;let useOriginalCatalog:Bool?;let excludePreferredAndNovelty:Bool?}
struct Voice:Codable{
 let id:String,name:String,language:String;let quality:Int;let isSystemPreferred:Bool;let isNovelty:Bool
 init(_ value:SpeechVoice){id=value.id;name=value.name;language=value.language;quality=value.quality.rawValue;isSystemPreferred=value.isSystemPreferred;isNovelty=value.isNovelty}
 var value:SpeechVoice{SpeechVoice(id:id,name:name,language:language,quality:SpeechVoiceQuality(rawValue:quality)!,isSystemPreferred:isSystemPreferred,isNovelty:isNovelty)}
}
struct Comparison:Codable{let lhs:String,rhs:String;let result:Int}
struct Observation:Codable{let catalog:[Voice];let ordered:[Voice];let selected:Voice?;let comparisons:[Comparison]}
struct Result:Codable{let id:String;let expected:Observation}
@main struct Main{
 static func main()throws{
  let corpus=try JSONDecoder().decode(Corpus.self,from:Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1])))
  guard Locale.current.identifier == "en_US" else {fatalError("Voice comparison locale must be en_US")}
  var results:[Result]=[]
  for row in corpus.cases{
   var catalog=row.useOriginalCatalog == true ? OriginalVoiceTestData.catalog : row.catalog!.map(\.value)
   if row.excludePreferredAndNovelty == true{catalog=catalog.filter{!$0.isSystemPreferred && !$0.isNovelty}}
   let language=LearningLanguage(rawValue:row.language)!
   let ordered=SpeechVoicePolicy.voices(catalog,for:language).map(Voice.init)
   let selected=SpeechVoicePolicy.select(from:catalog,language:language,preferredVoiceID:row.preferred).map(Voice.init)
   let comparisons=catalog.flatMap{lhs in catalog.map{rhs in Comparison(lhs:lhs.id,rhs:rhs.id,result:lhs.id.localizedStandardCompare(rhs.id).rawValue)}}
   results.append(Result(id:row.id,expected:Observation(catalog:catalog.map(Voice.init),ordered:ordered,selected:selected,comparisons:comparisons)))
  }
  let encoder=JSONEncoder();encoder.outputFormatting=[.sortedKeys];FileHandle.standardOutput.write(try encoder.encode(results))
 }
}
'''
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--mac-repo',type=Path,required=True);p.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'fixtures/parity/speech-voices-v1.json');p.add_argument('--capture-output',type=Path)
 a=p.parse_args();c=json.loads(a.fixture.read_text());rows=c['cases'];ids=[r['id'] for r in rows]
 references=['macos-26.6.2-25G83-arm64','macos-26.6.1-25G76-x86_64']
 if c.get('defaultComparisonReference')!=references[0] or c.get('comparisonReferences')!=references:p.error('Reviewed source comparison hosts required')
 version=subprocess.check_output(['sw_vers','-productVersion'],text=True).strip();build=subprocess.check_output(['sw_vers','-buildVersion'],text=True).strip()
 reference=f'macos-{version}-{build}-{platform.machine()}'
 if reference not in references:p.error('Unrecorded Foundation comparison host: '+reference+'; capture and review original source before admitting this host')
 for row in rows:
  if any(key not in references for key in row.get('expectedByComparisonReference',{})):p.error('Unrecorded comparison expectation')
 if c.get('comparisonLocale')!='en_US' or c.get('version')!=1 or c.get('macReference')!=BASELINE or len(ids)!=len(set(ids)) or hashlib.sha256('\n'.join(sorted(ids)).encode()).hexdigest()!=ID_HASH:p.error('Pinned reviewed voice matrix required')
 if any(r.get('macSource')!=SOURCE or r.get('provenanceKind') not in ['source-policy-extension','original-test-adaptation'] for r in rows):p.error('Original voice provenance required')
 text=source(a.mac_repo,SOURCE);models=source(a.mac_repo,'Cotabby/Models/Speech/LearningSpeechModels.swift');test=source(a.mac_repo,TEST)
 start=test.index('    private static let voices = [');catalog=test[start:test.index('    @Test',start)]
 values=[source(a.mac_repo,'Cotabby/Models/Context/LanguageCatalog.swift'),source(a.mac_repo,'Cotabby/Models/LanguageLearning/LearningLanguage.swift'),
  'import Foundation\n'+between(models,'nonisolated enum SpeechVoiceQuality:','/// Distinguishes equal text'),
  'import Foundation\n'+text[text.index('nonisolated enum LocalSpeechLanguage {'):],
  'enum OriginalVoiceTestData {\n'+catalog+'\nstatic var catalog:[SpeechVoice]{voices}\n}',DRIVER]
 with tempfile.TemporaryDirectory(prefix='lingrove-speech-voices-') as directory:
  root=Path(directory);files=[]
  for i,value in enumerate(values):
   f=root/f'source{i}.swift';f.write_text(value);files.append(str(f))
  binary=root/'probe';subprocess.run(['swiftc','-swift-version','6','-O',*files,'-o',str(binary)],check=True)
  actual=json.loads(subprocess.check_output([str(binary),str(a.fixture),'-AppleLocale','en_US']))
 for row in actual:row['expected'].setdefault('selected',None)
 if a.capture_output:
  with a.capture_output.open('x',encoding='utf-8') as out:json.dump(actual,out,ensure_ascii=False,indent=2);out.write('\n')
  print(f'Captured {len(actual)} original speech voice observations');return
 if len(actual)!=len(rows):raise SystemExit('Speech voice count changed')
 for row,value in zip(rows,actual):
  expected=row.get('expectedByComparisonReference',{}).get(reference,row['expected'])
  if value!={'id':row['id'],'expected':expected}:raise SystemExit('Mac speech voice mismatch: '+row['id']+' on '+reference)
 print(f'Verified {len(rows)} original speech voice observations with observed localized comparisons on {reference}')
if __name__=='__main__':main()
