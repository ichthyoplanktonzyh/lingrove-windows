#!/usr/bin/env python3
"""Read CMake's actual VS project: preserve unity membership and distinct object metadata.

This diagnostic runs after configuration and before compilation. It never modifies dependency
sources, generated files, compiler inputs or artifacts; only bounded structural evidence is printed.
"""
import argparse,json,re
from pathlib import Path
import xml.etree.ElementTree as ET

OBJECT='$(IntDir)lingrove-llama-model.obj'

def name(node):return node.tag.rsplit('}',1)[-1]
def path(project,include):
 if '$('in include:raise ValueError('Unexpected unevaluated source-path macro')
 source=Path(include.replace('\\','/'))
 return (source if source.is_absolute()else project.parent/source).resolve()
def metadata(item,key,configuration):
 result=None
 for node in item:
  if name(node)!=key:continue
  condition=node.get('Condition')
  if condition:
   match=re.fullmatch(r'''\s*['"]\$\(Configuration\)\|\$\(Platform\)['"]\s*==\s*['"]([^'"]+)['"]\s*''',condition)
   if not match:raise ValueError('Unknown generated metadata condition')
   if match.group(1)!=configuration:continue
  result=(node.text or '').strip()
 return result

def check(build,source):
 build=build.resolve();source=source.resolve()
 project=build/'upstream/src/llama.vcxproj';root=ET.parse(project).getroot()
 configurations=[node.get('Include')for node in root.iter()if name(node)=='ProjectConfiguration']
 if not configurations or 'Release|x64'not in configurations:raise ValueError('Missing configured x64 Release project')
 items=[node for node in root.iter()if name(node)=='ClCompile'and node.get('Include')]
 core=(source/'src/llama.cpp').resolve();model=(source/'src/models/llama.cpp').resolve()
 def one(expected):
  matches=[node for node in items if path(project,node.get('Include'))==expected]
  if len(matches)!=1:raise ValueError('Expected exactly one source inventory item: '+expected.name)
  return matches[0]
 core_item,model_item=one(core),one(model)
 # These two source inventory records are bounded build metadata, never generated/user text.
 # Preserve the actual CMake representation in CI when a generator violates our assumptions.
 print('Visual Studio direct-source metadata: '+json.dumps({label:{'attributes':dict(item.attrib),
  'properties':[{'name':name(node),'condition':node.get('Condition'),'value':(node.text or '').strip()}for node in item]}
  for label,item in [('core',core_item),('model',model_item)]},sort_keys=True),flush=True)
 # VS 2017 15.8+ uses MSBuild native unity metadata instead of ExcludedFromBuild for
 # member inventory items. CMake's generated unity unit is still the compiler input.
 # Require the complete native contract, not merely the absence of an exclusion.
 native=(model_item.get('IncludeInUnityFile')or '').lower()=='true'
 if native:
  if not any(name(node)=='EnableUnitySupport'and(node.text or '').strip().lower()=='true'for node in root.iter()):raise ValueError('Native unity support is disabled')
  if (model_item.get('CustomUnityFile')or '').lower()!='true':raise ValueError('Model is not a custom native unity member')
  model_unity_directory=path(project,model_item.get('UnityFilesDirectory')or '')
 if (core_item.get('IncludeInUnityFile')or '').lower()=='true':raise ValueError('Core source is a native unity member')
 for configuration in configurations:
  if (metadata(core_item,'ExcludedFromBuild',configuration)or '').lower()=='true':raise ValueError('Core source was excluded from direct compilation')
  if native:
   if (metadata(model_item,'ExcludedFromBuild',configuration)or '').lower()=='true':raise ValueError('Native unity member was excluded from its build')
  elif (metadata(model_item,'ExcludedFromBuild',configuration)or '').lower()!='true':raise ValueError('Unity member is also directly compiled')
  if metadata(model_item,'ObjectFileName',configuration)!=OBJECT:raise ValueError('Missing distinct model object metadata')
  if metadata(core_item,'ObjectFileName',configuration)==OBJECT:raise ValueError('Core and model share object metadata')
 units=[]
 for item in items:
  unity=path(project,item.get('Include'))
  if not re.fullmatch(r'unity_\d+_cxx\.cxx',unity.name):continue
  includes=[path(unity,value)for value in re.findall(r'^\s*#\s*include\s+"([^"]+)"',unity.read_text(encoding='utf-8'),re.MULTILINE)]
  units.append((item,unity,includes))
 owners={}
 for configuration in configurations:
  members=[]
  for item,unity,includes in units:
   if (metadata(item,'ExcludedFromBuild',configuration)or '').lower()=='true':continue
   if (item.get('IncludeInUnityFile')or '').lower()=='true':raise ValueError('Generated unity unit is itself a native unity member')
   if native and ((item.get('IncludeInUnityFile')or '').lower()!='false'
    or(item.get('CustomUnityFile')or '').lower()!='true'
    or path(project,item.get('UnityFilesDirectory')or '')!=unity.parent):raise ValueError('Incomplete native generated unity unit metadata')
   if core in includes:raise ValueError('Core source was added to a compiled unity translation unit')
   # Count occurrences as well as files: including the model twice in one active unit is also
   # invalid, and an exclusion in Release must not hide a duplicate compiled in Debug.
   members.extend([unity]*includes.count(model))
  if len(members)!=1:raise ValueError('Expected one compiled model include for '+configuration)
  if native and members[0].parent!=model_unity_directory:raise ValueError('Native model unity directory does not own the generated unit')
  owners[configuration]=members[0].relative_to(build).as_posix()
 return {'configurations':configurations,'coreStandalone':True,'modelExcludedFromDirectBuild':True,
  'unityMode':'native'if native else 'excluded-members','modelUnityOwners':owners,'modelObjectMetadata':OBJECT}

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--build-dir',required=True,type=Path);p.add_argument('--source-dir',required=True,type=Path);a=p.parse_args()
 print('Verified actual Visual Studio speech source ownership: '+json.dumps(check(a.build_dir.resolve(),a.source_dir.resolve()),sort_keys=True))
if __name__=='__main__':main()
