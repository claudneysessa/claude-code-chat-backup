# Publica a versão RELEASE (single-file, dependente do runtime .NET) em dist/release.
# Uso: powershell -ExecutionPolicy Bypass -File .\build-release.ps1
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "src\ClaudeChatBackup.csproj"
$output = Join-Path $root "dist\release"

dotnet publish $project -c Release -o $output `
    -p:PublishSingleFile=true `
    -p:SelfContained=false `
    -p:DebugType=embedded `
    -p:PublishTrimmed=false

Write-Host ""
Write-Host "Release publicado em: $output"
Get-ChildItem $output -File | Select-Object Name, Length | Format-Table -AutoSize
