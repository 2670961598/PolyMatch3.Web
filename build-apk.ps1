# 一键构建安卓 APK（Capacitor 离线壳，资源内置，无需服务器）
# 前置：Android SDK（%LOCALAPPDATA%\Android\Sdk）+ JDK 21
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

# Capacitor 需要 JDK 21（系统 JAVA_HOME 可能指向其他版本，构建时临时指定）
$env:JAVA_HOME = 'C:\Program Files\Eclipse Adoptium\jdk-21.0.11.10-hotspot'

Push-Location (Join-Path $root 'app')
# 防"套娃"：上一次打出的 APK 也放在 AppBundle 里供下载，sync 前必须清掉，
# 否则它会被一并打进新包（体积翻倍）
$appBundle = Join-Path $root 'src\PolyMatch3.Bridge\bin\Release\net9.0\browser-wasm\AppBundle'
Remove-Item (Join-Path $appBundle 'PolyMatch3.apk') -Force -ErrorAction SilentlyContinue
npx cap sync                                   # 把最新 AppBundle 拷进安卓工程
Set-Location android
.\gradlew assembleDebug --no-daemon            # 出 app\build\outputs\apk\debug\app-debug.apk
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Pop-Location

$apk = Join-Path $root 'app\android\app\build\outputs\apk\debug\app-debug.apk'
Copy-Item $apk (Join-Path $root 'src\PolyMatch3.Bridge\bin\Release\net9.0\browser-wasm\AppBundle\PolyMatch3.apk') -Force
Write-Host "`nAPK 已更新：$apk"
Write-Host "手机下载：http://<局域网IP>:8080/PolyMatch3.apk"
