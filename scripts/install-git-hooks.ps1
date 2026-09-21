$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

Push-Location $repoRoot
try {
    git config --local user.name 'BananaMetal-dev'
    git config --local user.email '225667808+BananaMetal-dev@users.noreply.github.com'
    git config --local core.hooksPath '.githooks'
    if ($LASTEXITCODE -ne 0) {
        throw 'Git hook configuration failed.'
    }

    node 'scripts/check-public-privacy.mjs' --all
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}
Write-Output 'Repository privacy hooks are enabled.'
