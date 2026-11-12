using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ClassLock
{
    /// <summary>全局图标缓存：优先读同目录 appicon.ico，失败则从 exe 内嵌图标提取</summary>
    internal static class AppIcon
    {
        static Icon _icon;
        public static Icon Loaded
        {
            get
            {
                if (_icon != null) return _icon;
                try
                {
                    string ico = Path.Combine(Application.StartupPath, "appicon.ico");
                    if (File.Exists(ico)) { _icon = new Icon(ico); return _icon; }
                    _icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                }
                catch { }
                return _icon;
            }
        }
    }

    // =====================================================================
    //  统一入口 —— 一个 exe 同时承担教师端 / 学生端两种身份
    //  分发默认双击弹选择框；命令行参数直接走对应分支（供看护/脚本调用）
    // =====================================================================
    internal static class UnifiedLauncher
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // 预加载图标（失败无所谓，后续 Form 构造里会兜底）
            var _ = AppIcon.Loaded;

            // 解析参数（不区分大小写）
            List<string> a = new List<string>();
            foreach (string s in args) a.Add(s.ToLowerInvariant());

            // ---- 明确的学生端命令（安装/卸载/改组号/看护/指定状态启动） ----
            if (a.Contains("/install") || a.Contains("-install")
                || a.Contains("/uninstall") || a.Contains("-uninstall")
                || a.Contains("/room") || a.Contains("-room")
                || a.Contains("/watch") || a.Contains("-watch")
                || a.Contains("/state") || a.Contains("-state"))
            {
                Program.Run(args);
                return;
            }

            // ---- 明确的教师端 ----
            if (a.Contains("/teacher") || a.Contains("-teacher"))
            {
                TeacherProgram.Run();
                return;
            }

            // ---- 明确的学生端静默运行（不走"未安装引导"） ----
            if (a.Contains("/student") || a.Contains("-student"))
            {
                Program.Run(args);
                return;
            }

            // ---- 无参数：弹选择框 ----
            ShowIdentityDialog();
        }

        static void ShowIdentityDialog()
        {
            DialogResult dr = MessageBox.Show(
                "课堂电脑控制\n\n" +
                "本机是教师机还是学生机？\n\n" +
                "  【是】  我是教师机 —— 打开教师端，扫描局域网控制其他电脑\n" +
                "  【否】  我是学生机 —— 安装学生端，等待教师机控制\n\n" +
                "（学生电脑请用管理员账号运行本程序）",
                Cfg.ProductName,
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1);

            if (dr == DialogResult.Yes)
            {
                TeacherProgram.Run();
            }
            else if (dr == DialogResult.No)
            {
                Program.Run(new string[0]);
            }
            // Cancel = 直接退出
        }
    }
}
