"""Fixture-only loopback effects for unchanged URLSession speech HTTP source observations."""
import gzip,http.server,json,threading,zlib
class SpeechWireProbe:
 def __init__(self,rows):
  self.rows={row['id']:row for row in rows if row.get('wire')};self.requests={};self.lock=threading.Lock();owner=self
  class Handler(http.server.BaseHTTPRequestHandler):
   def setup(self):super().setup();self.connection.settimeout(5)
   def do_POST(self):
    identity=self.path.split('/')[1];row=owner.rows[identity];length=int(self.headers['Content-Length']);assert 0<=length<=4096
    body=self.rfile.read(length)
    record={'url':'$LOOPBACK'+self.path,'method':self.command,'authorization':self.headers.get('Authorization',''),'accept':self.headers.get('Accept',''),'acceptEncoding':self.headers.get('Accept-Encoding',''),'contentType':self.headers.get('Content-Type',''),'body':json.loads(body)}
    with owner.lock:owner.requests[identity]=record
    data=bytes([90])*row['decodedBytes'];encoding=row['encoding']
    if encoding=='gzip':data=gzip.compress(data,mtime=0)
    elif encoding=='deflate':data=zlib.compress(data)
    else:compressor=zlib.compressobj(wbits=-15);data=compressor.compress(data)+compressor.flush()
    self.send_response(200);self.send_header('Content-Type','audio/wav');self.send_header('Content-Encoding','deflate'if encoding=='raw-deflate'else encoding);self.send_header('Content-Length',str(row.get('declared',len(data))));self.end_headers()
    try:self.wfile.write(data)
    except(BrokenPipeError,ConnectionResetError):pass
   def log_message(self,*args):pass
  self.server=http.server.ThreadingHTTPServer(('127.0.0.1',0),Handler)
  self.thread=threading.Thread(target=lambda:self.server.serve_forever(poll_interval=.05));self.thread.start()
  self.url='http://127.0.0.1:'+str(self.server.server_port)
 def __enter__(self):return self
 def __exit__(self,*args):self.server.shutdown();self.server.server_close();self.thread.join(timeout=5);assert not self.thread.is_alive()
