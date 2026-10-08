using System.Text.RegularExpressions;

namespace ReTAC.Domain.Tests;

/// <summary>
/// 消す・移す・上書きする呼び出しの基準。製品のコード（src）と実機調査の部品（tests の Probe）で、呼び出しの数が基準より増えたら失敗する。
/// - 既存（Legacy）: 2026-10-07 に軽く確認し、判定（問題なし・誤検出・要確認）を付けたもの。正常に動いているので変えない。増やさない。
/// - 厳格（Strict）: それより後に足すもの。S8（識別子による照合ができないものの削除には一切手を付けない。CLAUDE.md）を満たすことを、
///   理由に「S8:」で書く。所有の照合や新規作成だけで書くことをテストで確かめてから足す。
/// 減ったときも基準を下げる（緩いままにしない）。2026-10-07 の Phase 25 のインシデント（調査の道具が固定のフォルダの中身を消す作りだった）の再発防止。
/// </summary>
public class DestructiveApiTests
{
    private static readonly Regex Destructive = new(
        @"Directory\.Delete|File\.Delete|\.Delete\(|File\.Move|Directory\.Move|File\.Copy|File\.Replace|" +
        @"WriteAll(Text|Bytes|Lines)|FileMode\.(Create|Truncate|OpenOrCreate)\b|\.MoveTo\(|\.CopyTo\(|" +
        // Windows のファイル操作（ShellFileOperation の Move・Copy・Rename と、IFileOperation の各項目の操作）
        @"\.Move\(|\.Copy\(|\.Rename\(|\b(Move|Copy|Delete|Rename)Items?\(",
        RegexOptions.Compiled);

    /// <summary>既存のファイルの数。2026-10-07 の確認で固定した。増やさない（新しいものは Strict に足す）</summary>
    private const int LegacyFileCount = 26;

    /// <summary>2026-10-07 に確認した既存の呼び出し（ファイル → 件数と判定）。判定は「問題なし:」「誤検出:」「要確認:」で始める</summary>
    private static readonly Dictionary<string, (int Count, string Judgment)> Legacy = new(StringComparer.OrdinalIgnoreCase)
    {
        ["src/ReTAC.App/BookmarkDialog.cs"] = (2, "誤検出: ブックマークの並べ替え（BookmarkRules.Move）"),
        ["src/ReTAC.App/BookmarkDropZone.cs"] = (1, "誤検出: ブックマークの並べ替え（BookmarkRules.Move）"),
        ["src/ReTAC.App/BookmarkTreeView.cs"] = (1, "誤検出: ブックマークの並べ替え（BookmarkRules.Move）"),
        ["src/ReTAC.App/ErrorLog.cs"] = (1, "問題なし: R-126。ReTAC が決まった名前で管理する retac.error.old.log を置き換える（File.Move）"),
        ["src/ReTAC.App/FileListView.cs"] = (1, "誤検出: 投げ縄の移動（_lasso.Move）"),
        ["src/ReTAC.App/MacroBuildPromptButton.cs"] = (1, "誤検出: プロンプトのクリップボードへのコピー（MacroBuildPromptActions.Copy）"),
        ["src/ReTAC.App/MainForm.cs"] = (8, "問題なし: R-84 の元に戻す（名前を戻す・移し戻す・ごみ箱へ・記録した空のフォルダだけ消す）、R-19 の削除（ごみ箱経由）、" +
            "R-63 の別名複写（OS が衝突を確認）、名前の変更（利用者の操作）。ほかに誤検出 1（MacroBuildPromptActions.Copy）"),
        ["src/ReTAC.App/PreviewView.cs"] = (1, "誤検出: 文字のクリップボードへのコピー（TextPreviewInput.Copy）"),
        ["src/ReTAC.App/PromptSettingsDialog.cs"] = (5, "誤検出: 入力ダイアログの項目の削除・並べ替え（画面の中の一覧）"),
        ["src/ReTAC.App/QuickAccessPage.cs"] = (1, "誤検出: クイックアクセスの並べ替え（List.Move）"),
        ["src/ReTAC.App/SafeFileWrite.cs"] = (2, "問題なし: V-07。同じ呼び出しで新規作成（GUID の名前・CreateNew）した一時ファイルで置き換え、失敗したらその一時ファイルだけを消す"),
        ["src/ReTAC.App/SingleInstance.cs"] = (1, "問題なし: R-142。直前に自分で書いた試しのファイル（プロセス ID 入りの名前）を消す"),
        ["src/ReTAC.App/TransferExecution.cs"] = (3, "問題なし: R-41-4 の計画に従う移動・コピーと、移動の後の空のフォルダの削除（非再帰。中身があれば失敗する）"),
        ["src/ReTAC.Domain/FileOps/FileConcat.cs"] = (1, "要確認: 既にある連結先を、確認なしで切り詰めて上書きする（FileMode.Create）。連結元と同じなら断る。" +
            "既存の動作は変えず、利用者に報告した（2026-10-07）"),
        ["src/ReTAC.Domain/Selection/LassoGesture.cs"] = (3, "誤検出: 投げ縄の移動（lasso.Move）"),
        ["src/ReTAC.Shell/DragImageWindow.cs"] = (1, "誤検出: メモリの写し（Marshal.Copy）"),
        ["src/ReTAC.Shell/ShellFileBrowser.cs"] = (2, "誤検出: 文字列のバッファへの写し（Span.CopyTo・Marshal.Copy）"),
        ["src/ReTAC.Shell/ShellFileOperation.cs"] = (12, "問題なし: Windows のファイル操作の入口（Copy・CopyIdList・Move・Delete・Rename）と、IFileOperation の宣言。" +
            "何を消す・移すかは呼び出し側が決め、呼び出し側がこの基準に載る"),
        ["src/ReTAC.Shell/ShellImageInterop.cs"] = (1, "誤検出: メモリの写し（Marshal.Copy）"),
        ["src/ReTAC.Updater/Core/StagingFolder.cs"] = (2, "問題なし: 今回書いたと記録したファイルだけ（リンクは消さない）と、空になった準備フォルダ（非再帰）を消す"),
        ["src/ReTAC.Updater/Core/ZipPackage.cs"] = (1, "誤検出: ストリームへの写し（Stream.CopyTo）"),
        ["src/ReTAC.Updater/ElevatedReplace.cs"] = (1, "問題なし: アップデータの作業フォルダの progress.txt に結果を書く"),
        ["src/ReTAC.Updater/Fetchers.cs"] = (1, "誤検出: ストリームへの写し（Stream.CopyTo）"),
        ["src/ReTAC.Updater/Options.cs"] = (1, "問題なし: デバッグの一時停止の印を作業フォルダに書く（デバッグの引数のときだけ）"),
        ["src/ReTAC.Updater/Program.cs"] = (1, "問題なし: 新しく作った作業フォルダへアップデータ自身を写す（File.Copy。上書きしない）"),
        ["src/ReTAC.Updater/UpdaterForm.cs"] = (1, "問題なし: アップデータの作業フォルダの progress.txt を消す"),
    };

    /// <summary>2026-10-07 より後に足すもの（ファイル → 件数と理由）。理由は「S8:」で、照合して消すこと・新規作成だけで書くことを書く</summary>
    private static readonly Dictionary<string, (int Count, string Reason)> Strict = new(StringComparer.OrdinalIgnoreCase)
    {
        ["src/ReTAC.App/ArchiveExtraction.cs"] = (2, "S8: R-146-1。この処理が作った一時フォルダだけを、作ったときの識別子（ボリュームとファイル番号）と照合し、リンクでないことを確かめて消す（Directory.Delete と、その呼び出し）"),   // 安全: 基準の表の文字列（呼び出しではない）
        ["src/ReTAC.Shell/ShellFileOperation.cs"] = (1, "S8: R-146-1。CopyIdList の CopyItem。複写先は ReTAC が新しく作り識別子で照合した一時フォルダで、既にある物を消さず、書庫の側は読むだけ"),   // 安全: 基準の表の文字列（呼び出しではない）
    };

    /// <summary>
    /// 後から足した（Strict の）呼び出しの行に付ける印。同じファイルに既存の呼び出しと混ざるとき、行ごとにどちらの表かを決める
    /// （合計だけで見ると、既存の 1 件が消え、別の 1 件が足されても相殺されて見逃す）
    /// </summary>
    private const string StrictMarker = "安全: S";

    [Fact]
    public void DestructiveCalls_DoNotExceedBaseline()
    {
        var (legacy, strict) = Count(SchemaManifest.RepoRoot());
        var problems = new List<string>();
        Compare("Legacy", legacy, Legacy.ToDictionary(p => p.Key, p => p.Value.Count, StringComparer.OrdinalIgnoreCase), problems);
        Compare("Strict", strict, Strict.ToDictionary(p => p.Key, p => p.Value.Count, StringComparer.OrdinalIgnoreCase), problems);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>表ごと・ファイルごとに、増えても減っても失敗にする（Legacy は凍結。Strict は印のある行）</summary>
    private static void Compare(string table, Dictionary<string, int> actual, Dictionary<string, int> baseline, List<string> problems)
    {
        foreach (var file in actual.Keys.Concat(baseline.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var now = actual.GetValueOrDefault(file);
            var allowed = baseline.GetValueOrDefault(file);
            if (now > allowed) problems.Add($"{file}: {table} {now} 件（基準 {allowed}）。消す・移す・上書きする呼び出しが増えた。S8 を満たすことを確かめ、その行に「// {StrictMarker}…」の印を付けて Strict に理由を書いて足す");
            else if (now < allowed) problems.Add($"{file}: {table} {now} 件（基準 {allowed}）。基準を下げる");
        }
    }

    [Fact]
    public void Legacy_IsFrozenAndJudged()
    {
        // 既存の表は増やさない。新しいものは Strict に、S8 の理由を付けて足す
        Assert.Equal(LegacyFileCount, Legacy.Count);
        Assert.All(Legacy, p => Assert.Matches("^(問題なし|誤検出|要確認): ", p.Value.Judgment));
    }

    [Fact]
    public void Strict_ReasonsStateS8()
    {
        Assert.All(Strict, p => Assert.StartsWith("S8: ", p.Value.Reason));
    }

    [Fact]
    public void ArchiveExtraction_OnlyDeleteCallsRemove()
    {
        // S8: 一時フォルダを消す入口は OwnedTempFolder.Delete（識別子の照合の後）だけ。
        // 基準は「.Delete(」の件数なので、Remove を Delete の外から呼んでも数が変わらず、検出できない。位置で守る
        var text = File.ReadAllText(Path.Combine(SchemaManifest.RepoRoot(), "src/ReTAC.App/ArchiveExtraction.cs"));
        var start = text.IndexOf("public bool Delete()", StringComparison.Ordinal);
        Assert.True(start > 0);
        // Delete の本体（波括弧の対応で切る）。位置の近さには頼らない
        var open = text.IndexOf('{', start);
        var depth = 0;
        var end = open;
        for (; end < text.Length; end++)
        {
            if (text[end] == '{') depth++;
            else if (text[end] == '}' && --depth == 0) break;
        }
        var remove = new Regex(@"(?<![\w.])Remove\(");
        Assert.Equal(2, remove.Count(text[open..(end + 1)]));
        // ファイル全体では、本体の 2 件と宣言の 1 件だけ
        Assert.Equal(3, remove.Count(text));
    }

    [Fact]
    public void Probe_HasNoDestructiveCalls()
    {
        // 実機調査の部品は、消す・移す・上書きする操作を持たない（新規作成だけ）
        var (legacy, strict) = Count(SchemaManifest.RepoRoot());
        var probe = legacy.Concat(strict).Where(p => p.Key.Contains("/Probe/", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Empty(probe);
    }

    [Theory]
    [InlineData("Directory.Delete(path, true);", 1)]
    [InlineData("File.WriteAllText(path, text);", 1)]
    [InlineData("new FileStream(path, FileMode.Create);", 1)]
    [InlineData("entry.Delete();", 1)]
    [InlineData("File.Copy(self, copy);", 1)]
    // Windows のファイル操作（レビューの指摘: 見逃していた）
    [InlineData("operation.Move(item.Source, item.DestinationFolder, item.NewName);", 1)]
    [InlineData("operation.Copy(item.Source, item.DestinationFolder, item.NewName);", 1)]
    [InlineData("operation.Rename(path, name);", 1)]
    [InlineData("Check(_operation.MoveItem(Item(source), Destination(folder), newName, IntPtr.Zero));", 1)]
    [InlineData("Check(_operation.CopyItem(item, Destination(folder), null, IntPtr.Zero));", 1)]
    [InlineData("Check(_operation.DeleteItem(Item(path), IntPtr.Zero));", 1)]
    [InlineData("Check(_operation.RenameItem(Item(path), newName, IntPtr.Zero));", 1)]
    [InlineData("_operation.DeleteItems(items);", 1)]
    // 同じ行の複数の呼び出しは、それぞれ数える（レビューの指摘: 行で数えていた）
    [InlineData("Directory.Delete(a); File.Delete(b);", 2)]
    [InlineData("File.Delete(a); File.Delete(b); operation.Move(c, d);", 3)]
    [InlineData("new FileStream(path, FileMode.CreateNew);", 0)]
    [InlineData("// Directory.Delete はリンク先を消さない", 0)]
    [InlineData("public void Delete(string path)", 0)]
    public void Detector_CountsEachCall(string line, int expected) => Assert.Equal(expected, CallsIn(line));

    [Fact]
    public void Baseline_FailsWhenCallAddedToExistingLine() => Sta.WithTempFolder(root =>
    {
        // 既存の行に削除を足しても、件数が増えて基準を超える
        var file = Path.Combine(root, "x.cs");
        File.WriteAllText(file, "    File.Delete(a);\n");   // 安全: テストが新しく作ったフォルダの中
        var before = File.ReadLines(file).Sum(CallsIn);
        File.WriteAllText(file, "    File.Delete(a); Directory.Delete(b, true);\n");   // 安全: 同上
        Assert.Equal(before + 1, File.ReadLines(file).Sum(CallsIn));
    });

    private static int CallsIn(string line) =>
        line.TrimStart().StartsWith("//", StringComparison.Ordinal) ? 0 : Destructive.Matches(line).Count;

    private static (Dictionary<string, int> Legacy, Dictionary<string, int> Strict) Count(string root)
    {
        var files = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "tests"), "*.cs", SearchOption.AllDirectories)
                .Where(f => f.Contains($"{Path.DirectorySeparatorChar}Probe{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)));
        var legacy = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var strict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var parts = file.Split(Path.DirectorySeparatorChar);
            if (parts.Contains("bin", StringComparer.OrdinalIgnoreCase) || parts.Contains("obj", StringComparer.OrdinalIgnoreCase)) continue;
            var key = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            var lines = File.ReadLines(file).ToList();
            var strictCount = lines.Where(l => l.Contains(StrictMarker, StringComparison.Ordinal)).Sum(CallsIn);
            var legacyCount = lines.Where(l => !l.Contains(StrictMarker, StringComparison.Ordinal)).Sum(CallsIn);
            if (legacyCount > 0) legacy[key] = legacyCount;
            if (strictCount > 0) strict[key] = strictCount;
        }
        return (legacy, strict);
    }
}
