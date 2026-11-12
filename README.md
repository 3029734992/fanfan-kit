# fanfan-kit · 实用工具合集

> 我自己写的、解决实际电脑痛点的小工具合集。
> 每个工具独立目录，独立编译，独立发布。Windows 绿色 exe，解压即用。

---

## 🧰 工具索引

| 工具 | 说明 | 下载 |
|---|---|---|
| [classroom-control](./classroom-control/) | 教室电脑远程控制 —— 一键黑屏 / 锁屏 / 解除 | [v1.0](../../releases/) |

> 更多工具陆续加入…有想要的可以提 [Issue](../../issues)。

## 🗺️ 目录结构

```
fanfan-kit/
├── README.md                 ← 本文件，工具索引页
├── classroom-control/        ← 教室电脑远程控制
│   ├── src/                  ← C# 源码
│   ├── build.ps1             ← 一键编译脚本（Windows PowerShell）
│   ├── dist/                 ← 编译产物（.gitignore，不入库）
│   ├── appicon.ico           ← 图标（嵌入 exe）
│   ├── appicon.png           ← 图标（README 内嵌）
│   └── README.md             ← 工具自己的说明
└── (后续工具：每个一个子目录)
```

## ✨ 共同特点

- **零依赖绿色 exe**：Windows 系统自带 .NET Framework 4.x，不用装运行时
- **单文件分发**：一个 exe 搞定，不拖家带口
- **尽量不联网**：能离线就离线，尊重隐私
- **MIT 协议**：自由修改、自由二次分发

## 开发约定

- 开发语言：C# / WinForms / .NET Framework 4.8
- 编译：Windows 自带 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，**不需要装 .NET SDK**
- 构建脚本：每个工具一个 `build.ps1`，PowerShell 一键出 dist
- 新增工具：复制现有目录做模板，改名字、改图标、改 README

## 📬 反馈与合作

发现 bug？想要什么工具？直接 [开 Issue](../../issues) 告诉我。

---

> 🏷️ Tags: `windows-tools` `windows-desktop` `csharp` `winforms` `green-exe`
