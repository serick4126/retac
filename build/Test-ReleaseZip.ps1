<#
.SYNOPSIS
  配布用の zip を検査する。リリースの手順で zip を作った後に実行し、失敗したらリリースを止める。

.DESCRIPTION
  R-109 / INV-RELEASE-EXACT-SET: 公式のリリースの zip は、配布物の 6 ファイルを 1 つずつ含み、ほかを含まない。
  ReTAC.exe と ReTAC.Updater.exe のファイルバージョンは、どちらも X.Y.Z.0 である。

  zip の中の exe は実行しない（検査するものを動かさない）。版はファイルのバージョン情報から読むだけにする。
  配布物の名前はこのスクリプトに固定で書く。アップデータの一覧（Distribution.Names）と、テストに書いた 6 つの名前との
  一致は、テスト（ReleaseZipCheckTests）で確かめる。

.PARAMETER Zip
  検査する zip のパス。

.PARAMETER Version
  リリースの版（X.Y.Z）。

.PARAMETER PrintNames
  配布物の名前を 1 行に 1 つ出して終わる（テストが、アップデータの一覧と突き合わせるため）。

.EXAMPLE
  powershell -NoProfile -File build\Test-ReleaseZip.ps1 -Zip publish\ReTAC-2.8.0-win-x64.zip -Version 2.8.0
#>
param(
    [string] $Zip,
    [string] $Version,
    [switch] $PrintNames
)

$ErrorActionPreference = 'Stop'

# 配布物の名前（INV-UPDATER-ANY-MIX）。実行されるのは単独で動く 2 つの exe だけ。足すときは仕様とアップデータの一覧も見直す
$names = @(
    'ReTAC.exe',
    'ReTAC.Updater.exe',
    'LICENSE',
    'README.md',
    'DOTNET-LICENSE.txt',
    'DOTNET-ThirdPartyNotices.txt'
)

if ($PrintNames) {
    $names | ForEach-Object { Write-Output $_ }
    exit 0
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$problems = New-Object System.Collections.Generic.List[string]
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("ReTAC-check-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null

try {
    if ([string]::IsNullOrEmpty($Zip)) { throw '-Zip を指定してください' }
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "版は X.Y.Z の形で指定してください: $Version" }
    $zipPath = (Resolve-Path -LiteralPath $Zip).Path

    $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName })

        foreach ($entry in $entries) {
            if ($entry.Contains('/') -or $entry.Contains('\')) { $problems.Add("フォルダを含んでいる: $entry") }
        }
        $groups = $entries | Group-Object -Property { $_.ToLowerInvariant() } | Where-Object { $_.Count -gt 1 }
        foreach ($group in $groups) { $problems.Add('同じ名前が 2 つ以上ある（大文字小文字を区別しない）: ' + ($group.Group -join ', ')) }

        foreach ($name in $names) {
            if (-not ($entries -ccontains $name)) { $problems.Add("配布物が無い: $name") }
        }
        foreach ($entry in $entries) {
            if (-not ($names -ccontains $entry)) { $problems.Add("配布物の名前に無いファイル: $entry") }
        }

        # 版を読むために 2 つの exe を取り出す（実行はしない）
        foreach ($name in 'ReTAC.exe', 'ReTAC.Updater.exe') {
            $entry = $archive.Entries | Where-Object { $_.FullName -ceq $name } | Select-Object -First 1
            if ($null -ne $entry) { [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $work $name)) }
        }
    }
    finally { $archive.Dispose() }

    $expected = "$Version.0"
    foreach ($name in 'ReTAC.exe', 'ReTAC.Updater.exe') {
        $path = Join-Path $work $name
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $actual = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($path).FileVersion
        if ($actual -ne $expected) { $problems.Add("$name の版が $expected でない: $actual") }
    }
}
catch {
    $problems.Add($_.Exception.Message)
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($problems.Count -gt 0) {
    Write-Output 'NG: 配布用の zip に問題があります。リリースを止めてください。'
    $problems | ForEach-Object { Write-Output "  - $_" }
    exit 1
}
Write-Output "OK: $Zip（v$Version）"
exit 0
