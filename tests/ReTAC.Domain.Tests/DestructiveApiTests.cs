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
        @"\.Move\(|\.Copy\(|\.Rename\(|\b(Move|Copy|Delete|Rename)Items?\(|" +
        // ハンドルに削除の印を付けて消す手段（FileDispositionInfo。ハンドルを閉じるときに消える）。見張りの抜け穴にしない
        @"\bSetFileInformationByHandle\(|" +
        // そのほかの消す・移す手段（Win32 の API、閉じると消す指定、新しい削除の指定）。名前の一致だけの見張りの抜け穴にしない
        @"\b(DeleteFile|RemoveDirectory|MoveFileEx|MoveFile|ReplaceFile|CopyFileEx|CopyFile)[AW]?\(|" +
        @"\b(NtSetInformationFile|ZwSetInformationFile|SHFileOperation)[AW]?\(|" +
        @"FILE_FLAG_DELETE_ON_CLOSE|FileOptions\.DeleteOnClose|FileDispositionInfoEx|FileDispositionInformationEx|" +
        // NtCreateFile は呼び出しそのものは数えない（FILE_CREATE の作成だけなら消す・移す・上書きする手段ではない）。
        // 消す・上書きする指定（閉じると消す・既存を置き換える disposition）が付いたときだけ数える
        @"\bFILE_DELETE_ON_CLOSE\b|\bFILE_(SUPERSEDE|OVERWRITE|OVERWRITE_IF)\b",
        RegexOptions.Compiled);

    /// <summary>既存のファイルの数。2026-10-07 の確認で固定した。増やさない（新しいものは Strict に足す）</summary>
    private const int LegacyFileCount = 27;   // 26 + Replacer.cs（検出の対象を広げて数え始めた、2026-10-07 より前からあるコード）

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
        ["src/ReTAC.Updater/Core/ZipPackage.cs"] = (2, "誤検出: ストリームへの写し（Stream.CopyTo）。問題なし: 新規作成（CreateNew）したダウンロード先のファイルを、閉じたら消す（DeleteOnClose）。" +
            "検出の対象を広げたとき（2026-10-08）に初めて数えた。コードは 2026-10-07 より前からある"),
        ["src/ReTAC.Updater/Core/Replacer.cs"] = (2, "問題なし: R-109-4。準備フォルダのファイルを正規の名前へ MoveFileEx で付け替える（呼び出しと宣言）。" +
            "検出の対象を広げたとき（2026-10-08）に初めて数えた。コードは 2026-10-07 より前からある"),
        ["src/ReTAC.Updater/ElevatedReplace.cs"] = (1, "問題なし: アップデータの作業フォルダの progress.txt に結果を書く"),
        ["src/ReTAC.Updater/Fetchers.cs"] = (1, "誤検出: ストリームへの写し（Stream.CopyTo）"),
        ["src/ReTAC.Updater/Options.cs"] = (1, "問題なし: デバッグの一時停止の印を作業フォルダに書く（デバッグの引数のときだけ）"),
        ["src/ReTAC.Updater/Program.cs"] = (1, "問題なし: 新しく作った作業フォルダへアップデータ自身を写す（File.Copy。上書きしない）"),
        ["src/ReTAC.Updater/UpdaterForm.cs"] = (1, "問題なし: アップデータの作業フォルダの progress.txt を消す"),
    };

    /// <summary>2026-10-07 より後に足すもの（ファイル → 件数と理由）。理由は「S8:」で、照合して消すこと・新規作成だけで書くことを書く</summary>
    private static readonly Dictionary<string, (int Count, string Reason)> Strict = new(StringComparer.OrdinalIgnoreCase)
    {
        ["src/ReTAC.Shell/ShellFileOperation.cs"] = (1, "S8: R-146-1。CopyIdList の CopyItem。書庫の中の項目を宛先へ複製するだけで、何も消さず移さない。宛先の既存の同名は呼び出し側が silentOverwrite: false で Windows の確認画面に任せる"),   // 安全: 基準の表の文字列（呼び出しではない）
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
    public void ArchiveExtractionFile_IsGone()
    {
        // S4・S8: R-146-1。書庫の項目は Windows のコピーで宛先へ直接コピーする。一時フォルダを消す処理（ArchiveExtraction.cs）は戻さない
        var root = SchemaManifest.RepoRoot();
        Assert.False(File.Exists(Path.Combine(root, "src/ReTAC.App/ArchiveExtraction.cs")));
        Assert.DoesNotContain(Legacy.Keys.Concat(Strict.Keys), k => k.EndsWith("ArchiveExtraction.cs", StringComparison.OrdinalIgnoreCase));
        // 陽性対照: 表は実在するファイルを指している（空の表で通っているのではない）
        Assert.Contains("src/ReTAC.Shell/ShellFileOperation.cs", Strict.Keys);
    }

    [Fact]
    public void ArchivePath_HasNoDestructiveOrMovingCalls()
    {
        // R-146-1 / S4・S8: 書庫の経路（メソッドの本体。位置ではなく波括弧で切り出す）に、消す・移す・上書きする呼び出しや、
        // 移動・ReTAC の転送の本体・元に戻すの記録が無い。件数の基準だけでは、別の呼び出しとの入れ替わりを見逃す
        var text = File.ReadAllText(Path.Combine(SchemaManifest.RepoRoot(), "src/ReTAC.App/MainForm.cs"));
        var regions = new[]
        {
            Body(text, "private void CopyFromArchive(VirtualItems"),
            Body(text, "private void CopyFromArchiveViaDialog(VirtualItems"),
            Body(text, "private void AcceptVirtualDrop("),
            Body(text, "private bool ClipboardPaste()", "DropData.CaptureVirtual(data)"),
        };
        foreach (var region in regions)
        {
            Assert.Equal(0, CodeLines(region).Sum(CallsIn));
            Assert.DoesNotMatch(@"moving:\s*true|Recording\(|RunOperation\(|TransferCore\(|ExecuteTransfer\(|new ShellFileOperation\(|_recorder", string.Join("\n", CodeLines(region)));
        }
        // 陽性対照: 区間は実在し、中身がある。検出器は、足された削除を数える
        Assert.Contains("CopyIdList", regions[0]);
        Assert.Contains("ForArchiveCopy", regions[0]);
        Assert.Contains("CopyFromArchive(", regions[3]);
        Assert.Equal(1, CodeLines(regions[0] + "\n    File.Delete(x);").Sum(CallsIn));
    }

    private static IEnumerable<string> CodeLines(string region) =>
        region.Split('\n').Select(l => { var i = l.IndexOf("//", StringComparison.Ordinal); return i >= 0 ? l[..i] : l; });

    /// <summary>signature で始まるメソッドの本体（波括弧の対応で切り出す）。from があれば、その位置から最初の波括弧の対を取る</summary>
    private static string Body(string text, string signature, string? from = null)
    {
        var at = text.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, signature);
        if (from is not null)
        {
            // メソッドの中の一部の分岐（貼り付けの書庫の分岐）: 条件の行から、次の閉じる波括弧までの return true; の手前まで
            var start = text.IndexOf(from, at, StringComparison.Ordinal);
            Assert.True(start >= 0, from);
            var end = text.IndexOf("return true;", start, StringComparison.Ordinal);
            return text[start..end];
        }
        var open = text.IndexOf('{', at);
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return text[open..(i + 1)];
        }
        throw new InvalidOperationException(signature);
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
    [InlineData("SetFileInformationByHandle(handle, FileDispositionInfoClass, ref disposition, 1);", 1)]
    [InlineData("DeleteFileW(path);", 1)]
    [InlineData("RemoveDirectoryW(path);", 1)]
    [InlineData("MoveFileExW(a, b, flags);", 1)]
    [InlineData("static extern bool MoveFileEx(string existing, string replacement, uint flags);", 1)]
    [InlineData("CopyFile(a, b, false);", 1)]
    [InlineData("var flags = FILE_FLAG_DELETE_ON_CLOSE;", 1)]
    [InlineData("new FileStream(p, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.DeleteOnClose);", 1)]
    [InlineData("var c = FileDispositionInfoEx;", 1)]
    [InlineData("var c = FileDispositionInformationEx;", 1)]
    [InlineData("NtSetInformationFile(h, ref io, ref info, 4, 64);", 1)]
    [InlineData("SHFileOperationW(ref op);", 1)]
    // NtCreateFile: 作成だけ（FILE_CREATE）なら数えない。閉じると消す・既存を置き換える指定が付いたら数える
    [InlineData("var status = NtCreateFile(out var raw, access, ref attributes, out _, IntPtr.Zero, 0x10, share, FILE_CREATE, FILE_DIRECTORY_FILE, IntPtr.Zero, 0);", 0)]
    [InlineData("var options = FILE_DIRECTORY_FILE | FILE_DELETE_ON_CLOSE;", 1)]
    [InlineData("NtCreateFile(out h, access, ref a, out s, IntPtr.Zero, 0, share, FILE_OVERWRITE_IF, options, IntPtr.Zero, 0);", 1)]
    [InlineData("NtCreateFile(out h, access, ref a, out s, IntPtr.Zero, 0, share, FILE_SUPERSEDE, options, IntPtr.Zero, 0);", 1)]
    [InlineData("CommandId.CopyFileName => ShowNameFormatPopup(),", 0)]
    [InlineData("Marshal.GetLastWin32Error();", 0)]
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
