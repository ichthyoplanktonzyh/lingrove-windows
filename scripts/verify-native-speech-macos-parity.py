#!/usr/bin/env python3
"""Lock the complete original helper behind the explicitly enumerated platform adaptation."""
import argparse,hashlib
from pathlib import Path
from macos_vocabulary_reference import BASELINE,source
from macos_native_speech_reference import adapted_helper

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--mac-repo',required=True,type=Path);a=p.parse_args();root=Path(__file__).resolve().parents[1]
 assert (root/'native/tts/SpeechHelper.cpp').read_text()==adapted_helper(a.mac_repo),'Common speech helper differs beyond the enumerated platform seam'
 for path in ['native/tts/LLAMA_LICENSE.txt','native/tts/THIRD_PARTY_NOTICES.txt','native/tts/llama-revision.txt','patches/llama-tts-streaming.patch']:
  assert (root/path).read_bytes()==source(a.mac_repo,path).encode(),path+' differs from the original pin'
 print('Verified complete original native speech helper adaptation and exact dependency/notice bytes at '+BASELINE)
if __name__=='__main__':main()
