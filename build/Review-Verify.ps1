[CmdletBinding()]
param(
    [switch]$SkipDocs
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$previousNodeReuse = $env:MSBUILDDISABLENODEREUSE

function Assert-NativeSuccess([string]$Step) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE."
    }
}

try {
    # Review runs share bin/obj with the developer. Keep them sequential and do not leave MSBuild nodes behind.
    $env:MSBUILDDISABLENODEREUSE = '1'
    Push-Location $repoRoot
    try {
        & rtk proxy dotnet test ReTAC.slnx --no-restore -m:1 /nodeReuse:false
        Assert-NativeSuccess 'dotnet test'

        & rtk proxy dotnet build ReTAC.slnx --no-restore -m:1 /nodeReuse:false
        Assert-NativeSuccess 'dotnet build'
    }
    finally {
        Pop-Location
    }

    if (!$SkipDocs) {
        $commonGitDir = (& git -C $repoRoot rev-parse --path-format=absolute --git-common-dir).Trim()
        Assert-NativeSuccess 'git rev-parse --git-common-dir'
        $mainRoot = Split-Path -Parent $commonGitDir
        $docsScript = Join-Path $mainRoot 'docs\tools\Review-Verify.ps1'
        if (!(Test-Path -LiteralPath $docsScript)) {
            throw "docs review script was not found: $docsScript"
        }
        & $docsScript
    }
}
finally {
    $env:MSBUILDDISABLENODEREUSE = $previousNodeReuse
}
