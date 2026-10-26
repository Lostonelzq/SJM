using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Sjm {
    public static class RemoteClient {
        public static string NormalizeKey(string key) { return Regex.Replace(key.Trim().ToLowerInvariant(), @"\s+", ""); }
        public static bool IsPrivate(string host) {
            IPAddress ip;
            if (!IPAddress.TryParse(host, out ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
            byte[] b = ip.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || b[0] == 127;
        }
        public static string Sign(string key, string nonce, string command) {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key)))
                return BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(nonce + "\n" + command))).Replace("-", "").ToLowerInvariant();
        }
        public static string Fingerprint(X509Certificate cert) {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(cert.GetRawCertData())).Replace("-", "").ToLowerInvariant();
        }
        private static async Task<string> Line(Stream stream) {
            var bytes = new byte[1]; var text = new StringBuilder();
            for (int i=0; i<160; i++) {
                int read = await stream.ReadAsync(bytes,0,1);
                if (read == 0) throw new IOException("连接已关闭");
                if (bytes[0] == 10) return text.ToString();
                if (bytes[0] < 32 || bytes[0] > 126) throw new IOException("响应格式不正确");
                text.Append((char)bytes[0]);
            }
            throw new IOException("响应过长");
        }
        public static async Task<string[]> Pair(string host,string code) {
            if (!IsPrivate(host)) throw new ArgumentException("请输入手机显示的局域网 IPv4 地址，不带端口");
            code=Regex.Replace(code.Trim(),@"\s+","");
            if(!Regex.IsMatch(code,"^[0-9]{6}$")) throw new ArgumentException("请输入手机上当前显示的 6 位数字配对码");
            return await TimedExchange(host,code,"PAIR",null);
        }
        public static async Task<string> Send(string host,string key,string command,string fingerprint) {
            if (!IsPrivate(host)) throw new ArgumentException("请输入手机显示的局域网 IPv4 地址，不带端口");
            key = NormalizeKey(key);
            if (!Regex.IsMatch(key, "^[0-9a-f]{24}$") || string.IsNullOrEmpty(fingerprint)) throw new ArgumentException("请先输入 6 位配对码完成配对");
            if (!Regex.IsMatch(command,"\\A(?:UP|DOWN|STATUS|FOCUS|TEXT:[A-Za-z0-9+/=]{0,80})\\z")) throw new ArgumentException("不支持的指令或测试文字过长");
            return (await TimedExchange(host,key,command,fingerprint))[0];
        }
        private static async Task<string[]> TimedExchange(string host,string key,string command,string fingerprint) {
            using (var client = new TcpClient()) {
                string stage="建立 TCP 连接";
                var operation = Exchange(client,host,key,command,fingerprint,value=>stage=value);
                if (await Task.WhenAny(operation, Task.Delay(6000)) != operation) {
                    client.Close();
                    try { await operation; } catch { }
                    throw new TimeoutException("连接超时（"+stage+"）：检查手机接收状态和局域网");
                }
                try { return await operation; }
                catch (SocketException e) {
                    if(e.SocketErrorCode==SocketError.ConnectionRefused)
                        throw new IOException("手机尚未开启接收。请在手机轻划中点击「生成配对码并开启接收」或「开始接收」，再连接。",e);
                    throw;
                }
            }
        }
        private static async Task<string[]> Exchange(TcpClient client,string host,string key,string command,string expectedFingerprint,Action<string> stage) {
            await client.ConnectAsync(host,39871);
            stage("TLS 握手");
            string peerFingerprint=null;
            using (var stream = new SslStream(client.GetStream(),false,(sender,cert,chain,errors)=> {
                if(cert==null) return false;
                peerFingerprint=Fingerprint(cert);
                return expectedFingerprint==null ? command=="PAIR" : peerFingerprint==expectedFingerprint;
            })) {
                await stream.AuthenticateAsClientAsync("SJM",null,SslProtocols.Tls12,false);
                stage("等待手机挑战");
                string hello = await Line(stream);
                if (!Regex.IsMatch(hello, "^SJM1 [0-9a-f]{32}$")) throw new IOException("对方不是轻划接收端");
                string nonce = hello.Substring(5);
                byte[] request = Encoding.ASCII.GetBytes(command + " " + (command=="PAIR"?key:Sign(key,nonce,command)) + "\n");
                await stream.WriteAsync(request,0,request.Length);
                stage("等待指令响应");
                string reply=await Line(stream);
                if(command=="PAIR") {
                    if(!Regex.IsMatch(reply,"^PAIRED [0-9a-f]{24}$")) throw new IOException(reply=="ERR PAIRING"?"配对码错误或已过期，请在手机生成新的配对码":"手机未完成配对");
                    return new string[]{reply.Substring(7),peerFingerprint};
                }
                return new string[]{reply,peerFingerprint};
            }
        }
        public static string Explain(string response) {
            switch(response) {
                case "OK": return "手机已确认";
                case "ERR AUTH": return "密钥不匹配，请重新输入手机上的配对密钥";
                case "ERR ACCESSIBILITY": return "电脑已通过认证，但手机未开启「轻划 · 上下滑动」。请开启这项无障碍服务（不是系统的无障碍菜单），再点击连接。";
                case "ERR LOCKED": return "手机已锁屏，请先解锁";
                case "ERR BUSY": return "操作过快，请稍候再按";
                case "ERR STOPPED": return "手机已停止接收";
                case "ERR CANCELLED": return "滑动被中断，请重试";
                default: return "手机未完成操作，请检查手机后重试（" + response + "）";
            }
        }
    }
}
