<#
.SYNOPSIS
  配布用の zip を検査する。リリースの手順で zip を作った後に実行し、失敗したらリリースを止める。

.DESCRIPTION
  R-109 / INV-RELEASE-EXACT-SET: 公式のリリースの zip は、配布物の名前のファイルを 1 つずつ含み、ほかを含まない。
  ReTAC.exe と ReTAC.Updater.exe のファイルバージョンは、どちらも X.Y.Z.0 である。

  配布物の名前は、zip の中の ReTAC.Updater.exe に --list-distribution を渡して読む（アップデータのコードの 1 か所が正本。
  アップデータはこの一覧に無いファイルを含む zip を受け付けないので、リリースするアップデータの一覧と突き合わせる）。

.PARAMETER Zip
  検査する zip のパス。

.PARAMETER Version
  リリースの版（X.Y.Z）。

.EXAMPLE
  powershell -NoProfile -File build\Test-ReleaseZip.ps1 -Zip publish\ReTAC-2.8.0-win-x64.zip -Version 2.8.0
#>
param(
    [Parameter(Mandatory)] [string] $Zip,
    [Parameter(Mandatory)] [string] $Version
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$problems = New-Object System.Collections.Generic.List[string]
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("ReTAC-check-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null

try {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "版は X.Y.Z の形で指定してください: $Version" }
    $zipPath = (Resolve-Path -LiteralPath $Zip).Path

    # --- 項目を読む（書き出す前に全部見る）---
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName })
        foreach ($entry in $entries) {
            if ($entry.EndsWith('/') -or $entry.EndsWith('\') -or $entry.Contains('/') -or $entry.Contains('\')) {
                $problems.Add("フォルダを含んでいる: $entry")
            }
        }
        $groups = $entries | Group-Object -Property { $_.ToLowerInvariant() } | Where-Object { $_.Count -gt 1 }
        foreach ($group in $groups) { $problems.Add("同じ名前が 2 つ以上ある（大文字小文字を区別しない）: " + ($group.Group -join ', ')) }

        # 検査に使うアップデータと、版を読む 2 つの exe を取り出す
        foreach ($name in 'ReTAC.exe', 'ReTAC.Updater.exe') {
            $entry = $archive.Entries | Where-Object { $_.FullName -ceq $name } | Select-Object -First 1
            if ($null -eq $entry) { $problems.Add("$name が無い"); continue }
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $work $name))
        }
    }
    finally { $archive.Dispose() }

    # --- 配布物の名前（アップデータのコードから）---
    $updater = Join-Path $work 'ReTAC.Updater.exe'
    if (Test-Path -LiteralPath $updater) {
        # Start-Process -Wait は子プロセスの終了まで待つ。--list-distribution を知らない古いアップデータは、一時フォルダへ
        # 自分を移して画面を開くので、起動したプロセスだけを上限付きで待ち、残った子（画面）は止める
        $start = New-Object System.Diagnostics.ProcessStartInfo $updater, '--list-distribution'
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $process = [System.Diagnostics.Process]::Start($start)
        $read = $process.StandardOutput.ReadToEndAsync()
        $exited = $process.WaitForExit(15000)
        if (-not $exited) { $process.Kill() }
        Get-CimInstance Win32_Process -Filter "ParentProcessId=$($process.Id) AND Name='ReTAC.Updater.exe'" |
            ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        $names = @($read.Result -split "`r?`n" | Where-Object { $_ -ne '' })
        if (-not $exited -or $process.ExitCode -ne 0 -or $names.Count -eq 0) {
            $problems.Add('ReTAC.Updater.exe から配布物の名前を読めない（--list-distribution に対応していない古いアップデータかもしれない）')
        }
        else {
            foreach ($name in $names) {
                if (-not ($entries -ccontains $name)) { $problems.Add("配布物が無い: $name") }
            }
            foreach ($entry in $entries) {
                if (-not ($names -ccontains $entry)) { $problems.Add("配布物の名前に無いファイル: $entry") }
            }
        }
    }

    # --- 2 つの exe の版 ---
    $expected = "$Version.0"
    foreach ($name in 'ReTAC.exe', 'ReTAC.Updater.exe') {
        $path = Join-Path $work $name
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $actual = (Get-Item -LiteralPath $path).VersionInfo.FileVersion
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
