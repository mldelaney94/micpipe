# Publishes a self-contained win-x64 MicPipe build with private media tools included.
$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
Set-Location $Root
& "$Root\tools\fetch-tools.ps1"

$Out = Join-Path $Root "artifacts\publish\win-x64"
New-Item -ItemType Directory -Force -Path $Out | Out-Null

dotnet publish "$Root\src\MicPipe\MicPipe.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:Platform=x64 `
    -p:PublishSingleFile=false `
    -p:WindowsAppSDKSelfContained=true `
    -p:WindowsPackageType=None `
    -o $Out

$ToolsOut = Join-Path $Out "tools"
New-Item -ItemType Directory -Force -Path (Join-Path $ToolsOut "ffmpeg") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $ToolsOut "yt-dlp") | Out-Null
Copy-Item (Join-Path $Root "tools\ffmpeg\*") (Join-Path $ToolsOut "ffmpeg") -Force
Copy-Item (Join-Path $Root "tools\yt-dlp\*") (Join-Path $ToolsOut "yt-dlp") -Force

# WinUI XAML is loaded from MicPipe.pri (ms-appx). Ensure it is next to the exe.
$pri = Get-ChildItem -Path (Join-Path $Root "src\MicPipe\bin") -Filter "MicPipe.pri" -Recurse -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if ($null -ne $pri) {
    Copy-Item $pri.FullName -Destination (Join-Path $Out "MicPipe.pri") -Force
    Write-Host "Copied MicPipe.pri into publish output"
} else {
    Write-Warning "MicPipe.pri not found - published app may fail to load XAML."
}

Write-Host "Published to $Out"
Write-Host "Users only need VB-Audio Virtual Cable installed separately."
