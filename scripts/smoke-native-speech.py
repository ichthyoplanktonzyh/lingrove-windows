#!/usr/bin/env python3
"""Model-free checks of actual helper framing and native platform effects; no packages/audio device."""
import argparse,ctypes,json,os,struct,subprocess,sys,tempfile,time,wave
from pathlib import Path
REVISION='2a667032dc788c3a071d538e0b572e45a3f12731'
FATAL={'event':'fatal','error':{'code':'runtime_unavailable','message':'The local speech runtime could not load this model pair.'}}

def frame(command,expected,status):
 result=subprocess.run(command,capture_output=True,timeout=15)
 assert result.returncode==status,(command[1:],result.returncode,result.stderr.decode(errors='replace'))
 data=result.stdout;assert len(data)>=4
 length=struct.unpack('>I',data[:4])[0];assert 0<length<=4096 and len(data)==length+4
 assert json.loads(data[4:])==expected
 assert b'private-fixture'not in data

def wait_file(path):
 deadline=time.monotonic()+10
 while time.monotonic()<deadline:
  try:return json.loads(path.read_text())
  except(FileNotFoundError,json.JSONDecodeError):time.sleep(.01)
 raise AssertionError('Native parent observation did not publish its barrier')

def bridge(binary,record):
 child=subprocess.Popen([str(binary),'watch',str(os.getpid())],stdin=subprocess.DEVNULL,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL)
 record.write_text(json.dumps({'pid':child.pid}))
 ready=child.stdout.readline();record.with_suffix('.ready').write_text(json.dumps({'watching':ready==b'watching\r\n' or ready==b'watching\n'}))
 sys.stdin.buffer.read(1)
 os._exit(0) # Deliberately skip Python/Popen destruction; only the native watcher ends its child.

def abrupt_parent(binary,root):
 from ctypes import wintypes
 kernel=ctypes.WinDLL('kernel32',use_last_error=True)
 kernel.OpenProcess.argtypes=[wintypes.DWORD,wintypes.BOOL,wintypes.DWORD];kernel.OpenProcess.restype=wintypes.HANDLE
 kernel.WaitForSingleObject.argtypes=[wintypes.HANDLE,wintypes.DWORD];kernel.WaitForSingleObject.restype=wintypes.DWORD
 kernel.GetExitCodeProcess.argtypes=[wintypes.HANDLE,ctypes.POINTER(wintypes.DWORD)];kernel.GetExitCodeProcess.restype=wintypes.BOOL
 kernel.TerminateProcess.argtypes=[wintypes.HANDLE,wintypes.UINT];kernel.TerminateProcess.restype=wintypes.BOOL
 kernel.CloseHandle.argtypes=[wintypes.HANDLE];kernel.CloseHandle.restype=wintypes.BOOL
 record=root/'parent-child.json';held=None
 parent=subprocess.Popen([sys.executable,str(Path(__file__).resolve()),'--bridge','--platform',str(binary),'--record',str(record)],stdin=subprocess.PIPE,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
 try:
  pid=wait_file(record)['pid'];held=kernel.OpenProcess(0x00101001,False,pid);assert held,'Could not retain the exact native test child'
  assert wait_file(record.with_suffix('.ready'))['watching']
  assert kernel.WaitForSingleObject(held,0)==258
  parent.stdin.write(b'x');parent.stdin.flush();assert parent.wait(timeout=5)==0
  assert kernel.WaitForSingleObject(held,5000)==0,'Native watcher did not exit after its exact parent died'
  code=wintypes.DWORD();assert kernel.GetExitCodeProcess(held,ctypes.byref(code)) and code.value==0
 finally:
  if parent.poll()is None:parent.kill()
  parent.wait(timeout=5)
  if parent.stdin:parent.stdin.close()
  if held:
   if kernel.WaitForSingleObject(held,0)==258:kernel.TerminateProcess(held,1);kernel.WaitForSingleObject(held,5000)
   kernel.CloseHandle(held)

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--helper',type=Path);p.add_argument('--platform',type=Path,required=True);p.add_argument('--bridge',action='store_true');p.add_argument('--record',type=Path);a=p.parse_args()
 if a.bridge:bridge(a.platform,a.record);return
 if not a.helper:p.error('--helper is required outside the parent fixture')
 helper=a.helper.resolve();platform=a.platform.resolve();checks=0
 with tempfile.TemporaryDirectory(prefix='lingrove native 语音 ')as directory:
  root=Path(directory)
  frame([str(helper),'--version'],{'protocol_version':2,'runtime':'llama.cpp','revision':REVISION},0);checks+=1
  for arguments in [[],['--model'],['--unknown','private-fixture'],['--model','private-fixture','--mmproj','private-fixture','--parent-pid','1']]:
   frame([str(helper),*arguments],FATAL,1);checks+=1
  model=root/'private-fixture 模型.gguf';decoder=root/'private-fixture 解码.gguf';model.write_bytes(b'NOPE');decoder.write_bytes(b'NOPE')
  frame([str(helper),'--model',str(model),'--mmproj',str(decoder),'--parent-pid',str(os.getpid())],FATAL,1);checks+=1
  assert subprocess.check_output([str(platform),'values'],timeout=10).strip()==b'values';checks+=1
  for seconds in [0,1,30,31]:
   path=root/f'reference 声音 {seconds}.wav'
   with wave.open(str(path),'wb')as output:output.setnchannels(1);output.setsampwidth(2);output.setframerate(24000);output.writeframes(bytes(seconds*24000*2))
   actual=subprocess.check_output([str(platform),'reference',str(path)],timeout=15).strip()
   assert actual==(b'true'if 0<seconds<=30 else b'false'),(seconds,actual);checks+=1
  invalid=root/'invalid.wav';invalid.write_bytes(b'not a wave')
  assert subprocess.check_output([str(platform),'reference',str(invalid)],timeout=15).strip()==b'false';checks+=1
  result=subprocess.run([str(platform),'watch','1'],capture_output=True,timeout=10);assert result.returncode==1 and result.stdout==b'';checks+=1
  if os.name=='nt':abrupt_parent(platform,root);checks+=1
 print(f'Verified {checks} actual native speech framing/platform checks without model weights')
if __name__=='__main__':main()
