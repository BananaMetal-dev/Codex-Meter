param([string]$IsccPath)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.nuget'
if (!$IsccPath) {
    $candidates = @((Join-Path $PSScriptRoot '.tools\InnoSetup\ISCC.exe'), (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'))
    $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (!$IsccPath) { throw 'Inno Setup 6のISCC.exeを-IsccPathで指定してください。' }
[xml]$project = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'CodexMeter.csproj')
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must be major.minor.patch' }
# Fresh isolated staging excludes state and diagnostics left by previous runs.
$stage = Join-Path $PSScriptRoot ('artifacts\staging-' + [Guid]::NewGuid().ToString('N'))
$output = Join-Path $PSScriptRoot 'artifacts\release'
dotnet publish (Join-Path $PSScriptRoot 'CodexMeter.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $stage --nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $stage
New-Item -ItemType Directory -Path (Join-Path $stage 'docs') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\screenshot.png') -Destination (Join-Path $stage 'docs\screenshot.png')
$licenseDir = Join-Path $stage 'licenses'
New-Item -ItemType Directory -Path $licenseDir -Force | Out-Null
foreach ($package in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')) {
    $folder = Join-Path $env:NUGET_PACKAGES $package
    if (Test-Path -LiteralPath $folder) {
        Get-ChildItem -LiteralPath $folder -Recurse -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES|ThirdPartyNotices)(\.txt)?$' } | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $licenseDir ($package + '-' + $_.Directory.Name + '-' + $_.Name))
        }
    }
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
& $IsccPath '/Qp' "/DAppVersion=$version" "/DPublishDir=$stage" "/DOutputPath=$output" (Join-Path $PSScriptRoot 'installer\CodexMeter.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
$installer = Join-Path $output 'CodexMeter-Setup.exe'
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), "$hash  CodexMeter-Setup.exe`n")
Write-Output $installer

