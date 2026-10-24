#!/usr/bin/env python3
"""Prepare only the exact independent speech dependency; preserve unknown local changes."""
import argparse,hashlib,subprocess
from pathlib import Path
REVISION='2a667032dc788c3a071d538e0b572e45a3f12731'
PATCH_SHA='723e6747f2f3734f3b64c669bf5b26a368e1fe798cc85878d5580547f90fae47'

def git(directory,*args,bytes=False):
 return subprocess.check_output(['git','-C',str(directory),*args],text=not bytes)

def overlay_diff(directory):
 # The original overlay records nine-digit blob IDs. Git's automatic abbreviation varies with
 # object-store size/version; pin rendering so an identical shallow Windows checkout is accepted.
 return git(directory,'diff','--binary','--abbrev=9','--no-ext-diff','--no-color','--no-renames','--diff-algorithm=myers','--src-prefix=a/','--dst-prefix=b/','--unified=3','HEAD','--',bytes=True)

def prepare(root,directory,verify_only=False):
 patch=root/'patches/llama-tts-streaming.patch'
 if (root/'native/tts/llama-revision.txt').read_text().strip()!=REVISION or hashlib.sha256(patch.read_bytes()).hexdigest()!=PATCH_SHA:
  raise SystemExit('Speech pin or reviewed streaming patch differs.')
 if not verify_only:directory.mkdir(parents=True,exist_ok=True)
 if not (directory/'.git').is_dir():
  if verify_only:raise SystemExit('Prepare the exact speech checkout before configuring CMake.')
  if any(directory.iterdir()):raise SystemExit('Uninitialized speech directory contains files; preserve them before retrying.')
  subprocess.run(['git','init',str(directory)],check=True)
  subprocess.run(['git','-C',str(directory),'remote','add','origin','https://github.com/ggml-org/llama.cpp.git'],check=True)
 if not verify_only:subprocess.run(['git','-C',str(directory),'config','core.autocrlf','false'],check=True)
 head=subprocess.run(['git','-C',str(directory),'rev-parse','--verify','HEAD'],capture_output=True,text=True)
 if head.returncode:
  if verify_only:raise SystemExit('Prepare the pinned speech revision before configuring CMake.')
  if git(directory,'ls-files','--others').strip():raise SystemExit('Uninitialized speech checkout contains files.')
  subprocess.run(['git','-C',str(directory),'fetch','--depth','1','origin',REVISION],check=True)
  subprocess.run(['git','-C',str(directory),'checkout','--detach','FETCH_HEAD'],check=True)
 if git(directory,'rev-parse','HEAD').strip()!=REVISION:raise SystemExit('Speech checkout differs from the pinned revision; preserve it before retrying.')
 if git(directory,'diff','--cached','--binary',bytes=True):raise SystemExit('Speech checkout has staged changes; preserve them before retrying.')
 if git(directory,'ls-files','--others').strip():raise SystemExit('Speech checkout contains untracked files; preserve them before retrying.')
 diff=overlay_diff(directory)
 if not diff:
  if verify_only:raise SystemExit('Prepare the exact speech streaming patch before configuring CMake.')
  subprocess.run(['git','-C',str(directory),'apply','--check',str(patch)],check=True)
  subprocess.run(['git','-C',str(directory),'apply',str(patch)],check=True)
 elif diff!=patch.read_bytes():raise SystemExit('Speech checkout changes differ from the exact reviewed streaming patch; preserve them before retrying.')
 if overlay_diff(directory)!=patch.read_bytes():raise SystemExit('Applied speech overlay differs from the exact reviewed patch.')
 print('Prepared independent speech source '+REVISION+' and exact streaming patch.')

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--source-dir',type=Path);p.add_argument('--verify-only',action='store_true');a=p.parse_args();root=Path(__file__).resolve().parents[1]
 prepare(root,(a.source_dir or root/'build/speech-native/llama.cpp').resolve(),a.verify_only)
if __name__=='__main__':main()
