$ErrorActionPreference = "Stop"

$root = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $PSCommandPath }
if (-not $root) { $root = "C:\Users\30297\Desktop\trae_migrate\classroom-lock" }
$src  = Join-Path $root "src"
$dist = Join-Path $root "dist"
$icon = Join-Path $root "appicon.ico"

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $csc)) { throw "找不到 C# 编译器 csc.exe" }

New-Item -ItemType Directory -Force -Path $dist | Out-Null

$refs = @(
    "/r:System.dll",
    "/r:System.Core.dll",
    "/r:System.Drawing.dll",
    "/r:System.Windows.Forms.dll",
    "/r:Microsoft.VisualBasic.dll"
)
$opts = @("/nologo", "/target:winexe", "/codepage:65001", "/optimize+", "/platform:anycpu")

# 单 exe：统一入口（双击弹身份选择），内含教师端 + 学生端全部逻辑
$outExe = Join-Path $dist "ClassroomControl.exe"
Write-Host "==> 编译 ClassroomControl.exe（教师端 + 学生端 统一入口）..."
& $csc @opts @refs `
    "/out:`"$outExe`"" `
    "/win32icon:`"$icon`"" `
    "/win32manifest:`"$src\Student.manifest`"" `
    "$src\AssemblyInfo.cs" "$src\Common.cs" "$src\Student.cs" "$src\Teacher.cs" "$src\UnifiedLauncher.cs"
if ($LASTEXITCODE -ne 0) { throw "编译失败" }

Write-Host ""
Write-Host "==> 编译完成"

# 复制图标文件到发布目录
$iconIcoPath = Join-Path $root "appicon.ico"
$iconPngPath = Join-Path $root "appicon.png"
if (Test-Path -LiteralPath $iconIcoPath) { Copy-Item -LiteralPath $iconIcoPath -Destination $dist -Force }
if (Test-Path -LiteralPath $iconPngPath) { Copy-Item -LiteralPath $iconPngPath -Destination $dist -Force }

Write-Host "==> 输出目录：$dist"
Get-ChildItem $dist | Select-Object Name, Length | Format-Table -AutoSize
