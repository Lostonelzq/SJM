using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Sjm {
    static class Program {
        internal static void Log(string action,Exception error=null) {
            try {
                string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SJM");
                Directory.CreateDirectory(folder);
                string file=Path.Combine(folder,"diagnostics.log");
                if(File.Exists(file) && new FileInfo(file).Length>262144) File.Move(file,file+"."+DateTime.UtcNow.Ticks);
                File.AppendAllText(file,DateTime.Now.ToString("o")+" "+action+(error==null?"":" "+error.GetType().FullName+"\n"+error.StackTrace)+Environment.NewLine);
            } catch { }
        }
        [STAThread] static void Main() {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            ControlForm form=null;
            Application.ThreadException+=(s,e)=> { Log("UI exception",e.Exception); if(form!=null && !form.IsDisposed) form.ReportFailure(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException+=(s,e)=>Log("Unhandled exception",e.ExceptionObject as Exception);
            AppDomain.CurrentDomain.ProcessExit+=(s,e)=>Log("Process exit");
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Log("Start desktop 1.0.8");
            try { form=new ControlForm(); Application.Run(form); }
            catch(Exception ex) { Log("Fatal startup/run exception",ex); MessageBox.Show("轻划启动失败，诊断记录已保存到本机。", "轻划"); }
        }
    }
    public sealed class ControlForm : Form {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h,int id);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", SetLastError=true)] static extern IntPtr SetWindowsHookEx(int id,MouseProc proc,IntPtr module,uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr w,IntPtr l);
        [DllImport("kernel32.dll", CharSet=CharSet.Auto)] static extern IntPtr GetModuleHandle(string module);
        delegate IntPtr MouseProc(int code,IntPtr w,IntPtr l);
        [StructLayout(LayoutKind.Sequential)] struct MouseData { public Point pt; public uint mouseData; public uint flags,time; public UIntPtr extra; }
        TextBox host = new TextBox(), key = new TextBox();
        Label status = new Label();
        Button connect, next, previous, pause;
        CheckBox mouse;
        bool armed, busy, closing, ready;
        bool userPaused;
        readonly Timer healthTimer = new Timer { Interval=5000 };
        readonly Timer pointerTimer = new Timer { Interval=16 };
        PointerSession pointerSession;
        Task pointerTask;
        bool altFailed,leftCaptured;
        string pairedKey, fingerprint;
        IntPtr hook;
        readonly MouseProc mouseProc;
        readonly WheelGesture wheelGesture = new WheelGesture();
        DateTime lastAction = DateTime.MinValue;
        readonly Color ink = Color.FromArgb(24,47,43), green = Color.FromArgb(18,107,91);
        readonly string config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SJM","pairing.dat");
        public ControlForm() : this(null) { }
        internal ControlForm(string configPath) {
            mouseProc=OnMouse;
            if(configPath!=null) config=configPath;
            Text="轻划 · 电脑遥控器"; ClientSize=new Size(550,820); MinimumSize=new Size(540,700);
            AutoScaleMode=AutoScaleMode.Dpi; Font=new Font("Microsoft YaHei UI",10); BackColor=Color.FromArgb(245,248,246); ForeColor=ink;
            StartPosition=FormStartPosition.CenterScreen;
            var root = new TableLayoutPanel { Dock=DockStyle.Fill, Padding=new Padding(28), ColumnCount=1, AutoScroll=true };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); Controls.Add(root);
            Add(root,new Label { Text="轻划", Font=new Font(Font.FontFamily,28,FontStyle.Bold), AutoSize=true, Margin=new Padding(0,0,0,5) });
            Add(root,Label("手放桌上，视频接着看。",12));
            Add(root,Label("手机地址",10)); host.Text=""; host.AccessibleName="手机局域网地址"; host.Height=34; Add(root,host);
            Add(root,Label("手机轻划里显示的 IP，不是无线调试地址或端口。",9));
            Add(root,Label("6 位配对码",10)); key.MaxLength=7; key.AccessibleName="手机六位配对码"; Add(root,key);
            connect=Button("连接手机",async()=> { if(HasPairing()) CancelPairing(); else await Connect(); }); Add(root,connect);
            status.Text="先在手机开启无障碍并点击「开始接收」。"; status.AutoSize=true; status.MaximumSize=new Size(455,0); status.Padding=new Padding(0,10,0,10); Add(root,status);
            var row=new TableLayoutPanel { ColumnCount=2,Height=60,Dock=DockStyle.Top,Margin=new Padding(0) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            next=Button("↑ 上滑  Ctrl+↓",async()=>await Swipe("UP")); previous=Button("↓ 下滑  Ctrl+↑",async()=>await Swipe("DOWN"));
            row.Controls.Add(next,0,0); row.Controls.Add(previous,1,0); Add(root,row);
            pause=Button("暂停控制",async()=> { if(!ready) await Connect(); else { userPaused=!armed; SetArmed(!armed); } }); Add(root,pause);
            mouse=new CheckBox { Text="Ctrl + 鼠标滚轮：向下滚上滑，向上滚下滑",Checked=true,AutoSize=true,Margin=new Padding(0,10,0,10) };
            mouse.CheckedChanged+=(s,e)=>RefreshMouseHook(); Add(root,mouse);
            Add(root,Label("Ctrl + 空格也可上滑，窗口最小化后仍有效。\n暂停控制会释放快捷键及 Ctrl + 滚轮。\n启用时会占用 Ctrl + 空格和浏览器 Ctrl + 滚轮缩放。",9));
            Add(root,Label("按住 Alt：手机指针跟随鼠标，左键点击手机。\n松开 Alt：恢复电脑点击。暂不支持拖拽。",9));
            host.TextChanged+=(s,e)=> { ready=false; SetArmed(false); }; key.TextChanged+=(s,e)=> { ready=false; SetArmed(false); };
            LoadPairing(); next.Enabled=previous.Enabled=false; UpdatePairingUi();
            healthTimer.Tick+=async(s,e)=>await CheckConnection();
            pointerTimer.Tick+=(s,e)=>PointerTick();
            Shown+=async(s,e)=> { healthTimer.Start(); pointerTimer.Start(); if(HasPairing()) await Connect(); };
            FormClosing+=(s,e)=>Program.Log("Window closing: "+e.CloseReason);
        }
        protected override void Dispose(bool disposing) {
            if(disposing && !closing) {
                closing=true; armed=false; healthTimer.Stop(); healthTimer.Dispose();
                pointerTimer.Stop();pointerTimer.Dispose();StopPointer();
                if(IsHandleCreated) { UnregisterHotKey(Handle,1); UnregisterHotKey(Handle,2); UnregisterHotKey(Handle,3); }
                if(hook!=IntPtr.Zero) { UnhookWindowsHookEx(hook); hook=IntPtr.Zero; }
            }
            base.Dispose(disposing);
            GC.KeepAlive(mouseProc);
        }
        void StopPointer() { if(pointerSession!=null) { pointerSession.Dispose();pointerSession=null; } }
        Point PhonePoint(Point position) {
            Rectangle bounds=Screen.FromPoint(position).Bounds;
            return new Point(ScalePointer(position.X-bounds.Left,bounds.Width),ScalePointer(position.Y-bounds.Top,bounds.Height));
        }
        static int ScalePointer(int coordinate,int size) {
            double normalized=coordinate*10000.0/Math.Max(1,size-1);
            return Math.Max(0,Math.Min(10000,(int)Math.Round(5000+(normalized-5000)*1.5)));
        }
        void PointerTick() {
            bool alt=(GetAsyncKeyState((int)Keys.Menu)&0x8000)!=0;
            if(closing || !armed || !alt) { StopPointer(); if(!alt) altFailed=false; return; }
            if(pointerTask!=null && pointerTask.IsCompleted && pointerTask.IsFaulted) {
                Program.Log("Pointer session failed",pointerTask.Exception.GetBaseException());
                status.Text="鼠标连接失败，请松开 Alt 后重试。";StopPointer();pointerTask=null;altFailed=true;
            }
            if(altFailed) return;
            if(pointerSession==null) {
                if(pointerTask!=null && !pointerTask.IsCompleted) return;
                var session=new PointerSession();pointerSession=session;
                string address=host.Text.Trim(),secret=pairedKey,pin=fingerprint;
                pointerTask=Task.Run(()=>session.Run(address,secret,pin));
            }
            Point p=PhonePoint(Cursor.Position);pointerSession.Move(p.X,p.Y);
        }
        internal void ReportFailure(Exception error) {
            if(closing || IsDisposed) return;
            try { ready=false; userPaused=true; SetArmed(false); status.Text="控制发生异常，已暂停。可点击重新连接；诊断记录已保存在本机。"; }
            catch(Exception nested) { Program.Log("Error reporting failure",nested); }
        }
        void QueueSwipe(string direction) {
            if(closing || IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(new Action(async()=> {
                if(closing || IsDisposed) return;
                try { await Swipe(direction); }
                catch(Exception ex) { Program.Log("Shortcut exception",ex); ReportFailure(ex); }
            })); }
            catch(InvalidOperationException ex) { if(!closing) { Program.Log("Shortcut dispatch exception",ex); ReportFailure(ex); } }
        }
        Label Label(string text,int size) { return new Label { Text=text,Font=new Font(Font.FontFamily,size),AutoSize=true,MaximumSize=new Size(455,0),Margin=new Padding(0,6,0,6) }; }
        Button Button(string text,Func<Task> action) {
            var b=new Button { Text=text,Height=46,Dock=DockStyle.Top,FlatStyle=FlatStyle.Flat,BackColor=green,ForeColor=Color.White,Margin=new Padding(0,4,6,4),Cursor=Cursors.Hand };
            b.FlatAppearance.BorderSize=0;
            b.Click+=async(s,e)=> { try { await action(); } catch(Exception ex) { status.Text=ex.Message; } }; return b;
        }
        void Add(TableLayoutPanel parent,Control control) { control.Dock=DockStyle.Top; parent.RowCount++; parent.RowStyles.Add(new RowStyle(SizeType.AutoSize)); parent.Controls.Add(control,0,parent.RowCount-1); }
        bool HasPairing() { return !string.IsNullOrEmpty(pairedKey) && !string.IsNullOrEmpty(fingerprint); }
        void UpdatePairingUi() {
            bool paired=HasPairing();
            host.Enabled=key.Enabled=!busy && !paired;
            connect.Enabled=!busy;
            connect.Text=paired?"取消配对":"连接手机";
            pause.Enabled=!busy && paired;
            pause.Text=!ready?"重新连接":armed?"暂停控制":"恢复控制";
        }
        void CancelPairing() {
            if(busy) return;
            if(File.Exists(config)) File.Delete(config);
            ready=false; pairedKey=fingerprint=null; SetArmed(false);
            key.Clear(); host.Clear(); UpdatePairingUi();
            status.Text="已取消配对。填写手机地址和新的 6 位配对码可重新连接。";
        }
        async Task Connect() {
            if(busy) return;
            ready=false; SetArmed(false); busy=true; connect.Enabled=false; host.Enabled=key.Enabled=false; status.Text="正在连接手机…";
            try {
                if(!string.IsNullOrWhiteSpace(key.Text)) {
                    string[] pair=await RemoteClient.Pair(host.Text.Trim(),key.Text);
                    pairedKey=pair[0]; fingerprint=pair[1]; SavePairing(); key.Clear();
                }
                string response=await RemoteClient.Send(host.Text.Trim(),pairedKey??"","STATUS",fingerprint);
                if(closing) return;
                if(response=="OK") { SavePairing(); userPaused=false; ready=true; SetArmed(true); }
                else status.Text=RemoteClient.Explain(response);
            } catch(Exception ex) { if(!closing) status.Text=ex.Message; }
            finally { busy=false; if(!closing) UpdatePairingUi(); }
        }
        void ApplyHealth(string response) {
            if(response=="OK") {
                if(!ready) { ready=true; SetArmed(!userPaused); }
            } else {
                ready=false; SetArmed(false);
                status.Text=RemoteClient.Explain(response)+" · 每 5 秒自动重试。";
            }
        }
        async Task CheckConnection() {
            if(closing || busy || !HasPairing()) return;
            busy=true;
            try {
                string response=await RemoteClient.Send(host.Text.Trim(),pairedKey,"STATUS",fingerprint);
                if(!closing) ApplyHealth(response);
            } catch(Exception ex) {
                if(!closing) { ready=false; SetArmed(false); status.Text="手机不可达，正在自动重连。请确认同一局域网及手机 IP 未改变。\n"+ex.Message; }
            } finally { busy=false; if(!closing) UpdatePairingUi(); }
        }
        async Task Swipe(string direction) {
            if(!armed || busy || (DateTime.UtcNow-lastAction).TotalMilliseconds<450) return;
            busy=true; lastAction=DateTime.UtcNow; UpdatePairingUi();
            try {
                string response=await RemoteClient.Send(host.Text.Trim(),pairedKey,direction,fingerprint);
                if(closing) return;
                if(response=="ERR AUTH" || response=="ERR ACCESSIBILITY" || response=="ERR STOPPED") { ready=false; SetArmed(false); }
                status.Text=response=="OK" ? (direction=="UP"?"已上滑 · 下一条":"已下滑 · 上一条") + "  " + DateTime.Now.ToString("HH:mm:ss") : RemoteClient.Explain(response);
            } catch(Exception ex) { if(!closing) { ready=false; SetArmed(false); status.Text=ex.Message+"。正在自动重连…"; } }
            finally { busy=false; if(!closing) UpdatePairingUi(); }
        }
        void SetArmed(bool enabled) {
            if(enabled && !ready) return;
            armed=enabled; UnregisterHotKey(Handle,1); UnregisterHotKey(Handle,2); UnregisterHotKey(Handle,3);
            if(!armed) StopPointer();
            if(armed) {
                status.Text="控制已启用 · Ctrl+↓ / Ctrl+空格上滑，Ctrl+↑ 下滑。";
                var conflicts=new System.Collections.Generic.List<string>();
                if(!RegisterHotKey(Handle,1,0x4002,(uint)Keys.Up)) conflicts.Add("Ctrl+↑");
                if(!RegisterHotKey(Handle,2,0x4002,(uint)Keys.Down)) conflicts.Add("Ctrl+↓");
                if(!RegisterHotKey(Handle,3,0x4002,(uint)Keys.Space)) conflicts.Add("Ctrl+空格");
                if(conflicts.Count>0) status.Text="以下快捷键被其他程序占用："+string.Join("、",conflicts)+"。其他控制仍可用。";
            }
            next.Enabled=previous.Enabled=armed; UpdatePairingUi();
            if(!armed && !closing) status.Text="快捷键已暂停 · 点击连接或恢复继续。";
            if(mouse!=null) RefreshMouseHook();
        }
        void RefreshMouseHook() {
            wheelGesture.Reset();
            if(hook!=IntPtr.Zero) { UnhookWindowsHookEx(hook); hook=IntPtr.Zero; }
            if(!armed || closing) return;
            hook=SetWindowsHookEx(14,mouseProc,GetModuleHandle(null),0);
            if(hook==IntPtr.Zero) { mouse.Checked=false; status.Text="无法启用 Ctrl+滚轮，请使用 Ctrl+↑ / Ctrl+↓ 或 Ctrl+空格。"; }
        }
        IntPtr OnMouse(int code,IntPtr w,IntPtr l) {
            try {
            if(code>=0 && w.ToInt32()==0x0202 && leftCaptured) { leftCaptured=false;return new IntPtr(1); }
            if(code>=0 && armed && (GetAsyncKeyState((int)Keys.Menu)&0x8000)!=0) {
                if(w.ToInt32()==0x0200 && pointerSession!=null) {
                    var motion=(MouseData)Marshal.PtrToStructure(l,typeof(MouseData));Point p=PhonePoint(motion.pt);pointerSession.Move(p.X,p.Y);
                }
                if(w.ToInt32()==0x0201) {
                    leftCaptured=true;
                    if(pointerSession!=null) { var eventData=(MouseData)Marshal.PtrToStructure(l,typeof(MouseData));Point p=PhonePoint(eventData.pt);pointerSession.Click(p.X,p.Y); }
                    return new IntPtr(1);
                }
            }
            if(code>=0 && armed && mouse.Checked && w.ToInt32()==0x020A) {
                var data=(MouseData)Marshal.PtrToStructure(l,typeof(MouseData));
                bool ctrl=(GetAsyncKeyState((int)Keys.ControlKey) & 0x8000)!=0;
                string direction=wheelGesture.Feed(unchecked((short)(data.mouseData>>16)),ctrl,Environment.TickCount);
                if(!ctrl) return CallNextHookEx(hook,code,w,l);
                if(direction!=null) QueueSwipe(direction);
                return new IntPtr(1);
            }
            } catch(Exception ex) { Program.Log("Mouse callback exception",ex); }
            return CallNextHookEx(hook,code,w,l);
        }
        protected override void WndProc(ref Message m) {
            if(m.Msg==0x0312) {
                int id=m.WParam.ToInt32();
                if(id>=1 && id<=3) { string command=id==1?"DOWN":"UP"; QueueSwipe(command); }
            }
            base.WndProc(ref m);
        }
        void SavePairing() {
            Directory.CreateDirectory(Path.GetDirectoryName(config));
            byte[] data=Encoding.UTF8.GetBytes(host.Text.Trim()+"\n"+pairedKey+"\n"+fingerprint);
            File.WriteAllBytes(config,ProtectedData.Protect(data,null,DataProtectionScope.CurrentUser));
        }
        void LoadPairing() {
            if(!File.Exists(config)) return;
            try {
                string[] data=Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(config),null,DataProtectionScope.CurrentUser)).Split('\n');
                if(data.Length==3) { host.Text=data[0]; pairedKey=data[1]; fingerprint=data[2]; status.Text="配对已记住，无需再输配对码。手机开始接收后点击连接。"; }
            } catch { status.Text="配对信息无法读取，请重新填写。"; }
        }
    }
}
