# 单向同步：Match3（Unity 权威源码）→ PolyMatch3.Web（纯 C# 复刻）
# 用法：在仓库根（MatchThree\）下执行  powershell -ExecutionPolicy Bypass -File sync-web.ps1 [-DeleteExtras] [-WhatIf]
# 映射（AGENTS.md 同步纪律的机器化）：
#   Match3\Assets\Scripts\PolyMatch3\**（除 Tools 子目录）→ PolyMatch3.Web\src\PolyMatch3\**
#   Match3\Assets\Scripts\PolyMatch3\Tools\**              → PolyMatch3.Web\src\PolyMatch3.Tools\**
#   Match3\Assets\Scripts\PolyMatch3.Samples\**            → PolyMatch3.Web\src\PolyMatch3.Samples\**
# .meta 不拷；Web 独有（PolyMatch3.Game / PolyMatch3.Bridge / Defs 未引入等）不动。
param(
    [switch]$DeleteExtras,   # Web 侧多出的文件也删除（默认只警告）
    [switch]$WhatIf          # 只报告差异，不落盘
)
$ErrorActionPreference = 'Stop'
# 工作区根自动定位：脚本放在 MatchThree\ 或 MatchThree\PolyMatch3.Web\ 下都能跑
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = if (Test-Path (Join-Path $scriptDir 'Match3')) { $scriptDir } else { Split-Path -Parent $scriptDir }
$srcRoot = Join-Path $root 'Match3\Assets\Scripts'
$dstRoot = Join-Path $root 'PolyMatch3.Web\src'

# 不同步清单（同名文件两边归属不同，拷贝会重复/编译失败）：
#   PlayerSwapStep / RevertSwapStep —— Web 侧在 PolyMatch3.Game 项目里（Game 层的输入 Step），
#     Match3 侧在 Samples（Defs 合并带入），语义重复，各管各的；
#   SampleCatalogRegistration —— 依赖 PolyMatch3.Defs（Defs 层尚未引入 Web）。
$skipFiles = @('PlayerSwapStep.cs', 'RevertSwapStep.cs', 'SampleCatalogRegistration.cs')

# (源目录, 目标目录) 映射表
$maps = @(
    @{ Src = 'PolyMatch3';         Dst = 'PolyMatch3';         Exclude = 'Tools' },
    @{ Src = 'PolyMatch3\Tools';   Dst = 'PolyMatch3.Tools';   Exclude = $null },
    @{ Src = 'PolyMatch3.Samples'; Dst = 'PolyMatch3.Samples'; Exclude = $null },
    @{ Src = 'PolyMatch3.Defs';    Dst = 'PolyMatch3.Defs';    Exclude = $null }
)

# 行尾归一后比较（Match3 强制 LF，Web 工作区可能 CRLF，逐字节比会误报"更新"）
function Norm-Hash([string]$path) {
    $text = [System.IO.File]::ReadAllText($path) -replace "`r`n", "`n"
    $sha = New-Object System.Security.Cryptography.SHA256Managed
    $bytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($text))
    return -join ($bytes | ForEach-Object { $_.ToString('x2') })
}

$new = 0; $changed = 0; $same = 0; $extra = 0; $skipped = 0
foreach ($m in $maps) {
    $src = Join-Path $srcRoot $m.Src
    $dst = Join-Path $dstRoot $m.Dst
    if (-not (Test-Path $src)) { Write-Warning "源目录不存在：$src（跳过）"; continue }

    $srcFiles = Get-ChildItem $src -Recurse -Filter *.cs -File |
        Where-Object { $skipFiles -notcontains $_.Name }
    if ($m.Exclude) { $srcFiles = $srcFiles | Where-Object { $_.FullName -notmatch "[\\/]$($m.Exclude)[\\/]" } }

    $srcRel = @{}
    foreach ($f in $srcFiles) {
        $rel = $f.FullName.Substring($src.Length + 1)
        $srcRel[$rel] = $f.FullName
        $target = Join-Path $dst $rel
        if (-not (Test-Path $target)) {
            Write-Host "  新增  $($m.Dst)\$rel" -ForegroundColor Green
            if (-not $WhatIf) { New-Item (Split-Path $target) -ItemType Directory -Force | Out-Null; Copy-Item $f.FullName $target -Force }
            $new++
        } elseif ((Norm-Hash $f.FullName) -ne (Norm-Hash $target)) {
            Write-Host "  更新  $($m.Dst)\$rel" -ForegroundColor Yellow
            if (-not $WhatIf) { Copy-Item $f.FullName $target -Force }
            $changed++
        } else { $same++ }
    }

    # Web 侧多出的文件（跳过构建产物目录）
    if (Test-Path $dst) {
        $dstFiles = Get-ChildItem $dst -Recurse -Filter *.cs -File |
            Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' }
        if ($m.Exclude) { $dstFiles = $dstFiles | Where-Object { $_.FullName -notmatch "[\\/]$($m.Exclude)[\\/]" } }
        foreach ($f in $dstFiles) {
            $rel = $f.FullName.Substring($dst.Length + 1)
            if (-not $srcRel.ContainsKey($rel)) {
                Write-Host "  多出  $($m.Dst)\$rel（Web 侧有、Match3 侧无）" -ForegroundColor Magenta
                if ($DeleteExtras -and -not $WhatIf) { Remove-Item $f.FullName -Force }
                $extra++
            }
        }
    }
}
Write-Host "`n同步完成：新增 $new，更新 $changed，一致 $same，Web 侧多出 $extra（跳过 $($skipFiles.Count) 个不同步文件）" -ForegroundColor Cyan
if ($WhatIf) { Write-Host "（WhatIf 模式，未落盘）" }
if ($extra -gt 0 -and -not $DeleteExtras) { Write-Host "多出文件未处理；确认应删时加 -DeleteExtras" }
