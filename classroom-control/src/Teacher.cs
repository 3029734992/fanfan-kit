using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualBasic;

namespace ClassLock
{
    // =====================================================================
    //  教师端
    //  功能：扫描局域网 -> 列出学生机 -> 一键 黑屏 / 锁屏 / 解除
    // =====================================================================
    static class TeacherProgram
    {
        [STAThread]
        internal static void Run()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { ThreadPool.SetMinThreads(300, 300); }
            catch { }
            Application.Run(new MainForm());
        }
    }

    class MainForm : Form
    {
        Panel top;
        ListView list;
        GroupBox logGroup;
        TextBox logBox;
        Label lblLocal;
        Label lblRoom;
        TextBox roomBox;
        Button btnScan, btnAdd, btnDel, btnAll, btnNone, btnTest, btnHelp;
        Button btnBlack, btnLock, btnUnlock;
        ContextMenuStrip menu;

        bool scanning;

        string StorePath { get { return Path.Combine(Application.StartupPath, "students.txt"); } }
        string RoomStorePath { get { return Path.Combine(Application.StartupPath, "room.txt"); } }

        /// <summary>当前教室组号（教师端要和学生端一致才能控制）</summary>
        string CurrentRoom() { return Cfg.NormalizeRoom(roomBox.Text); }

        string LoadRoom()
        {
            try
            {
                if (File.Exists(RoomStorePath))
                {
                    string s = File.ReadAllText(RoomStorePath).Trim();
                    if (s.Length > 0) return Cfg.NormalizeRoom(s);
                }
            }
            catch { }
            return Cfg.DefaultRoom;
        }

        void SaveRoom()
        {
            try { File.WriteAllText(RoomStorePath, CurrentRoom()); }
            catch { }
        }

        public MainForm()
        {
            Text = Cfg.ProductName + " - 教师端";
            ClientSize = new Size(1000, 660);
            MinimumSize = new Size(820, 520);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("微软雅黑", 9F);
            Icon = AppIcon.Loaded;

            // ---------- 顶部按钮区 ----------
            top = new Panel();
            top.Location = new Point(4, 4);
            top.Size = new Size(992, 84);
            top.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            btnScan = MakeButton(top, 10, 10, 110, 28, "扫描局域网", delegate { ScanNetwork(); });
            btnAdd = MakeButton(top, 128, 10, 96, 28, "添加 IP", delegate { AddByHand(); });
            btnDel = MakeButton(top, 232, 10, 96, 28, "删除选中", delegate { DeleteSelected(); });
            btnAll = MakeButton(top, 336, 10, 72, 28, "全选", delegate { SetAllChecked(true); });
            btnNone = MakeButton(top, 416, 10, 72, 28, "全不选", delegate { SetAllChecked(false); });
            btnTest = MakeButton(top, 496, 10, 96, 28, "测试连接", delegate { TestChecked(); });
            btnHelp = MakeButton(top, 600, 10, 96, 28, "使用说明", delegate { ShowHelp(); });

            lblLocal = new Label();
            lblLocal.AutoSize = false;
            lblLocal.Location = new Point(706, 15);
            lblLocal.Size = new Size(280, 20);
            lblLocal.Text = "本机 IP：" + Cfg.LocalIpText();
            lblLocal.ForeColor = Color.DimGray;
            top.Controls.Add(lblLocal);

            Label lblOps = new Label();
            lblOps.AutoSize = false;
            lblOps.Location = new Point(10, 54);
            lblOps.Size = new Size(186, 22);
            lblOps.Text = "批量操作（作用于勾选的电脑）：";
            lblOps.ForeColor = Color.DimGray;
            top.Controls.Add(lblOps);

            btnBlack = MakeButton(top, 200, 46, 110, 30, "黑  屏", delegate { RunOnChecked("BLACK"); });
            btnLock = MakeButton(top, 318, 46, 110, 30, "锁  屏", delegate { RunOnChecked("LOCK"); });
            btnUnlock = MakeButton(top, 436, 46, 110, 30, "解  除", delegate { RunOnChecked("UNLOCK"); });
            btnBlack.Font = new Font("微软雅黑", 9.5F, FontStyle.Bold);
            btnLock.Font = new Font("微软雅黑", 9.5F, FontStyle.Bold);
            btnUnlock.Font = new Font("微软雅黑", 9.5F, FontStyle.Bold);
            btnBlack.ForeColor = Color.FromArgb(40, 40, 40);
            btnLock.ForeColor = Color.FromArgb(180, 30, 30);
            btnUnlock.ForeColor = Color.FromArgb(20, 110, 40);

            lblRoom = new Label();
            lblRoom.AutoSize = false;
            lblRoom.Location = new Point(562, 52);
            lblRoom.Size = new Size(70, 22);
            lblRoom.Text = "教室组号：";
            lblRoom.ForeColor = Color.DimGray;
            top.Controls.Add(lblRoom);

            roomBox = new TextBox();
            roomBox.Location = new Point(632, 49);
            roomBox.Size = new Size(96, 24);
            roomBox.Text = LoadRoom();
            roomBox.MaxLength = Cfg.RoomMaxLen;
            top.Controls.Add(roomBox);

            Label lblRoomTip = new Label();
            lblRoomTip.AutoSize = false;
            lblRoomTip.Location = new Point(736, 52);
            lblRoomTip.Size = new Size(250, 22);
            lblRoomTip.Text = "（需与学生端一致）";
            lblRoomTip.ForeColor = Color.DimGray;
            top.Controls.Add(lblRoomTip);

            // ---------- 列表 ----------
            list = new ListView();
            list.Location = new Point(4, 92);
            list.Size = new Size(992, 396);
            list.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            list.View = View.Details;
            list.CheckBoxes = true;
            list.FullRowSelect = true;
            list.GridLines = true;
            list.HideSelection = false;
            list.MultiSelect = true;
            list.Columns.Add("IP 地址", 150);
            list.Columns.Add("计算机名", 170);
            list.Columns.Add("登录用户", 130);
            list.Columns.Add("学生端状态", 100);
            list.Columns.Add("最近结果", 400);
            list.DoubleClick += delegate { RunOnItems(SelectedItems(), "LOCK"); };

            menu = new ContextMenuStrip();
            menu.Items.Add("黑屏", null, delegate { RunOnItems(SelectedItems(), "BLACK"); });
            menu.Items.Add("锁屏", null, delegate { RunOnItems(SelectedItems(), "LOCK"); });
            menu.Items.Add("解除", null, delegate { RunOnItems(SelectedItems(), "UNLOCK"); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("测试连接", null, delegate { RunOnItems(SelectedItems(), "PING"); });
            menu.Items.Add("删除", null, delegate { DeleteSelected(); });
            list.ContextMenuStrip = menu;

            // ---------- 日志 ----------
            logGroup = new GroupBox();
            logGroup.Text = "操作日志";
            logGroup.Location = new Point(4, 496);
            logGroup.Size = new Size(992, 160);
            logGroup.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            logBox = new TextBox();
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.Dock = DockStyle.Fill;
            logBox.BackColor = Color.FromArgb(250, 250, 250);
            logBox.Font = new Font("Consolas", 9F);
            logGroup.Controls.Add(logBox);

            Controls.Add(top);
            Controls.Add(list);
            Controls.Add(logGroup);

            Load += delegate
            {
                LoadList();
                Log("就绪。本机 IP：" + Cfg.LocalIpText());
                Log("当前教室组号：" + CurrentRoom() + "（教师端与学生端必须一致，否则扫描不到、也控制不了）");
            };
            FormClosing += delegate { SaveList(); SaveRoom(); };
        }

        Button MakeButton(Control parent, int x, int y, int w, int h, string text, EventHandler onClick)
        {
            Button b = new Button();
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            b.Text = text;
            b.Click += onClick;
            parent.Controls.Add(b);
            return b;
        }

        // =================================================================
        //  列表维护
        // =================================================================
        ListViewItem FindItem(string ip)
        {
            foreach (ListViewItem it in list.Items)
            {
                if (string.Equals(it.SubItems[0].Text, ip, StringComparison.Ordinal)) return it;
            }
            return null;
        }

        ListViewItem AddOrUpdate(string ip, string name, string user, string status, bool online, string result)
        {
            ListViewItem it = FindItem(ip);
            bool isNew = false;
            if (it == null)
            {
                it = new ListViewItem(ip);
                it.SubItems.Add(name == null ? "" : name);
                it.SubItems.Add(user == null ? "" : user);
                it.SubItems.Add(status == null ? "" : status);
                it.SubItems.Add(result == null ? "" : result);
                it.Checked = true;
                isNew = true;
            }
            else
            {
                if (!string.IsNullOrEmpty(name)) it.SubItems[1].Text = name;
                if (!string.IsNullOrEmpty(user)) it.SubItems[2].Text = user;
                if (!string.IsNullOrEmpty(status)) it.SubItems[3].Text = status;
                if (result != null) it.SubItems[4].Text = result;
            }

            it.ForeColor = online ? Color.Black : Color.Silver;

            if (isNew) list.Items.Add(it);
            return it;
        }

        List<ListViewItem> CheckedItems()
        {
            List<ListViewItem> r = new List<ListViewItem>();
            foreach (ListViewItem it in list.Items)
                if (it.Checked) r.Add(it);
            return r;
        }

        List<ListViewItem> SelectedItems()
        {
            List<ListViewItem> r = new List<ListViewItem>();
            foreach (ListViewItem it in list.SelectedItems) r.Add(it);
            return r;
        }

        void SetAllChecked(bool v)
        {
            foreach (ListViewItem it in list.Items) it.Checked = v;
        }

        void DeleteSelected()
        {
            List<ListViewItem> sel = SelectedItems();
            if (sel.Count == 0) { Log("请先在列表里选中要删除的行。"); return; }
            foreach (ListViewItem it in sel) list.Items.Remove(it);
            SaveList();
            Log("已删除 " + sel.Count + " 行。");
        }

        void AddByHand()
        {
            string ip = Interaction.InputBox(
                "请输入学生机的 IP 地址（可以一次输入多个，用逗号或空格分开）：",
                "添加学生机", "");
            if (string.IsNullOrEmpty(ip)) return;

            string[] parts = ip.Split(new char[] { ',', '，', ' ', ';', '；', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            int n = 0;
            foreach (string p in parts)
            {
                string s = p.Trim();
                if (s.Length == 0) continue;
                if (FindItem(s) == null) { AddOrUpdate(s, "", "", "", false, "未测试"); n++; }
            }
            SaveList();
            Log("已添加 " + n + " 个 IP。");
        }

        // =================================================================
        //  扫描
        // =================================================================
        void ScanNetwork()
        {
            if (scanning) { Log("正在扫描中，请稍候..."); return; }
            scanning = true;

            List<string> hosts = Cfg.LocalSubnetHosts(4096);
            if (hosts.Count == 0)
            {
                scanning = false;
                Log("没有找到可扫描的局域网网段，请检查网线/WiFi 连接。");
                return;
            }

            SaveRoom();
            string room = CurrentRoom();
            Log("开始扫描（教室组号 " + room + "），本机 IP：" + Cfg.LocalIpText() + "，共 " + hosts.Count + " 个地址...");
            SetBusy(true);

            ThreadPool.QueueUserWorkItem(delegate
            {
                int found = 0;
                DateTime t0 = DateTime.Now;
                try
                {
                    Parallel.ForEach(hosts, new ParallelOptions { MaxDegreeOfParallelism = 96 },
                        delegate(string ip)
                        {
                            try
                            {
                                string r = Wire.Send(ip, room, "PING", 350);
                                string[] p = Wire.Split(r);
                                if (p.Length >= 4 && p[0] == "OK")
                                {
                                    Interlocked.Increment(ref found);
                                    OnUi(delegate { AddOrUpdate(ip, p[1], p[2], p[3], true, "在线 " + DateTime.Now.ToString("HH:mm:ss")); });
                                }
                            }
                            catch { }
                        });
                }
                catch { }

                int cost = (int)(DateTime.Now - t0).TotalSeconds;
                OnUi(delegate
                {
                    Log("扫描完成：发现 " + found + " 台在线学生机（教室组号 " + room + "），耗时 " + cost + " 秒。");
                    Log("提示：只有“教室组号”与教师端一致的学生机才会出现；未装学生端的电脑不会出现。");
                    SetBusy(false);
                    scanning = false;
                });
            });
        }

        // =================================================================
        //  下发指令
        // =================================================================
        void RunOnChecked(string cmd)
        {
            List<ListViewItem> targets = CheckedItems();
            if (targets.Count == 0)
            {
                Log("还没有勾选任何电脑。请先勾选（或点“全选”），再执行操作。");
                return;
            }
            RunOnItems(targets, cmd);
        }

        void TestChecked()
        {
            List<ListViewItem> targets = CheckedItems();
            if (targets.Count == 0) { Log("还没有勾选任何电脑。"); return; }
            RunOnItems(targets, "PING");
        }

        void RunOnItems(List<ListViewItem> targets, string cmd)
        {
            if (targets == null || targets.Count == 0) { Log("请先选中或勾选电脑。"); return; }

            string action = ActionName(cmd);
            List<string> ips = new List<string>();
            foreach (ListViewItem it in targets) ips.Add(it.SubItems[0].Text);

            SaveRoom();
            string room = CurrentRoom();
            Log("开始" + action + "（教室组号 " + room + "），" + ips.Count + " 台电脑...");
            SetBusy(true);

            ThreadPool.QueueUserWorkItem(delegate
            {
                int ok = 0, fail = 0;
                Parallel.ForEach(ips, new ParallelOptions { MaxDegreeOfParallelism = 64 },
                    delegate(string ip)
                    {
                        string res;
                        try
                        {
                            string r = Wire.Send(ip, room, cmd);
                            string[] p = Wire.Split(r);
                            if (p.Length >= 1 && p[0] == "OK")
                            {
                                Interlocked.Increment(ref ok);
                                res = action + "成功" + (p.Length >= 2 ? "  [" + p[1] + "]" : "");
                                OnUi(delegate { AddOrUpdate(ip, null, null, p.Length >= 2 ? p[1] : null, true, res); });
                            }
                            else
                            {
                                Interlocked.Increment(ref fail);
                                res = p.Length >= 2 && p[1] == "room"
                                    ? action + "失败：教室组号不匹配（该机属于别的教室）"
                                    : action + "失败：" + r;
                                OnUi(delegate { AddOrUpdate(ip, null, null, null, false, res); });
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref fail);
                            res = action + "失败：" + ex.Message;
                            OnUi(delegate { AddOrUpdate(ip, null, null, null, false, res); });
                        }
                        OnUi(delegate { AppendLog(ip + "  " + action + "  ->  " + res); });
                    });

                OnUi(delegate
                {
                    AppendLog("【" + action + "】完成：成功 " + ok + " 台，失败 " + fail + " 台。");
                    SetBusy(false);
                });
            });
        }

        static string ActionName(string cmd)
        {
            if (cmd == "BLACK") return "黑屏";
            if (cmd == "LOCK") return "锁屏";
            if (cmd == "UNLOCK") return "解除";
            return "测试连接";
        }

        // =================================================================
        //  持久化
        // =================================================================
        void SaveList()
        {
            try
            {
                List<string> lines = new List<string>();
                foreach (ListViewItem it in list.Items)
                    lines.Add(it.SubItems[0].Text + "|" + it.SubItems[1].Text + "|" + (it.Checked ? "1" : "0"));
                File.WriteAllLines(StorePath, lines.ToArray());
            }
            catch { }
        }

        void LoadList()
        {
            try
            {
                if (!File.Exists(StorePath)) return;
                string[] lines = File.ReadAllLines(StorePath);
                foreach (string line in lines)
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    string[] p = line.Split('|');
                    string ip = p[0];
                    string name = p.Length > 1 ? p[1] : "";
                    bool chk = p.Length > 2 && p[2] == "1";
                    ListViewItem it = AddOrUpdate(ip, name, "", "", false, "未测试");
                    it.Checked = chk;
                }
                Log("已载入 " + list.Items.Count + " 台电脑的名单。");
            }
            catch { }
        }

        // =================================================================
        //  杂项
        // =================================================================
        void SetBusy(bool busy)
        {
            btnScan.Enabled = !busy;
            btnBlack.Enabled = !busy;
            btnLock.Enabled = !busy;
            btnUnlock.Enabled = !busy;
            btnTest.Enabled = !busy;
        }

        void OnUi(MethodInvoker m)
        {
            try
            {
                if (IsDisposed) return;
                if (IsHandleCreated) BeginInvoke(m);
            }
            catch { }
        }

        void AppendLog(string s)
        {
            if (logBox.IsDisposed) return;
            if (logBox.TextLength > 40000) logBox.Text = logBox.Text.Substring(logBox.TextLength - 20000);
            logBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + s + "\r\n");
        }

        void Log(string s)
        {
            AppendLog(s);
        }

        void ShowHelp()
        {
            string help =
                "【教室组号（重要）】\r\n" +
                "每个教室用一个组号（例如 301、302），教师端和学生端要填成一样的，\r\n" +
                "这样各教室之间就不会互相控制。默认组号是 1。\r\n" +
                "· 教师端：直接在本窗口右上角的“教室组号”里填。\r\n" +
                "· 学生端：安装时会弹框让你填；装好后也可以随时改，\r\n" +
                "  用管理员运行：ClassroomControl.exe /room 新组号（立即生效）。\r\n" +
                "\r\n" +
                "【学生机安装】每台电脑只需要做一次\r\n" +
                "1. 把 ClassroomControl.exe 拷到学生电脑上（U盘即可）。\r\n" +
                "2. 用管理员账号双击运行，第一个弹框问“本机是教师机还是学生机”，点【否】（我是学生机）。\r\n" +
                "3. 接着弹框问“是否现在安装”，点【是】，填写本教室组号，再过一次 UAC 就装好了。\r\n" +
                "   装好后会自动设成开机启动，并自动放行防火墙，以后不用再管。\r\n" +
                "\r\n" +
                "【本机使用】\r\n" +
                "1. 确认“教室组号”填对，点“扫描局域网”，稍等几秒，\r\n" +
                "   本教室的在线学生机会自动出现在列表里并默认勾选。\r\n" +
                "2. 点“黑屏 / 锁屏 / 解除”即可对勾选的电脑批量操作；\r\n" +
                "   也可以右键单台或双击单台单独控制。\r\n" +
                "3. 名单会自动保存在教师端目录的 students.txt 里，下次打开自动载入。\r\n" +
                "\r\n" +
                "【学生机状态说明】\r\n" +
                "黑屏 = 屏幕全黑并显示「老师提醒你上课认真听讲哦」，鼠标键盘还能动；\r\n" +
                "锁屏 = 屏幕全黑 + 屏蔽鼠标键盘（学生完全无法操作）。\r\n" +
                "\r\n" +
                "【应急】\r\n" +
                "如果教师端异常无法解除，可以在学生机上按 Ctrl+Alt+Shift+U 手动解除。\r\n" +
                "建议先在 1~2 台电脑上试通，再全班部署。\r\n" +
                "\r\n" +
                "【常见问题】\r\n" +
                "· 一台都扫不到：确认学生机的“教室组号”和教师端一样；确认学生机已安装并正在运行；\r\n" +
                "  确认学生机和本机在同一网段；确认学生机防火墙放行了 19527 端口（重装一次学生端即可自动放行）。\r\n" +
                "· 提示“教室组号不匹配”：该学生机属于别的教室，把两边组号改成一致即可。\r\n" +
                "· 杀毒软件报警：请把 ClassroomControl.exe 加入白名单，或临时关闭杀毒软件再安装。\r\n" +
                "· 学生机是普通账号：安装那一步必须用管理员账号登录后再执行。";

            MessageBox.Show(this, help, "使用说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
