# Publica a versão PORTABLE (single-file self-contained win-x64, sem precisar do runtime .NET)
# em dist/portable.
# Uso: powershell -ExecutionPolicy Bypass -File .\build-portable.ps1
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "src\ClaudeChatBackup.csproj"
$output = Join-Path $root "dist\portable"

dotnet publish $project -c Release -r win-x64 -o $output `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=embedded `
    -p:PublishTrimmed=false

Write-Host ""
Write-Host "Portable publicado em: $output"
Get-ChildItem $output -File | Select-Object Name, Length | Format-Table -AutoSize
