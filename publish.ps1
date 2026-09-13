# Publishes a self-contained win-x64 MicPipe folder. Tools and MicPipe.pri are copied by the project itself.
$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
& "$Root\tools\fetch-tools.ps1"

$Out = Join-Path $Root "artifacts\publish\win-x64"
dotnet publish "$Root\src\MicPipe\MicPipe.csproj" -c Release -r win-x64 --self-contained -p:Platform=x64 -o $Out

Write-Host "Published to $Out"
Write-Host "Users only need VB-Audio Virtual Cable installed separately."
