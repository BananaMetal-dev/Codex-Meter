param([switch]$ShowMeter)
$ErrorActionPreference = 'Stop'
$meter = Join-Path $PSScriptRoot 'bin\Release\net10.0-windows\CodexMeter.exe'
if (!(Test-Path -LiteralPath $meter)) { throw '先にBuild.ps1を実行してください。' }
$launchArgs = @('--with-codex')
if ($ShowMeter) { $launchArgs += '--show' }
Start-Process -FilePath $meter -ArgumentList $launchArgs -WindowStyle Hidden
