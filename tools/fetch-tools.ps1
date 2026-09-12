# Downloads private media tooling into tools/ for bundling. End users never run this.
$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$FfmpegDir = Join-Path $Root "ffmpeg"
$YtDlpDir = Join-Path $Root "yt-dlp"

New-Item -ItemType Directory -Force -Path $FfmpegDir | Out-Null
New-Item -ItemType Directory -Force -Path $YtDlpDir | Out-Null

$ytDlpPath = Join-Path $YtDlpDir "yt-dlp.exe"
if (-not (Test-Path $ytDlpPath)) {
    Write-Host "Fetching URL extractor..."
    Invoke-WebRequest -Uri "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe" -OutFile $ytDlpPath
}

$ffmpegExe = Join-Path $FfmpegDir "ffmpeg.exe"
if (-not (Test-Path $ffmpegExe)) {
    Write-Host "Fetching media transcoder..."
    $zipPath = Join-Path $env:TEMP "micpipe-ffmpeg.zip"
    # Essentials build — small, enough for audio cut/transcode
    Invoke-WebRequest -Uri "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip" -OutFile $zipPath
    $extract = Join-Path $env:TEMP "micpipe-ffmpeg-extract"
    if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
    Expand-Archive -Path $zipPath -DestinationPath $extract -Force
    $bin = Get-ChildItem -Path $extract -Recurse -Filter "ffmpeg.exe" | Select-Object -First 1
    if (-not $bin) { throw "ffmpeg.exe not found in archive" }
    Copy-Item $bin.FullName -Destination $ffmpegExe -Force
    $ffprobe = Get-ChildItem -Path $extract -Recurse -Filter "ffprobe.exe" | Select-Object -First 1
    if ($ffprobe) {
        Copy-Item $ffprobe.FullName -Destination (Join-Path $FfmpegDir "ffprobe.exe") -Force
    }
    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
    Remove-Item $extract -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Tools ready under $Root"
