using System.Runtime.InteropServices;
using ReTAC.App;

namespace ReTAC.Domain.Tests;

/// <summary>R-146-1 / S3〜S6: 一時フォルダへの取り出し・突き合わせ・続きの処理・片付け。</summary>
public class ArchiveExtractionTests
{
    private static readonly string[] Names = ["x.txt", "sub"];
    private static readonly string[] Displays = [@"C:\dl\a.zip\x.txt", @"C:\dl\a.zip\sub"];

    /// <summary>落とした 2 項目どおりに取り出す偽物</summary>
    private static bool ExtractBoth(string folder)
    {
        File.WriteAllText(Path.Combine(folder, "x.txt"), "x");   // 安全: テストが新しく作ったフォルダ（Sta.WithTempFolder）の中
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        File.WriteAllText(Path.Combine(folder, "sub", "y.txt"), "y");   // 安全: テストが新しく作ったフォルダ（Sta.WithTempFolder）の中
        return true;
    }

    private static string[] Leftovers(string root) => Directory.GetDirectories(root, ArchiveExtraction.FolderPrefix + "*");

    [Fact]
    public void Run_FolderExistsDuringNext() => Sta.WithTempFolder(root =>
    {
        string? seen = null;
        var result = ArchiveExtraction.Run(root, Names, Displays, ExtractBoth, extracted =>
        {
            // R-146-1: 続きの処理（ダイアログ・転送）が終わるまで、転送元は残っている
            Assert.All(extracted.Paths, p => Assert.True(Path.Exists(p)));
            Assert.Equal([Path.Combine(extracted.Folder, "x.txt"), Path.Combine(extracted.Folder, "sub")], extracted.Paths);
            seen = extracted.Folder;
        });
        Assert.True(result);
        Assert.NotNull(seen);
        Assert.False(Directory.Exists(seen));   // 終わったら片付く
        Assert.Empty(Leftovers(root));
    });

    [Fact]
    public void Run_NextThrowsStillDeletes() => Sta.WithTempFolder(root =>
    {
        Assert.Throws<InvalidOperationException>(() =>
            ArchiveExtraction.Run(root, Names, Displays, ExtractBoth, _ => throw new InvalidOperationException("bug")));
        Assert.Empty(Leftovers(root));
    });

    [Fact]
    public void Run_CancelledSendsNothing() => Sta.WithTempFolder(root =>
    {
        var called = false;
        Assert.False(ArchiveExtraction.Run(root, Names, Displays, folder => { ExtractBoth(folder); return false; }, _ => called = true));
        Assert.False(called);   // S3
        Assert.Empty(Leftovers(root));
    });

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(COMException))]
    [InlineData(typeof(InvalidOperationException))]
    public void Run_FailureDoesNotThrow(Type exception) => Sta.WithTempFolder(root =>
    {
        var called = false;
        var result = ArchiveExtraction.Run(root, Names, Displays,
            _ => throw (Exception)Activator.CreateInstance(exception, "fail")!, _ => called = true);
        Assert.False(result);   // 普通のファイル操作の失敗として受け止める（R-126 に載せない）
        Assert.False(called);
        Assert.Empty(Leftovers(root));
    });

    [Fact]
    public void Run_PartialExtractionSendsNothing() => Sta.WithTempFolder(root =>
    {
        var called = false;
        // 1 件だけ取り出せて、OS は完了と答えた（S3 / S6: 数が合わないので送らない）
        Assert.False(ArchiveExtraction.Run(root, Names, Displays,
            folder => { File.WriteAllText(Path.Combine(folder, "x.txt"), "x"); return true; }, _ => called = true));   // 安全: Run が作った一時フォルダ（テストのフォルダの中）
        Assert.False(called);
        Assert.Empty(Leftovers(root));
    });

    [Fact]
    public void Run_MismatchedNamesSendNothing() => Sta.WithTempFolder(root =>
    {
        var called = false;
        Assert.False(ArchiveExtraction.Run(root, Names, Displays, folder =>
        {
            ExtractBoth(folder);
            File.WriteAllText(Path.Combine(folder, "extra.txt"), "?");   // 落としていないものができた 安全: Run が作った一時フォルダ（テストのフォルダの中）
            return true;
        }, _ => called = true));
        Assert.False(called);   // S6
        Assert.Empty(Leftovers(root));
    });

    [Fact]
    public void Run_DeletesReadOnlyFiles() => Sta.WithTempFolder(root =>
    {
        Assert.True(ArchiveExtraction.Run(root, Names, Displays, folder =>
        {
            ExtractBoth(folder);
            File.SetAttributes(Path.Combine(folder, "sub", "y.txt"), FileAttributes.ReadOnly);
            File.SetAttributes(Path.Combine(folder, "x.txt"), FileAttributes.ReadOnly);
            return true;
        }, _ => { }));
        Assert.Empty(Leftovers(root));
    });

    [Fact]
    public void Run_DeletesHiddenAndSystemFiles() => Sta.WithTempFolder(root =>
    {
        // 既定の列挙は隠し・システム属性を飛ばす。読み取り専用も重なると外されず、一時フォルダが残った
        Assert.True(ArchiveExtraction.Run(root, Names, Displays, folder =>
        {
            ExtractBoth(folder);
            var attributes = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly;
            File.SetAttributes(Path.Combine(folder, "sub", "y.txt"), attributes);
            File.SetAttributes(Path.Combine(folder, "x.txt"), attributes);
            return true;
        }, _ => { }));
        Assert.Empty(Leftovers(root));
    });

    [Fact]
    public void Run_UsesNewFolderEachTime() => Sta.WithTempFolder(root =>
    {
        var folders = new List<string>();
        ArchiveExtraction.Run(root, Names, Displays, ExtractBoth, e => folders.Add(e.Folder));
        ArchiveExtraction.Run(root, Names, Displays, ExtractBoth, e => folders.Add(e.Folder));
        Assert.Equal(2, folders.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(folders, f => Assert.StartsWith(ArchiveExtraction.FolderPrefix, Path.GetFileName(f)));
    });

    [Fact]
    public void Run_LeavesForeignReTACFolders() => Sta.WithTempFolder(root =>
    {
        // S4: 同じ形（tempRoot の直下・ReTAC- で始まる）でも、この処理が作っていないフォルダは中身ごと残る
        var foreign = Directory.CreateDirectory(Path.Combine(root, ArchiveExtraction.FolderPrefix + "keep")).FullName;
        File.WriteAllText(Path.Combine(foreign, "precious.txt"), "!");   // 安全: テストが新しく作ったフォルダ（Sta.WithTempFolder）の中
        ArchiveExtraction.Run(root, Names, Displays, ExtractBoth, _ => { });
        ArchiveExtraction.Run(root, Names, Displays, _ => false, _ => { });
        ArchiveExtraction.Run(root, Names, Displays, _ => throw new IOException("fail"), _ => { });
        Assert.Equal("!", File.ReadAllText(Path.Combine(foreign, "precious.txt")));
    });

    [Fact]
    public void Owned_ExistingFolderIsNotUsed() => Sta.WithTempFolder(root =>
    {
        // S4: 作るときに既にあったら使わない（中身のあるほかのフォルダを、自分のものとして消さない）
        var existing = Directory.CreateDirectory(Path.Combine(root, ArchiveExtraction.FolderPrefix + "same")).FullName;
        File.WriteAllText(Path.Combine(existing, "precious.txt"), "!");   // 安全: テストが新しく作ったフォルダ（Sta.WithTempFolder）の中
        Assert.Null(OwnedTempFolder.TryCreate(root, ArchiveExtraction.FolderPrefix + "same"));
        Assert.Equal("!", File.ReadAllText(Path.Combine(existing, "precious.txt")));
    });

    /// <summary>
    /// S4 / S8: 片付けの 3 つの時点（所有確認の後・走査の途中・削除の直前）で、作ったフォルダを移して同じパスにおとりを置こうとする。
    /// 固定していれば移せず、おとりは置けない。移せてしまった場合はおとりを置く（その場合、おとりが消えるので失敗する）。
    /// 返り値: 移せてしまったか
    /// </summary>
    private static bool TryReplace(string root, OwnedTempFolder owned, string decoy, string tag)
    {
        var moved = Path.Combine(root, "retac-vtest-moved-" + tag);
        try { Directory.Move(owned.FullPath, moved); }   // 安全: テストのフォルダの中で、所有のフォルダを移そうとする（失敗することを確かめる）
        catch (IOException) { return false; }
        Directory.Move(decoy, owned.FullPath);   // 安全: 移せてしまったときだけ。テストのおとり（テストが作った）を同じパスへ置く
        return true;
    }

    /// <summary>おとり（中身入り）と、中身・サブフォルダ・読み取り専用ファイルを持つ所有のフォルダを作る</summary>
    private static (OwnedTempFolder Owned, string Decoy, string Precious) ArrangeReplace(string root, string tag)
    {
        var decoy = Directory.CreateDirectory(Path.Combine(root, "retac-vtest-decoy-" + tag)).FullName;
        var precious = Path.Combine(decoy, "precious.txt");
        File.WriteAllText(precious, "!");   // 安全: テストが新しく作ったフォルダ（Sta.WithTempFolder）の中
        var owned = OwnedTempFolder.TryCreate(root, ArchiveExtraction.FolderPrefix + "pin-" + tag)!;
        Directory.CreateDirectory(Path.Combine(owned.FullPath, "sub"));
        File.WriteAllText(Path.Combine(owned.FullPath, "sub", "y.txt"), "y");   // 安全: Run と同じ、テストが作った所有のフォルダの中
        File.WriteAllText(Path.Combine(owned.FullPath, "x.txt"), "x");   // 安全: 同上
        return (owned, decoy, precious);
    }

    [Theory]
    [InlineData(0)]   // AfterOwnership
    [InlineData(1)]   // DuringScan
    [InlineData(2)]   // BeforeRemove
    public void Owned_CannotBeReplacedWhileHeld(int phaseNumber) => Sta.WithTempFolder(root =>
    {
        var phase = (OwnedTempFolder.DeletePhase)phaseNumber;
        // S4: 確認した対象を、削除が終わるまで固定する。どの時点で置き換えようとしても、おとりのフォルダは残る
        var (owned, decoy, precious) = ArrangeReplace(root, phase.ToString());
        var attempts = 0;
        var replaced = false;
        owned.Hook = p =>
        {
            if (p != phase) return;
            attempts++;
            replaced |= TryReplace(root, owned, decoy, phase.ToString());
        };
        var result = owned.Delete();   // 安全: テストのフォルダの中で作った所有のフォルダ
        Assert.Equal(1, attempts);   // 置き換えの試みが実際にその時点で行われた（行われていなければ何も確かめていない）
        Assert.False(replaced);
        Assert.True(result);
        Assert.False(Directory.Exists(owned.FullPath));   // 作ったフォルダは片付いた
        Assert.Equal("!", File.ReadAllText(precious));   // おとりは無事
    });

    [Fact]
    public void Owned_DeleteWithoutInterferenceRemovesFolder() => Sta.WithTempFolder(root =>
    {
        // 陽性対照: 差し込み口で何もしなければ、作ったフォルダ（中身ごと）は消える
        var (owned, _, precious) = ArrangeReplace(root, "none");
        var phases = new List<OwnedTempFolder.DeletePhase>();
        owned.Hook = phases.Add;
        Assert.True(owned.Delete());   // 安全: テストのフォルダの中で作った所有のフォルダ
        Assert.Equal([OwnedTempFolder.DeletePhase.AfterOwnership, OwnedTempFolder.DeletePhase.DuringScan, OwnedTempFolder.DeletePhase.BeforeRemove], phases);
        Assert.False(Directory.Exists(owned.FullPath));
        Assert.Equal("!", File.ReadAllText(precious));
    });

    [Fact]
    public void Owned_HeldFolderCannotBeMovedOrDeletedByOthers() => Sta.WithTempFolder(root =>
    {
        // S4: 作った直後から固定している。移す・名前を変える・パスで消すは、ほかからはできない
        var owned = OwnedTempFolder.TryCreate(root, ArchiveExtraction.FolderPrefix + "held")!;
        Assert.Throws<IOException>(() => Directory.Move(owned.FullPath, Path.Combine(root, "retac-vtest-elsewhere")));   // 安全: 失敗することを確かめる（移せたらテストが失敗する）
        Assert.Throws<IOException>(() => Directory.Delete(owned.FullPath));   // 安全: 失敗することを確かめる（空の、所有のフォルダ）
        Assert.True(Directory.Exists(owned.FullPath));
        Assert.True(owned.Delete());   // 安全: テストのフォルダの中で作った所有のフォルダ
        Assert.False(Directory.Exists(owned.FullPath));
    });

    [Fact]
    public void Owned_DeleteClosesHandleEvenWhenNotDeleted() => Sta.WithTempFolder(root =>
    {
        // 消せなかった場合（中身が増えた・直下に開いたままのファイルがある）でも、ハンドルは閉じる（固定が残り続けない）
        var owned = OwnedTempFolder.TryCreate(root, ArchiveExtraction.FolderPrefix + "busy")!;
        var file = Path.Combine(owned.FullPath, "locked.txt");
        using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))   // 安全: 新規作成。テストが作った所有のフォルダの中
        {
            Assert.False(owned.Delete());   // 安全: 中のファイルが開かれていて消せない。消えないことを確かめる
        }
        Assert.True(Directory.Exists(owned.FullPath));
        Directory.Move(owned.FullPath, Path.Combine(root, "retac-vtest-released"));   // 安全: ハンドルが閉じていれば移せる。テストのフォルダの中で移すだけ
    });

    /// <summary>テストのフォルダの中だけに結合点（ジャンクション）を作る。作れなければ失敗にする（黙って飛ばさない）</summary>
    private static void MakeJunction(string link, string target)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, "mklink /J failed: " + output);
        Assert.True((new DirectoryInfo(link).Attributes & FileAttributes.ReparsePoint) != 0);   // 作ったものが本当にリンク
    }

    [Fact]
    public void Owned_DeleteDoesNotFollowJunctionInside() => Sta.WithTempFolder(root =>
    {
        // S8: 中のリンクはたどらない。リンク先の読み取り専用ファイルの属性も、中身も、残る
        var target = Directory.CreateDirectory(Path.Combine(root, "retac-vtest-target")).FullName;
        var victim = Path.Combine(target, "victim.txt");
        File.WriteAllText(victim, "keep");   // 安全: テストが新しく作ったフォルダ（Sta.WithTempFolder）の中
        File.SetAttributes(victim, FileAttributes.ReadOnly);
        var owned = OwnedTempFolder.TryCreate(root, ArchiveExtraction.FolderPrefix + "junc")!;
        MakeJunction(Path.Combine(owned.FullPath, "link"), target);
        Assert.True(owned.Delete());   // 安全: テストのフォルダの中で作った所有のフォルダ（中にリンクがある）
        Assert.False(Directory.Exists(owned.FullPath));
        Assert.True(File.Exists(victim));
        Assert.True((File.GetAttributes(victim) & FileAttributes.ReadOnly) != 0);
        Assert.Equal("keep", File.ReadAllText(victim));
    });

    [Fact]
    public void Owned_MissingTempRootCreatesNothing() => Sta.WithTempFolder(root =>
    {
        var missing = Path.Combine(root, "no-such", "deeper");
        Assert.Null(OwnedTempFolder.TryCreate(missing));
        Assert.False(Directory.Exists(Path.Combine(root, "no-such")));   // 親も作らない
    });

    [Fact]
    public void Owned_UnusableIdentityIsRejected()
    {
        Assert.False(OwnedTempFolder.IsUsable((0, 0)));
        Assert.True(OwnedTempFolder.IsUsable((1, 0)));
        Assert.True(OwnedTempFolder.IsUsable((0, 1)));
    }

    [Fact]
    public void Owned_DeletesOwnFolder() => Sta.WithTempFolder(root =>
    {
        var owned = OwnedTempFolder.TryCreate(root, ArchiveExtraction.FolderPrefix + "mine")!;
        File.WriteAllText(Path.Combine(owned.FullPath, "x.txt"), "x");   // 安全: テストが新しく作ったフォルダ（Sta.WithTempFolder）の中
        Assert.True(owned.Delete());   // 安全: テストのフォルダの中で作った所有のフォルダ
        Assert.False(Directory.Exists(owned.FullPath));
    });

    [Fact]
    public void Match_UnreadableFolderThrowsIOException() => Sta.WithTempFolder(root =>
    {
        // R-146-1 / S6: 結果を読む所（直下の一覧）で読めないときは IOException。Run はこれを取り出しの失敗と同じく中断にする
        // （一時フォルダは固定されているので、Run の中から消して読めない状態を作ることはできない。Run 側は Run_FailureDoesNotThrow が確かめる）
        Assert.ThrowsAny<IOException>(() => ExtractedFolder.Match(Path.Combine(root, "no-such"), Names, Displays));
    });

    [Fact]
    public void Match_DuplicateNamesAreRejected() => Sta.WithTempFolder(root =>
    {
        // S6: 指定が x.txt, x.txt で、実際が x.txt と x (2).txt（別名になった）。数が合っても通さない
        File.WriteAllText(Path.Combine(root, "x.txt"), "x");   // 安全: テストが新しく作ったフォルダ（Sta.WithTempFolder）の中
        File.WriteAllText(Path.Combine(root, "x (2).txt"), "x");   // 安全: 同上
        string[] displays = [@"C:\dl\a.zip\x.txt", @"C:\dl\a.zip\x.txt"];
        Assert.Null(ExtractedFolder.Match(root, ["x.txt", "x.txt"], displays));
        // 陽性対照: 重複の無い正しい指定なら通る（この入力が照合できる状態であることの確認）
        Assert.NotNull(ExtractedFolder.Match(root, ["x.txt", "x (2).txt"], displays));
    });

    [Fact]
    public void Match_CaseOnlyDuplicateIsRejected() => Sta.WithTempFolder(root =>
    {
        File.WriteAllText(Path.Combine(root, "a.txt"), "a");   // 安全: テストが新しく作ったフォルダ（Sta.WithTempFolder）の中
        File.WriteAllText(Path.Combine(root, "b.txt"), "b");   // 安全: 同上
        string[] displays = [@"C:\dl\a.zip\a.txt", @"C:\dl\a.zip\A.TXT"];
        Assert.Null(ExtractedFolder.Match(root, ["a.txt", "A.TXT"], displays));
        Assert.NotNull(ExtractedFolder.Match(root, ["a.txt", "B.TXT"], displays));   // 大小文字を無視して照合できる
    });

    [Theory]
    [InlineData("x.txt", "x.txt")]
    [InlineData("a.txt", "A.TXT")]
    public void Run_DuplicateNamesAreNotExtracted(string first, string second) => Sta.WithTempFolder(root =>
    {
        var extracted = false;
        var called = false;
        // S6: 重複する名前は、取り出す前に断る（extract も next も呼ばない。一時フォルダも作らない）
        Assert.False(ArchiveExtraction.Run(root, [first, second], [@"C:\dl\a.zip\1", @"C:\dl\a.zip\2"], _ => extracted = true, _ => called = true));
        Assert.False(extracted);
        Assert.False(called);
        Assert.Empty(Directory.GetFileSystemEntries(root));
    });

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(@"..\x.txt")]
    [InlineData("sub/x.txt")]
    [InlineData(@"C:\x.txt")]
    [InlineData("a:b")]
    public void Run_InvalidNameIsNotExtracted(string bad) => Sta.WithTempFolder(root =>
    {
        var extracted = false;
        // S6: 落とした項目の名前が 1 つの名前として正しくなければ、取り出す前に断る（一時フォルダも作らない）
        Assert.False(ArchiveExtraction.Run(root, [bad], [@"C:\dl\a.zip\x"], _ => extracted = true, _ => { }));
        Assert.False(extracted);
        Assert.Empty(Directory.GetFileSystemEntries(root));
    });

    [Fact]
    public void Display_MapsToArchivePath() => Sta.WithTempFolder(root =>
    {
        ArchiveExtraction.Run(root, Names, Displays, ExtractBoth, e =>
        {
            Assert.Equal(@"C:\dl\a.zip\x.txt", e.Display(Path.Combine(e.Folder, "x.txt")));
            Assert.Equal(@"C:\dl\a.zip\sub\y.txt", e.Display(Path.Combine(e.Folder, "sub", "y.txt")));
            Assert.Equal(@"D:\other\x.txt", e.Display(@"D:\other\x.txt"));   // 一時フォルダの外はそのまま
            Assert.True(e.Contains(Path.Combine(e.Folder, "sub", "y.txt")));
            Assert.False(e.Contains(@"D:\other\x.txt"));
        });
    });
}
