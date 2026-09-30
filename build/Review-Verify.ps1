[CmdletBinding()]
param(
    [switch]$SkipDocs
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$previousNodeReuse = $env:MSBUILDDISABLENODEREUSE

# rtk（手元のトークン節約のラッパー）があれば出力を潰さないよう proxy で通し、無ければ dotnet をそのまま呼ぶ
function Invoke-Dotnet {
    if (Get-Command rtk -ErrorAction SilentlyContinue) { & rtk proxy dotnet @args } else { & dotnet @args }
}

function Assert-NativeSuccess([string]$Step) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE."
    }
}

try {
    # レビューの検証は開発側と bin/obj を共有する。直列に走らせ、MSBuild のワーカーを残さない
    $env:MSBUILDDISABLENODEREUSE = '1'
    Push-Location $repoRoot
    try {
        # -m:1 は引用符で渡す。PowerShell の関数に素で渡すと -m: と 1 に分かれ、MSBuild が 1 をプロジェクトとみなす
        Invoke-Dotnet test ReTAC.slnx --no-restore '-m:1' /nodeReuse:false
        Assert-NativeSuccess 'dotnet test'

        Invoke-Dotnet build ReTAC.slnx --no-restore '-m:1' /nodeReuse:false
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
