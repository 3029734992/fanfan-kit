using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ClassLock
{
    // =====================================================================
    //  学生端
    //  功能：接收教师端指令 -> 黑屏 / 锁屏 / 解除；支持 /install 一键安装
    // =====================================================================
    static class Program
    {
        const string MAIN_MUTEX = "Local\\ClassLockMain_v1";
        const string WATCH_MUTEX = "Local\\ClassLockWatch_v1";

        static Mutex mainMutex;
        static Mutex watchMutex;

        [STAThread]
        internal static void Run(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { ThreadPool.SetMinThreads(32, 32); }   // 防止并发连接排队导致响应变慢
            catch { }

            List<string> a = new List<string>();
            foreach (string s in args) a.Add(s.ToLowerInvariant());

            if (a.Contains("/install") || a.Contains("-install")) { Install(RoomArg(a)); return; }
            if (a.Contains("/uninstall") || a.Contains("-uninstall")) { Uninstall(); return; }
            if (a.Contains("/room") || a.Contains("-room")) { SetRoom(RoomArg(a)); return; }
            if (a.Contains("/watch") || a.Contains("-watch")) { RunWatchdog(); return; }

            string state = Cfg.StateFree;
            int idx = a.IndexOf("/state");
            if (idx >= 0 && idx + 1 < a.Count)
            {
                string s = a[idx + 1].ToUpperInvariant();
                if (s == Cfg.StateLock || s == Cfg.StateBlack) state = s;
            }

            // 双击运行、且还没安装过 -> 引导安装
            if (a.Count == 0 && !Cfg.IsInstalled())
            {
                DialogResult dr = MessageBox.Show(
                    "检测到学生端尚未安装。\r\n\r\n" +
                    "是否现在安装到本机？安装后会自动设为开机启动、自动放行防火墙，之后再也不用管。\r\n\r\n" +
                    "（需要管理员权限，学生电脑请用管理员账号登录后再执行）",
                    Cfg.ProductName + " - 学生端", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (dr == DialogResult.Yes) Install(null);
                return;
            }

            RunMain(state);
        }

        /// <summary>从参数里取 /room 后面的教室组号，没写返回 null</summary>
        static string RoomArg(List<string> a)
        {
            int i = a.IndexOf("/room");
            if (i < 0) i = a.IndexOf("-room");
            if (i >= 0 && i + 1 < a.Count) return Cfg.NormalizeRoom(a[i + 1]);
            return null;
        }

        // ---------------- 正常运行 ----------------
        static void RunMain(string initialState)
        {
            bool created;
            mainMutex = new Mutex(true, MAIN_MUTEX, out created);
            if (!created) return;                 // 已有一个学生端在跑

            StartWatchdog();                      // 看护进程，防止被结束

            StudentApp app = new StudentApp(initialState);
            Application.Run(app);
        }

        static void StartWatchdog()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, "/watch");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(psi);
            }
            catch { }
        }

        // ---------------- 看护进程 ----------------
        static void RunWatchdog()
        {
            bool created;
            watchMutex = new Mutex(true, WATCH_MUTEX, out created);
            if (!created) return;

            Application.Run(new WatchContext());
        }

        // ---------------- 安装 / 卸载 ----------------
        static void Install(string room)
        {
            if (!Cfg.IsAdmin())
            {
                try
                {
                    string reArg = room == null ? "/install" : "/install /room " + room;
                    ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, reArg);
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                    Process.Start(psi);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("安装需要管理员权限。\r\n请右键点击学生端，选择“以管理员身份运行”。\r\n\r\n" + ex.Message,
                        Cfg.ProductName + " - 安装", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            try
            {
                Cfg.EnsureDir(Cfg.InstallDir);
                string target = Cfg.InstalledExe;
                if (!string.Equals(Application.ExecutablePath, target, StringComparison.OrdinalIgnoreCase))
                    File.Copy(Application.ExecutablePath, target, true);

                // 0. 教室组号（命令行没给就弹框问）
                if (room == null)
                {
                    string input = Microsoft.VisualBasic.Interaction.InputBox(
                        "请填写本教室的【教室组号】。\r\n\r\n" +
                        "同一个教室的学生机和教师端要填一样的组号（例如 301）。\r\n" +
                        "不同教室填不同组号，这样各教室之间就不会互相控制。",
                        "教室组号（学生端安装）", Cfg.DefaultRoom);
                    room = Cfg.NormalizeRoom(input);
                }
                Cfg.WriteRoom(room);

                // 1. 开机（登录）自启
                using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (rk != null) rk.SetValue(Cfg.RunValueName, "\"" + target + "\"");
                }

                // 2. 放行防火墙（仅 TCP 19527 入站）
                RunCmd("netsh", "advfirewall firewall delete rule name=\"" + Cfg.FwRuleName + "\"");
                RunCmd("netsh", "advfirewall firewall add rule name=\"" + Cfg.FwRuleName +
                    "\" dir=in action=allow protocol=TCP localport=" + Cfg.Port + " profile=any");

                // 3. 立即在当前会话启动
                try
                {
                    ProcessStartInfo si = new ProcessStartInfo(target);
                    si.UseShellExecute = false;
                    si.CreateNoWindow = true;
                    Process.Start(si);
                }
                catch { }

                MessageBox.Show(
                    "学生端安装完成！\r\n\r\n" +
                    "本机教室组号：" + room + "\r\n" +
                    "安装位置：" + target + "\r\n" +
                    "监听端口：" + Cfg.Port + "（TCP）\r\n\r\n" +
                    "教师端的“教室组号”必须填成一样的，才能控制本机。\r\n" +
                    "以后每次开机都会自动运行，不需要再安装。\r\n" +
                    "如需改组号，用管理员运行：Student.exe /room 新组号\r\n" +
                    "如需卸载，请以管理员身份运行本程序并加参数 /uninstall。",
                    Cfg.ProductName + " - 安装成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("安装失败：" + ex.Message, Cfg.ProductName + " - 安装",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---------------- 修改教室组号 ----------------
        static void SetRoom(string room)
        {
            if (room == null)
            {
                MessageBox.Show("用法：Student.exe /room 教室组号\r\n例如：Student.exe /room 301",
                    Cfg.ProductName + " - 教室组号", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!Cfg.IsAdmin())
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, "/room " + room);
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                    Process.Start(psi);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("修改教室组号需要管理员权限。\r\n请右键以管理员身份运行本程序。\r\n\r\n" + ex.Message,
                        Cfg.ProductName + " - 教室组号", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            try
            {
                Cfg.WriteRoom(room);
                MessageBox.Show("本机教室组号已设为：" + room + "\r\n\r\n" +
                    "教师端的“教室组号”填成一样的即可控制本机（立即生效，无需重启）。",
                    Cfg.ProductName + " - 教室组号", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("设置失败：" + ex.Message, Cfg.ProductName + " - 教室组号",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static void Uninstall()
        {
            if (!Cfg.IsAdmin())
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, "/uninstall");
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                    Process.Start(psi);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("卸载需要管理员权限。\r\n\r\n" + ex.Message, Cfg.ProductName + " - 卸载",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            try
            {
                using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (rk != null) rk.DeleteValue(Cfg.RunValueName, false);
                }
                RunCmd("netsh", "advfirewall firewall delete rule name=\"" + Cfg.FwRuleName + "\"");

                // 反复清理，防止看护进程把主进程又拉起来
                for (int i = 0; i < 4; i++)
                {
                    KillByName("Student");
                    KillByName("ClassLockStudent");
                    Thread.Sleep(700);
                }
                Thread.Sleep(500);
                try { if (Directory.Exists(Cfg.InstallDir)) Directory.Delete(Cfg.InstallDir, true); } catch { }

                MessageBox.Show("学生端已卸载。\r\n如果目录未能删除，重启电脑后可手动删除：\r\n" + Cfg.InstallDir,
                    Cfg.ProductName + " - 卸载", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("卸载过程中出现问题：" + ex.Message, Cfg.ProductName + " - 卸载",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static void KillByName(string name)
        {
            try
            {
                foreach (Process p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        if (p.Id == Process.GetCurrentProcess().Id) continue;
                        p.Kill();
                    }
                    catch { }
                }
            }
            catch { }
        }

        static string RunCmd(string file, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                Process p = Process.Start(psi);
                string o = p.StandardOutput.ReadToEnd();
                string e = p.StandardError.ReadToEnd();
                p.WaitForExit(20000);
                return (o + " " + e).Trim();
            }
            catch (Exception ex) { return ex.Message; }
        }
    }

    // =====================================================================
    //  看不见的窗体，用来做 UI 线程调度
    // =====================================================================
    class AnchorForm : Form
    {
        protected override void SetVisibleCore(bool value)
        {
            // 关键：必须在这里把句柄建出来，否则跨线程调度不可用
            if (!IsHandleCreated) CreateHandle();
            base.SetVisibleCore(false);
        }
    }

    // =====================================================================
    //  看护进程：主进程被杀掉后自动拉起来，并恢复锁定状态
    // =====================================================================
    class WatchContext : ApplicationContext
    {
        System.Threading.Timer timer;

        public WatchContext()
        {
            timer = new System.Threading.Timer(Check, null, 2000, 2500);
        }

        void Check(object o)
        {
            try
            {
                if (MainAlive()) return;
                string state = ReadState();
                string exe = Cfg.InstalledExe;
                if (!File.Exists(exe)) exe = Application.ExecutablePath;

                ProcessStartInfo psi = new ProcessStartInfo(exe, "/state " + state);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi);
            }
            catch { }
        }

        static bool MainAlive()
        {
            try
            {
                Mutex m = Mutex.OpenExisting("Local\\ClassLockMain_v1");
                m.Close();
                return true;
            }
            catch { return false; }
        }

        static string ReadState()
        {
            try
            {
                if (File.Exists(Cfg.StateFile))
                {
                    string s = File.ReadAllText(Cfg.StateFile).Trim().ToUpperInvariant();
                    if (s == Cfg.StateLock || s == Cfg.StateBlack) return s;
                }
            }
            catch { }
            return Cfg.StateFree;
        }
    }

    // =====================================================================
    //  学生端主体：指令服务 + 状态管理
    // =====================================================================
    class StudentApp : ApplicationContext
    {
        readonly AnchorForm anchor = new AnchorForm();
        readonly Overlay overlay = new Overlay();
        readonly HookMgr hooks;
        readonly int uiThreadId;
        TcpListener listener;
        Thread acceptThread;
        System.Threading.Timer healthTimer;
        string state = Cfg.StateFree;

        public StudentApp(string initialState)
        {
            uiThreadId = Thread.CurrentThread.ManagedThreadId;
            IntPtr forceHandle = anchor.Handle;   // 强制建出句柄，保证可以跨线程调度到 UI 线程

            hooks = new HookMgr(anchor);
            hooks.EmergencyUnlock = delegate { PostToUi(delegate { ApplyState(Cfg.StateFree); }); };

            StartListener();

            // 兜底：万一监听线程意外退出，自动重新拉起，避免“用久了收不到指令”
            healthTimer = new System.Threading.Timer(HealthCheck, null, 5000, 5000);

            ApplyState(initialState == Cfg.StateLock || initialState == Cfg.StateBlack ? initialState : Cfg.StateFree);
        }

        void StartListener()
        {
            try
            {
                if (listener != null) { try { listener.Stop(); } catch { } }
                listener = new TcpListener(IPAddress.Any, Cfg.Port);
                listener.Start();
            }
            catch (Exception ex) { Cfg.LogError(ex); }

            acceptThread = new Thread(AcceptLoop);
            acceptThread.IsBackground = true;
            acceptThread.Start();
        }

        void HealthCheck(object o)
        {
            try
            {
                if (acceptThread != null && acceptThread.IsAlive) return;
                Cfg.LogText("监听线程已退出，正在自动恢复...");
                StartListener();
            }
            catch (Exception ex) { Cfg.LogError(ex); }
        }

        void AcceptLoop()
        {
            while (true)
            {
                TcpClient c = null;
                try { c = listener.AcceptTcpClient(); }
                catch (Exception ex)
                {
                    // 关键：单次异常绝不能停掉监听，否则这台机器会永久“掉线”
                    Cfg.LogText("Accept 异常（已忽略）: " + ex.Message);
                    Thread.Sleep(200);
                    continue;
                }
                try { ThreadPool.QueueUserWorkItem(HandleClient, c); }
                catch { try { c.Close(); } catch { } }
            }
        }

        void HandleClient(object o)
        {
            TcpClient c = (TcpClient)o;
            try
            {
                c.NoDelay = true;
                NetworkStream ns = c.GetStream();
                string line = Wire.ReadLine(ns);
                string resp = ProcessLine(line);
                byte[] b = Encoding.UTF8.GetBytes(resp + "\n");
                ns.Write(b, 0, b.Length);
                ns.Flush();
            }
            catch { }
            finally { try { c.Close(); } catch { } }
        }

        string ProcessLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return "ERR|empty";
            string[] parts = line.Trim().Split(' ');
            if (parts.Length < 3) return "ERR|format";
            if (parts[0] != Cfg.Token) return "ERR|auth";
            if (!string.Equals(parts[1], Cfg.ReadRoom(), StringComparison.OrdinalIgnoreCase)) return "ERR|room";

            string cmd = parts[2].ToUpperInvariant();
            if (cmd == "PING") return "OK|" + Cfg.HostName() + "|" + Cfg.UserName() + "|" + StateText();
            if (cmd == "BLACK") { InvokeUi(delegate { ApplyState(Cfg.StateBlack); }); return "OK|" + StateText(); }
            if (cmd == "LOCK") { InvokeUi(delegate { ApplyState(Cfg.StateLock); }); return "OK|" + StateText(); }
            if (cmd == "UNLOCK") { InvokeUi(delegate { ApplyState(Cfg.StateFree); }); return "OK|" + StateText(); }
            return "ERR|unknown";
        }

        string StateText()
        {
            if (state == Cfg.StateLock) return "已锁定";
            if (state == Cfg.StateBlack) return "已黑屏";
            return "空闲";
        }

        // ---------- UI 线程调度 ----------
        void InvokeUi(MethodInvoker m)
        {
            if (Thread.CurrentThread.ManagedThreadId == uiThreadId)
            {
                try { m(); } catch (Exception ex) { Cfg.LogError(ex); }
                return;
            }
            try
            {
                // 带超时的调度：万一 UI 线程被卡住，也不要把线程池里的线程永久挂死
                IAsyncResult r = anchor.BeginInvoke(m);
                if (!r.AsyncWaitHandle.WaitOne(5000))
                {
                    Cfg.LogText("UI 调度超时，本机界面可能未及时更新");
                    return;
                }
                anchor.EndInvoke(r);
            }
            catch (Exception ex) { Cfg.LogError(ex); }
        }

        void PostToUi(MethodInvoker m)
        {
            if (Thread.CurrentThread.ManagedThreadId == uiThreadId)
            {
                try { m(); } catch (Exception ex) { Cfg.LogError(ex); }
                return;
            }
            try { anchor.BeginInvoke(m); }
            catch (Exception ex) { Cfg.LogError(ex); }
        }

        // ---------- 状态应用 ----------
        void ApplyState(string s)
        {
            state = s;
            try
            {
                Cfg.EnsureDir(Cfg.StateDir);
                File.WriteAllText(Cfg.StateFile, s);
            }
            catch { }

            try
            {
                if (s == Cfg.StateFree)
                {
                    hooks.Stop();
                    overlay.Hide();
                }
                else
                {
                    overlay.Show(s == Cfg.StateLock);
                    hooks.Start(s == Cfg.StateLock);
                }
            }
            catch (Exception ex) { Cfg.LogError(ex); }
        }

        protected override void Dispose(bool disposing)
        {
            try { hooks.Stop(); } catch { }
            try { overlay.Hide(); } catch { }
            try { listener.Stop(); } catch { }
            base.Dispose(disposing);
        }
    }

    // =====================================================================
    //  全屏黑底窗体：自己画背景和居中文字（不用 Label，避免布局问题）
    // =====================================================================
    class BlankForm : Form
    {
        readonly string message;
        readonly Font textFont = new Font("微软雅黑", 36F, FontStyle.Bold);

        public BlankForm(string msg)
        {
            message = msg;
            AutoScaleMode = AutoScaleMode.None;   // 不要因为字体变化触发自动缩放
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            ControlBox = false;
            KeyPreview = true;
            Text = "系统提示";
            BackColor = Color.Black;
            ForeColor = Color.White;
            TopMost = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.Black);

            if (!string.IsNullOrEmpty(message))
            {
                TextRenderer.DrawText(e.Graphics, message, textFont, ClientRectangle, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            }
        }
    }

    // =====================================================================
    //  全屏黑屏 / 锁屏遮罩
    // =====================================================================
    class Overlay
    {
        const int HWND_TOPMOST = -1;
        const int SWP_NOMOVE = 0x0002;
        const int SWP_NOSIZE = 0x0001;
        const int SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);

        readonly List<Form> forms = new List<Form>();
        System.Windows.Forms.Timer timer;
        bool closing;
        bool hiddenCursor;

        public bool IsVisible { get { return forms.Count > 0; } }

        public void Show(bool hardLock)
        {
            Hide();
            closing = false;

            try
            {
                string msg = hardLock ? Cfg.LockMessage : Cfg.BlackMessage;
                foreach (Screen s in Screen.AllScreens)
                {
                    BlankForm f = new BlankForm(msg);
                    f.Bounds = s.Bounds;
                    f.FormClosing += OverlayClosing;
                    f.Show();
                    f.Bounds = s.Bounds;
                    f.TopMost = true;
                    f.Invalidate();
                    forms.Add(f);
                }
            }
            catch (Exception ex)
            {
                Cfg.LogError(ex);
                Hide();
                return;
            }

            if (forms.Count > 0)
            {
                try { SetForegroundWindow(forms[0].Handle); } catch { }
                if (hardLock)
                {
                    try { Cursor.Hide(); hiddenCursor = true; } catch { }
                }
            }

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 800;
            timer.Tick += KeepTopMost;
            timer.Start();
        }

        void OverlayClosing(object sender, FormClosingEventArgs e)
        {
            if (!closing) e.Cancel = true;
        }

        void KeepTopMost(object sender, EventArgs e)
        {
            try
            {
                foreach (Form f in forms)
                {
                    if (f.IsDisposed) continue;
                    if (!f.TopMost) f.TopMost = true;
                    SetWindowPos(f.Handle, HWND_TOPMOST, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
                if (forms.Count > 0 && !forms[0].IsDisposed) SetForegroundWindow(forms[0].Handle);
            }
            catch { }
        }

        public void Hide()
        {
            try
            {
                if (timer != null) { timer.Stop(); timer.Dispose(); timer = null; }
            }
            catch { }

            closing = true;
            foreach (Form f in forms)
            {
                try { if (!f.IsDisposed) f.Close(); } catch { }
                try { if (!f.IsDisposed) f.Dispose(); } catch { }
            }
            forms.Clear();

            if (hiddenCursor)
            {
                try { Cursor.Show(); } catch { }
                hiddenCursor = false;
            }
        }
    }

    // =====================================================================
    //  键盘 / 鼠标钩子
    //  - 黑屏：只拦截 Alt+Tab / Alt+F4 / Win 等“跑出去”的键
    //  - 锁屏：拦掉全部键盘和鼠标
    //  - 紧急解除热键：Ctrl+Alt+Shift+U（网络出问题时老师可在本机手动解除）
    // =====================================================================
    class HookMgr
    {
        const int WH_KEYBOARD_LL = 13;
        const int WH_MOUSE_LL = 14;
        const int WM_KEYDOWN = 0x0100;
        const int WM_KEYUP = 0x0101;
        const int WM_SYSKEYDOWN = 0x0104;
        const int WM_SYSKEYUP = 0x0105;

        const int VK_SHIFT = 0x10;
        const int VK_CONTROL = 0x11;
        const int VK_MENU = 0x12;   // Alt
        const int VK_TAB = 0x09;
        const int VK_ESCAPE = 0x1B;
        const int VK_F4 = 0x73;
        const int VK_SPACE = 0x20;
        const int VK_RETURN = 0x0D;
        const int VK_LWIN = 0x5B;
        const int VK_RWIN = 0x5C;
        const int VK_U = 0x55;

        [StructLayout(LayoutKind.Sequential)]
        struct KBDLLHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr GetModuleHandle(string lpModuleName);

        readonly Control anchor;
        IntPtr kbHook = IntPtr.Zero;
        IntPtr msHook = IntPtr.Zero;
        HookProc kbProc;
        HookProc msProc;
        bool hard;

        public Action EmergencyUnlock;

        public HookMgr(Control uiAnchor)
        {
            anchor = uiAnchor;
        }

        public void Start(bool hardLock)
        {
            hard = hardLock;

            if (kbHook == IntPtr.Zero)
            {
                kbProc = KbCallback;
                kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, kbProc, GetModuleHandle(null), 0);
            }

            if (hard && msHook == IntPtr.Zero)
            {
                msProc = MsCallback;
                msHook = SetWindowsHookEx(WH_MOUSE_LL, msProc, GetModuleHandle(null), 0);
            }
        }

        public void Stop()
        {
            hard = false;
            try
            {
                if (kbHook != IntPtr.Zero) UnhookWindowsHookEx(kbHook);
                if (msHook != IntPtr.Zero) UnhookWindowsHookEx(msHook);
            }
            catch { }
            kbHook = IntPtr.Zero;
            msHook = IntPtr.Zero;
        }

        static bool Down(int vk)
        {
            try { return (GetAsyncKeyState(vk) & 0x8000) != 0; }
            catch { return false; }
        }

        IntPtr KbCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    KBDLLHOOKSTRUCT d = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                    int vk = d.vkCode;
                    bool down = (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN);

                    // 紧急解除
                    if (down && vk == VK_U && Down(VK_CONTROL) && Down(VK_MENU) && Down(VK_SHIFT))
                    {
                        if (EmergencyUnlock != null) EmergencyUnlock();
                        return (IntPtr)1;
                    }

                    if (hard) return (IntPtr)1;     // 锁屏：全部吃掉

                    bool alt = Down(VK_MENU);
                    bool ctrl = Down(VK_CONTROL);

                    if (vk == VK_LWIN || vk == VK_RWIN) return (IntPtr)1;
                    if (alt && (vk == VK_TAB || vk == VK_ESCAPE || vk == VK_F4 || vk == VK_SPACE || vk == VK_RETURN))
                        return (IntPtr)1;
                    if (ctrl && vk == VK_ESCAPE) return (IntPtr)1;
                }
            }
            catch { }

            return CallNextHookEx(kbHook, nCode, wParam, lParam);
        }

        IntPtr MsCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && hard) return (IntPtr)1;
            return CallNextHookEx(msHook, nCode, wParam, lParam);
        }
    }
}
