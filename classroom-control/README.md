# classroom-control · 教室电脑远程控制

> 一款给教室用的电脑远程控制工具。教师一台电脑，一键黑屏 / 锁屏 / 解除。

**一个 exe，同时是教师端和学生端。** 双击弹选择框，选「是」= 教师机，选「否」= 学生机。

![icon](appicon.png)

## 功能

| 操作 | 效果 |
|---|---|
| 黑屏 | 屏幕变黑并显示「老师提醒你上课认真听讲哦」，鼠标键盘仍可操作 |
| 锁屏 | 屏幕变黑并屏蔽鼠标键盘，学生无法操作 |
| 解除 | 恢复正常 |

## 系统要求

- Windows 7 / 10 / 11（64 位 / 32 位均可）
- **无需安装 .NET** —— 依赖 .NET Framework 4.x，Windows 系统自带
- 教师机与学生机必须在**同一个局域网**

## 快速上手

### 1. 教师电脑

1. 双击 `ClassroomControl.exe`
2. 弹框问「教师机还是学生机」→ 点【是】
3. 右上角填教室组号（如 `301`）
4. 点【扫描局域网】→ 勾选电脑 → 点黑屏 / 锁屏 / 解除

### 2. 学生电脑（首次需管理员权限）

1. 把 exe 拷到学生电脑
2. **用管理员账号**双击运行（右键 → 以管理员身份运行）
3. 弹框点【否】→ 进入安装流程
4. 填写教室组号 → 确定
5. 安装完成，之后开机自动启动

## 教室组号隔离

| 教室 | 学生机安装时填 | 教师端填 |
|---|---|---|
| 301 教室 | 301 | 301 |
| 302 教室 | 302 | 302 |

- 可以填数字、字母、中文（如「三年级1班」）
- 两边必须完全一致（英文大小写不区分）
- 组号不一致时教师端扫不到该机器

## 命令行参数

```
ClassroomControl.exe /teacher        # 直接进教师端（不弹选择框）
ClassroomControl.exe /student        # 直接进学生端（静默）
ClassroomControl.exe /install         # 安装学生端（需要管理员）
ClassroomControl.exe /install 301     # 安装并指定教室组号
ClassroomControl.exe /uninstall       # 卸载学生端
ClassroomControl.exe /room 302        # 修改教室组号（已安装机器）
```

## 应急通道

教师端失联时，在学生电脑上按 **Ctrl + Alt + Shift + U** 可立即解除黑屏/锁屏。

## 安全

- 所有通信**仅限局域网内**（TCP 端口 19527），不联网、不上传任何数据
- 内置通信口令防止误操作
- 黑屏/锁屏状态完全本地渲染，无屏幕传输

## 源码与构建

```powershell
# Windows PowerShell
./build.ps1          # 编译输出到 dist/
```

- 编译器：`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`
- 依赖：零第三方 dll，只用 .NET Framework 系统库
- 编译产物：`dist/ClassroomControl.exe`（单文件，约 110 KB）

## 目录结构

```
classroom-control/
├── README.md               ← 本文件
├── src/                    ← C# 源码
│   ├── AssemblyInfo.cs     ← 版本号 / 公司 / 版权
│   ├── Common.cs           ← 协议常量 + 配置
│   ├── Student.cs          ← 学生端逻辑
│   ├── Teacher.cs          ← 教师端逻辑
│   ├── UnifiedLauncher.cs  ← 统一入口
│   ├── Student.manifest
│   └── Teacher.manifest
├── build.ps1               ← 一键编译
├── appicon.ico             ← 图标（嵌入 exe）
├── appicon.png             ← 图标（README 内嵌）
└── dist/                   ← 编译产物（.gitignore）
```

## 版本历史

- **v1.0** — 初始公开版：教师端/学生端统一入口、教室组号隔离、防掉线加固、黑屏提醒文字自定义

## 授权

MIT License.
