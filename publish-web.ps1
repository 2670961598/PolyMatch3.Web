# 一键发布：编译 WASM 并把前端文件拷进 AppBundle
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$appBundle = Join-Path $root 'src\PolyMatch3.Bridge\bin\Release\net9.0\browser-wasm\AppBundle'

dotnet publish (Join-Path $root 'src\PolyMatch3.Bridge\PolyMatch3.Bridge.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item (Join-Path $root 'web\index.html'), (Join-Path $root 'web\main.js'), (Join-Path $root 'web\manifest.json'), (Join-Path $root 'web\sw.js'), (Join-Path $root 'web\icon-192.png'), (Join-Path $root 'web\icon-512.png') $appBundle -Force
Write-Host "`n发布完成：$appBundle"
Write-Host "试玩：cd `"$appBundle`"; python -m http.server 8080  →  http://localhost:8080"
