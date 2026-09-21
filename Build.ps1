$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet build (Join-Path $PSScriptRoot 'CodexMeter.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$ws = New-Object -ComObject WScript.Shell
$link = $ws.CreateShortcut((Join-Path $PSScriptRoot 'Codex + Meter.lnk'))
$link.TargetPath = Join-Path $PSScriptRoot 'bin\Release\net10.0-windows\CodexMeter.exe'
$link.Arguments = '--with-codex'
$link.WorkingDirectory = $PSScriptRoot
$link.Description = 'Codex Desktop と Codex Meter を起動'
$link.WindowStyle = 7
$link.Save()
