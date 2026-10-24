#!/usr/bin/env python3
"""Exercise source preservation against isolated real Git clones, without model weights."""
import argparse,contextlib,io,subprocess,tempfile
from pathlib import Path
import importlib.util
spec=importlib.util.spec_from_file_location('speech_prepare',Path(__file__).with_name('prepare-speech-runtime.py'))
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)

def call(root,checkout):
 with contextlib.redirect_stdout(io.StringIO()):module.prepare(root,checkout)

def rejected(root,checkout,reason):
 try:call(root,checkout)
 except SystemExit as error:assert reason in str(error),(reason,str(error));return
 raise AssertionError('Unsafe speech source state was accepted')

def git(root,*arguments):
 return subprocess.check_output(['git','-C',str(root),*arguments])

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--source-dir',required=True,type=Path);a=p.parse_args();root=Path(__file__).resolve().parents[1]
 with tempfile.TemporaryDirectory(prefix='lingrove-source-guard-')as directory:
  temp=Path(directory);checkout=temp/'dependency'
  subprocess.run(['git','-c','core.autocrlf=false','clone','--no-hardlinks','--depth','1',a.source_dir.resolve().as_uri(),str(checkout)],check=True,capture_output=True)
  call(root,checkout);baseline=module.overlay_diff(checkout);assert baseline==(root/'patches/llama-tts-streaming.patch').read_bytes()
  call(root,checkout);assert module.overlay_diff(checkout)==baseline
  config=(checkout/'.git/config').read_bytes();module.prepare(root,checkout,verify_only=True);assert( checkout/'.git/config').read_bytes()==config
  extra=checkout/'unknown-fixture-file';extra.write_bytes(b'preserve this untracked input')
  rejected(root,checkout,'untracked files');assert extra.read_bytes()==b'preserve this untracked input';assert module.overlay_diff(checkout)==baseline;extra.unlink()
  license=checkout/'LICENSE';original=license.read_bytes();license.write_bytes(original+b'\nunknown user edit\n')
  rejected(root,checkout,'changes differ');assert license.read_bytes()==original+b'\nunknown user edit\n';license.write_bytes(original)
  # Stage only a disposable fixture file. Removing it restores this test clone's original
  # index without a destructive reset/checkout and without touching the product repository.
  staged=checkout/'staged-fixture';staged.write_bytes(b'preserve staged input');subprocess.run(['git','-C',str(checkout),'add','staged-fixture'],check=True)
  index=git(checkout,'diff','--cached','--binary');rejected(root,checkout,'staged changes');assert git(checkout,'diff','--cached','--binary')==index;assert staged.read_bytes()==b'preserve staged input'
  subprocess.run(['git','-C',str(checkout),'rm','--cached','staged-fixture'],check=True,capture_output=True);staged.unlink()
  subprocess.run(['git','-C',str(checkout),'-c','user.name=Fixture','-c','user.email=fixture@example.invalid','commit','--allow-empty','-m','isolated wrong revision'],check=True,capture_output=True)
  head=git(checkout,'rev-parse','HEAD');rejected(root,checkout,'pinned revision');assert git(checkout,'rev-parse','HEAD')==head;assert module.overlay_diff(checkout)==baseline
  fake=temp/'wrong-pin';(fake/'native/tts').mkdir(parents=True);(fake/'patches').mkdir();(fake/'native/tts/llama-revision.txt').write_text(module.REVISION)
  (fake/'patches/llama-tts-streaming.patch').write_bytes(b'unknown patch')
  untouched=temp/'never-created';rejected(fake,untouched,'patch differs');assert not untouched.exists()
 print('Verified 8 actual Git preparation/preservation checks without model weights')
if __name__=='__main__':main()
