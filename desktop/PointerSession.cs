using System;
using System.IO;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Sjm {
    // One authenticated TLS session, ordered clicks, coalesced moves, no replay on failure.
    sealed class PointerSession : IDisposable {
        readonly TcpClient client=new TcpClient();
        readonly object gate=new object();
        readonly System.Collections.Generic.Queue<string> clicks=new System.Collections.Generic.Queue<string>();
        string position="MOVE:5000:5000";
        volatile bool stopped;
        public volatile bool Connected;
        public void Move(int x,int y) { lock(gate) position="MOVE:"+x+":"+y; }
        public void Click(int x,int y) { lock(gate) { if(!stopped && Connected && clicks.Count<4) clicks.Enqueue("TAP:"+x+":"+y); } }
        static string Line(Stream stream) {
            var s=new StringBuilder();
            for(int i=0;i<160;i++) { int b=stream.ReadByte(); if(b==10) return s.ToString(); if(b<32 || b>126) throw new IOException("鼠标连接响应异常"); s.Append((char)b); }
            throw new IOException("鼠标响应过长");
        }
        static void Write(Stream stream,string value) { byte[] data=Encoding.ASCII.GetBytes(value+"\n"); stream.Write(data,0,data.Length); }
        public void Run(string host,string key,string pin) {
            try {
                if(!RemoteClient.IsPrivate(host) || string.IsNullOrEmpty(pin)) throw new IOException("请先完成配对");
                var connecting=client.BeginConnect(host,39871,null,null);
                using(connecting.AsyncWaitHandle) { if(!connecting.AsyncWaitHandle.WaitOne(2000)) throw new TimeoutException("鼠标连接超时"); }
                client.EndConnect(connecting); client.NoDelay=true; client.ReceiveTimeout=2000; client.SendTimeout=2000;
                using(var stream=new SslStream(client.GetStream(),false,(s,c,ch,e)=>c!=null && RemoteClient.Fingerprint(c)==pin)) {
                    stream.ReadTimeout=2000;stream.WriteTimeout=2000;
                    stream.AuthenticateAsClient("SJM",null,SslProtocols.Tls12,false);
                    string hello=Line(stream);
                    if(!Regex.IsMatch(hello,"\\ASJM1 [0-9a-f]{32}\\z")) throw new IOException("手机鼠标协议不匹配");
                    Write(stream,"MOUSE2 "+RemoteClient.Sign(key,hello.Substring(5),"MOUSE2"));
                    string response=Line(stream); if(response!="OK") throw new IOException(RemoteClient.Explain(response));
                    Connected=true;
                    string previous=null;
                    var heartbeat=System.Diagnostics.Stopwatch.StartNew();
                    while(!stopped) {
                        string command; lock(gate) command=clicks.Count>0?clicks.Dequeue():position;
                        if(stopped) break;
                        if(command.StartsWith("TAP:")) {
                            Write(stream,command); response=Line(stream);
                            if(response!="OK") throw new IOException(RemoteClient.Explain(response));
                        } else if(command!=previous) { Write(stream,command);previous=command; }
                        if(heartbeat.ElapsedMilliseconds>=250) {
                            Write(stream,"PING");response=Line(stream);
                            if(response!="OK") throw new IOException(RemoteClient.Explain(response));
                            heartbeat.Restart();
                        }
                        Thread.Sleep(8);
                    }
                }
            } catch { if(!stopped) throw; }
            finally { Dispose(); }
        }
        public void Dispose() { stopped=true; Connected=false; lock(gate) clicks.Clear(); client.Close(); }
    }
}
