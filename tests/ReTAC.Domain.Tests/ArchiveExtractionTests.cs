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

    [Fact]
    public void Owned_ReplacedFolderIsNotDeleted() => Sta.WithTempFolder(root =>
    {
        // S4: 作った後で同じパスに別のフォルダができたら（識別子が違えば）消さない
        var owned = OwnedTempFolder.TryCreate(root, ArchiveExtraction.FolderPrefix + "swap")!;
        Directory.Delete(owned.FullPath);   // 安全: テストのフォルダの中で作った所有のフォルダを、置き換わりを模すために消す
        Directory.CreateDirectory(owned.FullPath);
        File.WriteAllText(Path.Combine(owned.FullPath, "precious.txt"), "!");   // 安全: テストが新しく作ったフォルダ（Sta.WithTempFolder）の中
        // 陽性対照: 置き換えたフォルダの識別子は、作ったときのものと実際に違う（違わなければ、この試験は何も確かめていない）
        Assert.NotEqual(owned.Identity, OwnedTempFolder.IdOf(owned.FullPath));
        Assert.False(owned.Delete());   // 安全: 識別子が違うので消さないことを確かめる
        Assert.Equal("!", File.ReadAllText(Path.Combine(owned.FullPath, "precious.txt")));
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
    public void Owned_RootReplacedByJunctionIsNotDeleted() => Sta.WithTempFolder(root =>
    {
        // S8: 作ったフォルダの場所がリンクに置き換わっていたら、リンクもリンク先も触らない
        var target = Directory.CreateDirectory(Path.Combine(root, "retac-vtest-target")).FullName;
        var victim = Path.Combine(target, "victim.txt");
        File.WriteAllText(victim, "keep");   // 安全: テストが新しく作ったフォルダ（Sta.WithTempFolder）の中
        var owned = OwnedTempFolder.TryCreate(root, ArchiveExtraction.FolderPrefix + "jroot")!;
        Directory.Delete(owned.FullPath);   // 安全: テストのフォルダの中で作った空の所有のフォルダを、置き換わりを模すために消す
        MakeJunction(owned.FullPath, target);
        try
        {
            Assert.False(owned.Delete());   // 安全: リンクに置き換わっているので消さないことを確かめる
            Assert.True(Directory.Exists(owned.FullPath));
            Assert.Equal("keep", File.ReadAllText(victim));
        }
        finally { Directory.Delete(owned.FullPath); }   // 安全: このテストが作ったリンクそのもの（非再帰。リンク先は消えない）。後片付けが止まらないように
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
    public void Run_MatchReadFailureIsAbort() => Sta.WithTempFolder(root =>
    {
        var called = false;
        // R-146-1 / S6: 取り出しは終わったと答えたのに、結果を読む所（直下の一覧）で読めない。例外を外へ出さず、何も送らない
        var result = ArchiveExtraction.Run(root, Names, Displays,
            folder => { Directory.Delete(folder, recursive: true); return true; }, _ => called = true);   // 安全: Run が作った一時フォルダ（テストのフォルダの中）を、読めない状態を模すために消す
        Assert.False(result);
        Assert.False(called);
        Assert.Empty(Leftovers(root));
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
