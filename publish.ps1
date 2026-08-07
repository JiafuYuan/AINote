# AINote 打包脚本
# 用法:
#   .\publish.ps1                     # 自动递增补丁版本
#   .\publish.ps1 -Version 0.2.0     # 指定版本
#   .\publish.ps1 -SkipAndroid       # 跳过 Android 打包
#   .\publish.ps1 -SkipRelease       # 跳过 GitHub Release

param(
    [string]$Version = "",
    [switch]$SkipAndroid,
    [switch]$SkipRelease
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$CsProj = Join-Path $RepoRoot "src\AINote\AINote.csproj"
$PublishRoot = Join-Path $RepoRoot "publish"
$Today = Get-Date -Format "yyyyMMdd"

# ── 读取当前版本 ──────────────────────────────────────────────
$csprojContent = Get-Content -Raw $CsProj
if ($csprojContent -match '<Version>([\d\.]+)</Version>') {
    $CurrentVersion = $Matches[1]
} else {
    throw "无法从 csproj 读取版本号"
}

# ── 确定目标版本 ──────────────────────────────────────────────
if ($Version -eq "") {
    $parts = $CurrentVersion.Split('.')
    $parts[-1] = [int]$parts[-1] + 1
    $Version = $parts -join '.'
}
Write-Host "版本: $CurrentVersion -> $Version" -ForegroundColor Cyan

# ── 读取当前 Android versionCode ─────────────────────────────
$currentVersionCode = 1
if ($csprojContent -match '<ApplicationVersion[^>]*>(\d+)</ApplicationVersion>') {
    $currentVersionCode = [int]$Matches[1]
}
$newVersionCode = $currentVersionCode + 1

# ── 升级版本号 (csproj 中 3 个位置 + versionCode) ───────────
$csprojContent = $csprojContent `
    -replace '<Version>[\d\.]+</Version>', "<Version>$Version</Version>" `
    -replace '<InformationalVersion>[\d\.]+</InformationalVersion>', "<InformationalVersion>$Version</InformationalVersion>" `
    -replace '<ApplicationDisplayVersion[^>]*>[\d\.]+</ApplicationDisplayVersion>', "<ApplicationDisplayVersion Condition=`"$([char]36)([MSBuild]::GetTargetPlatformIdentifier('$([char]36)(TargetFramework)')) == 'android'`">$Version</ApplicationDisplayVersion>" `
    -replace '<ApplicationVersion[^>]*>\d+</ApplicationVersion>', "<ApplicationVersion Condition=`"$([char]36)([MSBuild]::GetTargetPlatformIdentifier('$([char]36)(TargetFramework)')) == 'android'`">$newVersionCode</ApplicationVersion>"
[System.IO.File]::WriteAllText($CsProj, $csprojContent)
Write-Host "csproj 版本已更新: $Version (Android versionCode=$newVersionCode)" -ForegroundColor Green

# ── 构建 ──────────────────────────────────────────────────────
Write-Host "`n=== 构建桌面版 ===" -ForegroundColor Yellow
dotnet build $CsProj -f net10.0 -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "桌面构建失败" }

# ── 发布 Windows x64 (自包含单文件) ─────────────────────────
Write-Host "`n=== 发布 Windows x64 ===" -ForegroundColor Yellow
$winDir = Join-Path $PublishRoot "win-x64-$Today"
if (Test-Path $winDir) { Remove-Item -Recurse -Force $winDir }

dotnet publish $CsProj `
    -f net10.0 `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:UseMonoRuntime=false `
    -o $winDir
if ($LASTEXITCODE -ne 0) { throw "Windows 发布失败" }
Write-Host "Windows 发布完成: $winDir" -ForegroundColor Green

# ── 打包 zip ─────────────────────────────────────────────────
$zipName = "AINote-v$Version-win-x64.zip"
$zipPath = Join-Path $PublishRoot $zipName
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
Compress-Archive -Path (Join-Path $winDir "AINote.exe") -DestinationPath $zipPath
Write-Host "ZIP: $zipPath" -ForegroundColor Green

# ── Android APK ──────────────────────────────────────────────
$apkFile = $null
if (-not $SkipAndroid) {
    Write-Host "`n=== 发布 Android APK ===" -ForegroundColor Yellow
    $androidDir = Join-Path $PublishRoot "android-$Today"
    if (Test-Path $androidDir) { Remove-Item -Recurse -Force $androidDir }

    $env:ANDROID_HOME = "$env:LOCALAPPDATA\Android\Sdk"
    $env:ANDROID_SDK_ROOT = "$env:LOCALAPPDATA\Android\Sdk"

    dotnet publish $CsProj `
        -f net10.0-android `
        -c Release `
        -p:AndroidSdkDirectory="$env:LOCALAPPDATA\Android\Sdk" `
        -p:AndroidPackageFormats=apk `
        -o $androidDir
    if ($LASTEXITCODE -ne 0) { throw "Android 发布失败" }

    $apkFile = Get-ChildItem -Path $androidDir -Filter "*.apk" -Recurse | Select-Object -First 1
    if ($apkFile) {
        $finalApk = Join-Path $PublishRoot "AINote-Android-$Version.apk"
        Copy-Item $apkFile.FullName $finalApk -Force
        Write-Host "APK: $finalApk" -ForegroundColor Green
    } else {
        Write-Host "警告: 未找到 APK 文件" -ForegroundColor Yellow
    }
}

# ── GitHub Release ───────────────────────────────────────────
if (-not $SkipRelease) {
    Write-Host "`n=== 创建 GitHub Release ===" -ForegroundColor Yellow
    $releaseArgs = @(
        "release", "create", "v$Version",
        "--repo", "JiafuYuan/AINote",
        "--title", "v$Version",
        "--notes", "AI 记事本 v$Version"
    )
    $releaseArgs += $zipPath
    if ($apkFile -and (Test-Path $finalApk)) {
        $releaseArgs += $finalApk
    }
    & gh @releaseArgs
    if ($LASTEXITCODE -ne 0) { throw "GitHub Release 创建失败" }
    Write-Host "Release 已创建: v$Version" -ForegroundColor Green
}

# ── 完成 ──────────────────────────────────────────────────────
Write-Host "`n打包完成!" -ForegroundColor Cyan
Write-Host "  版本: $Version"
Write-Host "  Windows ZIP: $zipPath"
if ($apkFile -and (Test-Path $finalApk)) {
    Write-Host "  Android APK: $finalApk"
}
Write-Host "`n后续操作:"
Write-Host "  git add -A && git commit -m `"v$Version`" && git push" -ForegroundColor Gray
