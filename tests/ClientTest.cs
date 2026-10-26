using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Sjm;
class ClientTest {
    static void Require(bool condition) { if(!condition) throw new Exception("Assertion failed"); }
    static int Main(string[] args) {
        try {
            if(args.Length>0) {
                string config=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SJM","pairing.dat");
                if(args[0]=="pair") {
                    string[] pair=RemoteClient.Pair(args[1],Console.ReadLine()).GetAwaiter().GetResult();
                    Directory.CreateDirectory(Path.GetDirectoryName(config));
                    File.WriteAllBytes(config,ProtectedData.Protect(Encoding.UTF8.GetBytes(args[1]+"\n"+pair[0]+"\n"+pair[1]),null,DataProtectionScope.CurrentUser));
                    Console.WriteLine("PAIRED; encrypted credentials saved for desktop app");
                } else {
                    string[] data=Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(config),null,DataProtectionScope.CurrentUser)).Split('\n');
                    if(args[0]=="pointer") {
                        using(var pointer=new PointerSession()) {
                            int x=int.Parse(args[1]),y=int.Parse(args[2]);pointer.Move(x,y);
                            var run=Task.Run(()=>pointer.Run(data[0],data[1],data[2]));
                            var wait=System.Diagnostics.Stopwatch.StartNew();
                            while(!pointer.Connected && !run.IsCompleted && wait.ElapsedMilliseconds<4000) System.Threading.Thread.Sleep(20);
                            if(!pointer.Connected) { pointer.Dispose();run.GetAwaiter().GetResult();throw new Exception("Pointer did not connect"); }
                            Console.WriteLine("POINTER CONNECTED");
                            if(args.Length>3 && args[3]=="sweep") {
                                var sweep=System.Diagnostics.Stopwatch.StartNew();int count=0;while(sweep.ElapsedMilliseconds<4000) { pointer.Move(x+(int)(1000*Math.Sin(sweep.ElapsedMilliseconds/400.0)),y+(int)(1000*Math.Cos(sweep.ElapsedMilliseconds/400.0)));count++;System.Threading.Thread.Sleep(8); } Console.WriteLine("Generated positions="+count);
                            } else System.Threading.Thread.Sleep(4000);
                            if(args.Length>3 && args[3]=="click") pointer.Click(x,y);
                            System.Threading.Thread.Sleep(1000);pointer.Dispose();run.GetAwaiter().GetResult();
                            Console.WriteLine("POINTER CLOSED");
                        }
                        return 0;
                    }
                    Console.WriteLine(RemoteClient.Send(data[0],data[1],args[1],data[2]).GetAwaiter().GetResult());
                }
            } else Run().GetAwaiter().GetResult();
            return 0;
        } catch(Exception e) { Console.Error.WriteLine(e.ToString()); return 1; }
    }
    static async Task Run() {
        var wheel=new WheelGesture();
        Require(wheel.Feed(120,false,1000)==null);
        Require(wheel.Feed(120,true,1001)=="DOWN");
        Require(wheel.Feed(-120,true,1002)=="UP");
        Require(wheel.Feed(30,true,1003)==null);
        Require(wheel.Feed(90,true,1004)=="DOWN");
        Require(wheel.Feed(60,true,1005)==null);
        Require(wheel.Feed(-60,true,1006)==null);
        Require(wheel.Feed(-60,true,1607)==null);
        Require(wheel.Feed(-60,true,1608)=="UP");
        Console.WriteLine("PASS: Ctrl-wheel direction, normal wheel passthrough, high-resolution accumulation, direction reversal and stale remainder reset");
        const string key="0123456789abcdef01234567", nonce="abcdef0123456789abcdef0123456789";
        Require(RemoteClient.Sign(key,nonce,"UP")=="0ac2865d9e8010a216804d8223a44143c4cf2cb9e05f575f34df68786d6e107b");
        Require(!RemoteClient.IsPrivate("8.8.8.8")); Require(!RemoteClient.IsPrivate("example.com"));
        Require(RemoteClient.IsPrivate("192.168.31.12")); Require(RemoteClient.NormalizeKey("ABCD 1234")=="abcd1234");
        var listener=new TcpListener(IPAddress.Loopback,39871); listener.Start();
        var cert=new X509Certificate2(".local/test-tls.p12","test-only");
        string pin=RemoteClient.Fingerprint(cert);
        try {
            var server=Task.Run(async()=>{
                using(var c=await listener.AcceptTcpClientAsync()) using(var stream=new SslStream(c.GetStream())) {
                    await stream.AuthenticateAsServerAsync(cert,false,SslProtocols.Tls12,false);
                    var writer=new StreamWriter(stream,Encoding.ASCII) { AutoFlush=true,NewLine="\n" };
                    await writer.WriteLineAsync("SJM1 "+nonce);
                    var reader=new StreamReader(stream,Encoding.ASCII);
                    string line=await reader.ReadLineAsync();
                    Require(line=="UP "+RemoteClient.Sign(key,nonce,"UP"));
                    await writer.WriteLineAsync("OK");
                }
            });
            Require(await RemoteClient.Send("127.0.0.1",key,"UP",pin)=="OK"); await server;
            var bad=Task.Run(async()=>{
                using(var c=await listener.AcceptTcpClientAsync()) using(var s=new SslStream(c.GetStream())) {
                    await s.AuthenticateAsServerAsync(cert,false,SslProtocols.Tls12,false);
                    byte[] b=Encoding.ASCII.GetBytes("UNEXPECTED\n"); await s.WriteAsync(b,0,b.Length);
                }
            });
            try { await RemoteClient.Send("127.0.0.1",key,"UP",pin); throw new Exception("Expected rejection"); } catch(IOException) { }
            await bad;
            var pairing=Task.Run(async()=> {
                using(var c=await listener.AcceptTcpClientAsync()) using(var s=new SslStream(c.GetStream())) {
                    await s.AuthenticateAsServerAsync(cert,false,SslProtocols.Tls12,false);
                    var writer=new StreamWriter(s,Encoding.ASCII) { AutoFlush=true,NewLine="\n" };
                    await writer.WriteLineAsync("SJM1 "+nonce);
                    Require(await new StreamReader(s,Encoding.ASCII).ReadLineAsync()=="PAIR 123456");
                    await writer.WriteLineAsync("PAIRED "+key);
                }
            });
            string[] paired=await RemoteClient.Pair("127.0.0.1","123 456"); Require(paired[0]==key && paired[1]==pin); await pairing;
            var wrongPin=Task.Run(async()=> {
                using(var c=await listener.AcceptTcpClientAsync()) using(var s=new SslStream(c.GetStream())) {
                    try { await s.AuthenticateAsServerAsync(cert,false,SslProtocols.Tls12,false); } catch(Exception) { }
                }
            });
            try { await RemoteClient.Send("127.0.0.1",key,"UP",new string('0',64)); throw new Exception("Expected certificate rejection"); } catch(AuthenticationException) { }
            await wrongPin;
            using(var pointer=new PointerSession()) {
                pointer.Move(1200,3400);pointer.Click(1,1); // A click before authentication must be ignored.
                var moved=new TaskCompletionSource<bool>();
                var pointerServer=Task.Run(async()=> {
                    using(var c=await listener.AcceptTcpClientAsync()) using(var s=new SslStream(c.GetStream())) {
                        await s.AuthenticateAsServerAsync(cert,false,SslProtocols.Tls12,false);
                        var writer=new StreamWriter(s,Encoding.ASCII) { AutoFlush=true,NewLine="\n" };
                        var reader=new StreamReader(s,Encoding.ASCII);
                        await writer.WriteLineAsync("SJM1 "+nonce);
                        Require(await reader.ReadLineAsync()=="MOUSE2 "+RemoteClient.Sign(key,nonce,"MOUSE2"));
                        await writer.WriteLineAsync("OK");
                        Require(await reader.ReadLineAsync()=="MOVE:1200:3400");
                        for(int i=0;i<8;i++) { pointer.Move(1300+i,3400); Require(await reader.ReadLineAsync()=="MOVE:"+(1300+i)+":3400"); }
                        pointer.Click(9000,8000);
                        Require(await reader.ReadLineAsync()=="TAP:9000:8000");
                        await writer.WriteLineAsync("OK");moved.SetResult(true);
                        try { string frame;while((frame=await reader.ReadLineAsync())!=null) { if(frame=="PING" || frame.StartsWith("TAP:")) await writer.WriteLineAsync("OK"); } } catch(IOException) { }
                    }
                });
                var pointerClient=Task.Run(()=>pointer.Run("127.0.0.1",key,pin));
                Require(await Task.WhenAny(moved.Task,Task.Delay(5000))==moved.Task);
                pointer.Dispose();
                Require(await Task.WhenAny(pointerClient,Task.Delay(3000))==pointerClient);
                await pointerClient;await pointerServer;
            }
            Console.WriteLine("PASS: persistent pointer authentication, move/click ordering, no pre-connect click replay, release closes session");
        } finally { listener.Stop(); }
        Console.WriteLine("PASS: Java/C# HMAC, private addresses, TLS exchange, six-digit pairing, invalid peer and certificate change rejection");
    }
}
