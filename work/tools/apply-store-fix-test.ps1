param(
    [string]$ApkPath = ""
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($ApkPath)) {
    $ApkPath = Join-Path $projectRoot "dist\影之刃1_64位修复版_商城数量发放修复.apk"
}

$adb = "D:\Netease\MuMuPlayer\nx_main\adb.exe"
if (-not (Test-Path -LiteralPath $adb)) {
    throw "MuMu adb was not found: $adb"
}
if (-not (Test-Path -LiteralPath $ApkPath)) {
    throw "Patched APK was not found: $ApkPath"
}

$apk = (Resolve-Path -LiteralPath $ApkPath).Path
& $adb connect 127.0.0.1:7555 | Out-Host
& $adb root | Out-Host

$packageLine = (& $adb shell pm path m.Sgame.yzr | Select-Object -First 1).Trim()
if (-not $packageLine.StartsWith("package:")) {
    throw "m.Sgame.yzr is not installed in the running MuMu player."
}
$installedApk = $packageLine.Substring("package:".Length)

& $adb shell am force-stop m.Sgame.yzr
& $adb push $apk /data/local/tmp/m.Sgame.yzr.patched.apk | Out-Host
& $adb shell "cp /data/local/tmp/m.Sgame.yzr.patched.apk '$installedApk'"
& $adb shell "chown system:system '$installedApk'; chmod 644 '$installedApk'; restorecon '$installedApk'"
& $adb shell am start -n m.Sgame.yzr/.AdapterActivity | Out-Host

Write-Output "Patched APK applied to the existing MuMu installation."
Write-Output "Installed package with its existing save data."
