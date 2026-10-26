$ErrorActionPreference = "Stop"
Get-Process GameGuruMax.ModelViewer -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

$out = Join-Path $PSScriptRoot "dist\GameGuruMax.ModelViewer"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

dotnet publish (Join-Path $PSScriptRoot "src\GameGuruMax.ModelViewer\GameGuruMax.ModelViewer.csproj") `
  -c Release -r win-x64 --self-contained true -o $out `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=None `
  -p:DebugSymbols=false

$zip = Join-Path $PSScriptRoot "dist\GameGuruMax.ModelViewer-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip -Force

Get-ChildItem (Join-Path $out "GameGuruMax.ModelViewer.exe"), $zip |
  Format-Table Name, LastWriteTime, @{ N = "MB"; E = { [math]::Round($_.Length / 1MB, 2) } }
