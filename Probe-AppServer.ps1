param([string]$CodexExe)
$ErrorActionPreference = 'Stop'
if (!$CodexExe) {
  $npm = Join-Path $env:APPDATA 'npm\node_modules\@openai\codex'
  $CodexExe = Get-ChildItem -LiteralPath $npm -Filter codex.exe -Recurse | Select-Object -First 1 -ExpandProperty FullName
}
if (!$CodexExe) { throw 'Codex executable not found' }
$si = [Diagnostics.ProcessStartInfo]::new($CodexExe)
$si.Arguments = 'app-server --stdio -c analytics.enabled=false'
$si.UseShellExecute = $false
$si.CreateNoWindow = $true
$si.RedirectStandardInput = $true
$si.RedirectStandardOutput = $true
$si.RedirectStandardError = $true
$p = [Diagnostics.Process]::Start($si)
$stderr = $p.StandardError.ReadToEndAsync()
function Read-Reply([int]$id) {
  $deadline = [DateTime]::UtcNow.AddSeconds(30)
  while ([DateTime]::UtcNow -lt $deadline) {
    $line = $p.StandardOutput.ReadLineAsync()
    $remaining = [int][Math]::Max(1, ($deadline - [DateTime]::UtcNow).TotalMilliseconds)
    if (!$line.Wait($remaining)) { throw 'App-server response timed out' }
    if ($null -eq $line.Result) { throw 'App-server closed its output' }
    $m = $line.Result | ConvertFrom-Json
    if ($m.id -eq $id) { return $m }
  }
  throw 'App-server response timed out'
}
try {
  $p.StandardInput.WriteLine('{"id":1,"method":"initialize","params":{"clientInfo":{"name":"codex_meter_probe","version":"1.0.0"}}}')
  $init = Read-Reply 1
  if ($init.error) { throw ('Initialize failed: RPC code ' + $init.error.code) }
  $p.StandardInput.WriteLine('{"method":"initialized","params":{}}')
  $p.StandardInput.WriteLine('{"id":2,"method":"account/rateLimits/read"}')
  $reply = Read-Reply 2
  if ($reply.error) { Write-Output ('RPC error code: ' + $reply.error.code); Write-Output $reply.error.message; exit 2 }
  $r = $reply.result
  $bucket = $r.rateLimitsByLimitId.codex
  if (!$bucket) { $bucket = $r.rateLimits }
  [ordered]@{
    limitId = $bucket.limitId
    primary = $bucket.primary
    secondary = $bucket.secondary
    availableCount = $r.rateLimitResetCredits.availableCount
    credits = @($r.rateLimitResetCredits.credits | Where-Object { $null -ne $_ } | Select-Object status,resetType,expiresAt)
    creditDetailsAvailable = $null -ne $r.rateLimitResetCredits.credits
  } | ConvertTo-Json -Depth 8
} finally {
  if (!$p.HasExited) { $p.Kill() }
  $p.Dispose()
}
