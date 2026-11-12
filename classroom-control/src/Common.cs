using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace ClassLock
{
    /// <summary>全局配置与共用小工具</summary>
    public static class Cfg
    {
        public const int Port = 19527;                 // 学生端监听端口
        public const string Token = "CLK-2f9a7b41";    // 通信口令，防止别人乱发指令
        public const int ConnectTimeoutMs = 1000;      // 连接超时
        public const int IoTimeoutMs = 2500;           // 读写超时
        public const int CommandRetries = 2;           // 控制指令失败重试次数（含首次）

        public const string DefaultRoom = "1";         // 教室组号默认值
        public const int RoomMaxLen = 24;              // 组号最大长度

        public const string FwRuleName = "ClassLock Student";
        public const string RunValueName = "ClassLockStudent";
        public const string ProductName = "课堂电脑控制";

        public const string StateFree = "FREE";
        public const string StateBlack = "BLACK";
        public const string StateLock = "LOCK";

        // 遮罩上显示的文字，想改文案只改这里即可
        public const string BlackMessage = "老师提醒你上课认真听讲哦";
        public const string LockMessage = "本机已被老师锁定";

        // 安装目录（需要管理员写入）
        public static string InstallDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ClassLock"); }
        }

        // 运行状态目录（普通用户可写）
        public static string StateDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClassLock"); }
        }

        public static string InstalledExe { get { return Path.Combine(InstallDir, "Student.exe"); } }
        public static string StateFile { get { return Path.Combine(StateDir, "state.txt"); } }
        public static string RoomFile { get { return Path.Combine(InstallDir, "room.txt"); } }

        /// <summary>读取本机（学生端）所属教室组号；没设置时用默认组号</summary>
        public static string ReadRoom()
        {
            try
            {
                if (File.Exists(RoomFile))
                {
                    string s = File.ReadAllText(RoomFile).Trim();
                    if (s.Length > 0) return NormalizeRoom(s);
                }
            }
            catch { }
            return DefaultRoom;
        }

        /// <summary>写入本机（学生端）所属教室组号</summary>
        public static void WriteRoom(string room)
        {
            EnsureDir(InstallDir);
            File.WriteAllText(RoomFile, NormalizeRoom(room));
        }

        /// <summary>组号只保留字母/数字/下划线/短横，防空值</summary>
        public static string NormalizeRoom(string s)
        {
            if (s == null) return DefaultRoom;
            StringBuilder sb = new StringBuilder();
            foreach (char c in s.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
                if (sb.Length >= RoomMaxLen) break;
            }
            return sb.Length == 0 ? DefaultRoom : sb.ToString();
        }

        public static bool IsAdmin()
        {
            try
            {
                WindowsPrincipal wp = new WindowsPrincipal(WindowsIdentity.GetCurrent());
                return wp.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        /// <summary>是否已经装了开机自启（即已安装）</summary>
        public static bool IsInstalled()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey rk = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (rk == null) return false;
                    object v = rk.GetValue(RunValueName);
                    return v != null && v.ToString().Length > 0;
                }
            }
            catch { return false; }
        }

        public static string HostName()
        {
            try { return Dns.GetHostName(); }
            catch { return "?"; }
        }

        public static string UserName()
        {
            try { return Environment.UserName; }
            catch { return "?"; }
        }

        public static void EnsureDir(string dir)
        {
            try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); }
            catch { }
        }

        /// <summary>把异常写到 %LOCALAPPDATA%\ClassLock\error.log，方便排查</summary>
        public static void LogError(Exception ex)
        {
            LogText(ex.ToString());
        }

        public static void LogText(string s)
        {
            try
            {
                EnsureDir(StateDir);
                string p = Path.Combine(StateDir, "error.log");
                try
                {
                    if (File.Exists(p) && new FileInfo(p).Length > 200000) File.Delete(p);
                }
                catch { }
                File.AppendAllText(p, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + s + "\r\n");
            }
            catch { }
        }

        /// <summary>把目标 IP 列表按本机网卡算出可能的网段主机</summary>
        public static List<string> LocalSubnetHosts(int maxTotal)
        {
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>();
            try
            {
                NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
                foreach (NetworkInterface nic in nics)
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                    UnicastIPAddressInformationCollection ua = nic.GetIPProperties().UnicastAddresses;
                    foreach (UnicastIPAddressInformation info in ua)
                    {
                        if (info.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        byte[] ipb = info.Address.GetAddressBytes();
                        if (ipb[0] == 127) continue;
                        if (ipb[0] == 169 && ipb[1] == 254) continue;   // APIPA 自动地址

                        int prefix = 24;
                        if (info.IPv4Mask != null)
                        {
                            byte[] mb = info.IPv4Mask.GetAddressBytes();
                            prefix = 0;
                            for (int i = 0; i < 4; i++)
                            {
                                int v = mb[i];
                                while ((v & 0x80) != 0) { prefix++; v = (v << 1) & 0xFF; }
                            }
                        }
                        if (prefix >= 31) continue;             // 点对点地址不扫描
                        if (prefix < 22) prefix = 22;           // 网段太大时按 /22 截断，避免扫爆

                        uint ipv = ToUInt(ipb);
                        uint mask = prefix == 0 ? 0 : (0xFFFFFFFFu << (32 - prefix));
                        uint net = ipv & mask;
                        uint broadcast = net | (~mask);

                        uint start = net + 1;
                        uint end = broadcast - 1;
                        uint count = end - start + 1;

                        for (uint h = start; h <= end; h++)
                        {
                            if (result.Count >= maxTotal) return result;
                            string s = ToIp(h);
                            if (seen.Add(s)) result.Add(s);
                        }
                    }
                }
            }
            catch { }
            return result;
        }

        static uint ToUInt(byte[] b)
        {
            return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        }

        static string ToIp(uint v)
        {
            return ((v >> 24) & 0xFF) + "." + ((v >> 16) & 0xFF) + "." + ((v >> 8) & 0xFF) + "." + (v & 0xFF);
        }

        public static string LocalIpText()
        {
            List<string> ips = new List<string>();
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (UnicastIPAddressInformation info in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (info.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        byte[] b = info.Address.GetAddressBytes();
                        if (b[0] == 127 || (b[0] == 169 && b[1] == 254)) continue;
                        ips.Add(info.Address.ToString());
                    }
                }
            }
            catch { }
            return string.Join(" / ", ips.ToArray());
        }
    }

    /// <summary>教师端 &lt;-&gt; 学生端 的简单文本协议</summary>
    public static class Wire
    {
        /// <summary>发送控制指令并等待回复；失败会自动重试一次</summary>
        public static string Send(string ip, string room, string cmd)
        {
            return Send(ip, room, cmd, Cfg.ConnectTimeoutMs, Cfg.CommandRetries);
        }

        /// <summary>扫描等场景用：单次尝试，避免拖慢整体速度</summary>
        public static string Send(string ip, string room, string cmd, int connectTimeoutMs)
        {
            return Send(ip, room, cmd, connectTimeoutMs, 1);
        }

        public static string Send(string ip, string room, string cmd, int connectTimeoutMs, int attempts)
        {
            Exception last = null;
            for (int i = 0; i < attempts; i++)
            {
                try { return SendOnce(ip, room, cmd, connectTimeoutMs); }
                catch (Exception ex) { last = ex; Thread.Sleep(60); }
            }
            throw last;
        }

        static string SendOnce(string ip, string room, string cmd, int connectTimeoutMs)
        {
            TcpClient client = new TcpClient();
            try
            {
                client.NoDelay = true;
                IAsyncResult ar = client.BeginConnect(ip, Cfg.Port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(connectTimeoutMs, false))
                    throw new TimeoutException("连接超时");
                client.EndConnect(ar);

                NetworkStream ns = client.GetStream();
                byte[] buf = Encoding.UTF8.GetBytes(Cfg.Token + " " + Cfg.NormalizeRoom(room) + " " + cmd + "\n");
                ns.Write(buf, 0, buf.Length);
                ns.Flush();
                return ReadLine(ns);
            }
            finally
            {
                try { client.Close(); } catch { }
            }
        }

        public static string ReadLine(NetworkStream ns)
        {
            MemoryStream ms = new MemoryStream();
            byte[] one = new byte[1];
            DateTime deadline = DateTime.Now.AddMilliseconds(Cfg.IoTimeoutMs);
            while (DateTime.Now < deadline)
            {
                if (!ns.DataAvailable) { Thread.Sleep(5); continue; }
                int n;
                try { n = ns.Read(one, 0, 1); }
                catch { break; }
                if (n <= 0) break;
                if (one[0] == (byte)'\n') break;
                ms.WriteByte(one[0]);
                if (ms.Length > 4096) break;
            }
            return Encoding.UTF8.GetString(ms.ToArray()).TrimEnd('\r');
        }

        public static string[] Split(string response)
        {
            if (response == null) return new string[0];
            return response.Split('|');
        }
    }
}
