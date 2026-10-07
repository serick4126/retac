using System.Text.RegularExpressions;

namespace ReTAC.Domain.Tests;

/// <summary>
/// 消す・移す・上書きする呼び出しの基準。製品のコード（src）と実機調査の部品（tests の Probe）で、基準より増えたら失敗する。
/// 足すときは、所有の照合（作ったものだけを消す）や新規作成だけで書くことをテストで確かめてから、理由を添えてここに足す。
/// 減ったときも基準を下げる（緩いままにしない）。2026-10-07 の Phase 25 のインシデント（調査の道具が固定のフォルダの中身を消す作りだった）の再発防止。
/// </summary>
public class DestructiveApiTests
{
    private static readonly Regex Destructive = new(
        @"Directory\.Delete|File\.Delete|\.Delete\(|File\.Move|Directory\.Move|File\.Copy|File\.Replace|" +
        @"WriteAll(Text|Bytes|Lines)|FileMode\.(Create|Truncate|OpenOrCreate)\b|\.MoveTo\(|\.CopyTo\(",
        RegexOptions.Compiled);

    /// <summary>ファイル（リポジトリの根からの相対・/ 区切り）→ 件数と理由。Probe は 0 件（消さない・上書きしない）</summary>
    private static readonly Dictionary<string, (int Count, string Reason)> Baseline = new(StringComparer.OrdinalIgnoreCase)
    {
        ["src/ReTAC.App/ErrorLog.cs"] = (1, "R-126: 1 MB を超えた記録を old に置き換える（File.Move）"),
        ["src/ReTAC.App/MainForm.cs"] = (3, "R-84: 元に戻すでコピー・作成をごみ箱へ送る・空のフォルダだけ消す。R-19: 削除（ごみ箱経由）"),
        ["src/ReTAC.App/SafeFileWrite.cs"] = (2, "V-07: 一時ファイルから置き換える・失敗した一時ファイルを消す"),
        ["src/ReTAC.App/SingleInstance.cs"] = (1, "R-142: 書けるかを試したファイルを消す"),
        ["src/ReTAC.App/TransferExecution.cs"] = (1, "移動の後の空のフォルダを消す（非再帰。中身があれば失敗する）"),
        ["src/ReTAC.Domain/FileOps/FileConcat.cs"] = (1, "ファイルの連結の出力先を作る（FileMode.Create）"),
        ["src/ReTAC.Shell/ShellFileBrowser.cs"] = (1, "誤検出: 文字列のバッファへの写し（Span.CopyTo。ファイル操作ではない）"),
        ["src/ReTAC.Updater/Core/StagingFolder.cs"] = (2, "更新の作業フォルダの中のファイル（リンクは消さない）と、空になった作業フォルダ（非再帰）を消す"),
        ["src/ReTAC.Updater/Core/ZipPackage.cs"] = (1, "誤検出: ストリームへの写し（Stream.CopyTo）"),
        ["src/ReTAC.Updater/ElevatedReplace.cs"] = (1, "昇格した置き換えの結果を書く（WriteAllText）"),
        ["src/ReTAC.Updater/Fetchers.cs"] = (1, "誤検出: ストリームへの写し（Stream.CopyTo）"),
        ["src/ReTAC.Updater/Options.cs"] = (1, "印のファイルを書く（WriteAllText）"),
        ["src/ReTAC.Updater/Program.cs"] = (1, "アップデータ自身を写す（File.Copy。上書きしない）"),
        ["src/ReTAC.Updater/UpdaterForm.cs"] = (1, "進捗のファイルを消す"),
    };

    [Fact]
    public void DestructiveCalls_DoNotExceedBaseline()
    {
        var actual = Count(SchemaManifest.RepoRoot());
        var problems = new List<string>();
        foreach (var (file, count) in actual)
        {
            var allowed = Baseline.TryGetValue(file, out var entry) ? entry.Count : 0;
            if (count > allowed) problems.Add($"{file}: {count} 件（基準 {allowed}）。消す・移す・上書きする呼び出しが増えた");
        }
        foreach (var (file, entry) in Baseline)
        {
            var count = actual.GetValueOrDefault(file);
            if (count < entry.Count) problems.Add($"{file}: {count} 件（基準 {entry.Count}）。基準を下げる");
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Probe_HasNoDestructiveCalls()
    {
        // 実機調査の部品は、消す・移す・上書きする操作を持たない（新規作成だけ）
        var probe = Count(SchemaManifest.RepoRoot()).Where(p => p.Key.Contains("/Probe/", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Empty(probe);
    }

    [Theory]
    [InlineData("Directory.Delete(path, true);", true)]
    [InlineData("File.WriteAllText(path, text);", true)]
    [InlineData("new FileStream(path, FileMode.Create);", true)]
    [InlineData("entry.Delete();", true)]
    [InlineData("new FileStream(path, FileMode.CreateNew);", false)]
    [InlineData("// Directory.Delete はリンク先を消さない", false)]
    [InlineData("public void Delete(string path)", false)]
    public void Detector_FindsDestructiveLines(string line, bool expected) => Assert.Equal(expected, IsDestructive(line));

    private static bool IsDestructive(string line) => !line.TrimStart().StartsWith("//", StringComparison.Ordinal) && Destructive.IsMatch(line);

    private static Dictionary<string, int> Count(string root)
    {
        var files = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "tests"), "*.cs", SearchOption.AllDirectories)
                .Where(f => f.Contains($"{Path.DirectorySeparatorChar}Probe{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)));
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var parts = file.Split(Path.DirectorySeparatorChar);
            if (parts.Contains("bin", StringComparer.OrdinalIgnoreCase) || parts.Contains("obj", StringComparer.OrdinalIgnoreCase)) continue;
            var count = File.ReadLines(file).Count(IsDestructive);
            if (count > 0) counts[Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/')] = count;
        }
        return counts;
    }
}
