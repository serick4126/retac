using System.IO.Compression;
using System.Runtime.InteropServices;
using ReTAC.Shell;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-146-1 / S6（必須条件）: 書庫の中の不正な名前で、Windows の圧縮フォルダが取り出し先の外へ書き出さないこと。
/// ReTAC は書庫の中の名前を解釈せず Windows に頼るので、ここが失敗したら書庫の中の項目のドロップを実装しない。
/// 検証そのものの誤りで「何も確かめずに合格」にならないよう、入力の検証・陽性対照・判定の自己確認を必ず含める。
/// </summary>
public class ArchiveSafetyTests
{
    [Fact]
    public void Judge_PassesWhenOnlyDestinationChanges() => Sta.WithTempFolder(root =>
    {
        var dest = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
        File.WriteAllText(Path.Combine(root, "sentinel.txt"), "original");
        var before = OutsideWriteCheck.Snapshot([root], dest);

        File.WriteAllText(Path.Combine(dest, "inside.txt"), "x");
        Directory.CreateDirectory(Path.Combine(dest, "sub"));
        File.WriteAllText(Path.Combine(dest, "sub", "deep.txt"), "x");

        // 正常な取り出し（取り出し先の中だけが変わる）を不合格にしない。見張りが残っていても合格
        Assert.Equal("PASS", OutsideWriteCheck.Judge(before, OutsideWriteCheck.Snapshot([root], dest)));
    });

    [Fact]
    public void Judge_FailsOnCreatedOutside() => Sta.WithTempFolder(root =>
    {
        var dest = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
        var before = OutsideWriteCheck.Snapshot([root], dest);
        File.WriteAllText(Path.Combine(root, "created.txt"), "x");
        Assert.StartsWith("FAIL", OutsideWriteCheck.Judge(before, OutsideWriteCheck.Snapshot([root], dest)));
    });

    [Fact]
    public void Judge_FailsOnCreatedBesideDestinationWithSamePrefix() => Sta.WithTempFolder(root =>
    {
        // 「out」で始まる兄弟（out2）は取り出し先の中ではない
        var dest = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
        var before = OutsideWriteCheck.Snapshot([root], dest);
        Directory.CreateDirectory(Path.Combine(root, "out2"));
        File.WriteAllText(Path.Combine(root, "out2", "x.txt"), "x");
        Assert.StartsWith("FAIL", OutsideWriteCheck.Judge(before, OutsideWriteCheck.Snapshot([root], dest)));
    });

    [Fact]
    public void Judge_FailsOnSentinelOverwrittenOrDeleted() => Sta.WithTempFolder(root =>
    {
        var dest = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
        var sentinel = Path.Combine(root, "sentinel.txt");
        File.WriteAllText(sentinel, "original");
        var before = OutsideWriteCheck.Snapshot([root], dest);

        File.WriteAllText(sentinel, "changed");
        Assert.StartsWith("FAIL", OutsideWriteCheck.Judge(before, OutsideWriteCheck.Snapshot([root], dest)));

        File.Delete(sentinel);
        Assert.StartsWith("FAIL", OutsideWriteCheck.Judge(before, OutsideWriteCheck.Snapshot([root], dest)));
    });

    [Fact]
    public void Judge_WatchesEveryRoot() => Sta.WithTempFolder(root =>
    {
        // 絶対パスの項目が指す別の場所も見張る
        var dest = Directory.CreateDirectory(Path.Combine(root, "work", "out")).FullName;
        var other = Directory.CreateDirectory(Path.Combine(root, "abs-target")).FullName;
        var before = OutsideWriteCheck.Snapshot([Path.Combine(root, "work"), other], dest);
        File.WriteAllText(Path.Combine(other, "escape.txt"), "x");
        Assert.StartsWith("FAIL", OutsideWriteCheck.Judge(before, OutsideWriteCheck.Snapshot([Path.Combine(root, "work"), other], dest)));
    });

    /// <summary>不正な名前の項目と、それぞれが取り出し先の外へ出たときに当たる所</summary>
    private static (string[] Entries, string[] Sentinels) EvilArchive(string root, string work, string level1)
    {
        var absoluteSlash = Path.Combine(root, "abs-target", "escape-abs1.txt").Replace('\\', '/');
        var absoluteBackslash = Path.Combine(root, "abs-target", "escape-abs2.txt");
        string[] entries =
        [
            "sub/ok.txt",                  // 正しい項目（陽性対照）
            "sub/../../escape1.txt",       // dest\sub\..\..\ → level1
            "sub/..\\..\\escape2.txt",     // 区切りが \ の形 → level1
            "../escape3.txt",              // 直下の項目の ..\ → level1
            "sub/../../../escape4.txt",    // → work
            absoluteSlash,                 // 本当の絶対パス（/ 区切り）
            absoluteBackslash,             // 本当の絶対パス（\ 区切り）
        ];
        string[] sentinels =
        [
            Path.Combine(level1, "escape1.txt"), Path.Combine(level1, "escape2.txt"), Path.Combine(level1, "escape3.txt"),
            Path.Combine(work, "escape4.txt"),
            Path.Combine(root, "abs-target", "escape-abs1.txt"), Path.Combine(root, "abs-target", "escape-abs2.txt"),
        ];
        return (entries, sentinels);
    }

    [Theory]
    [InlineData(true)]    // エクスプローラーで書庫を開いて全部選ぶ（Ctrl+A）のと同じ、書庫の直下の全項目
    [InlineData(false)]   // 入れ子の不正な名前を持つフォルダ sub だけ
    public void Extract_NeverWritesOutsideDestination(bool allItems) => Sta.Run(() => Sta.WithTempFolder(root =>
    {
        // 絶対パスの名前は、見張りを置けるこのテストのフォルダを指すものだけ（ドライブの直下などへ書き出させない）
        var work = Directory.CreateDirectory(Path.Combine(root, "work")).FullName;
        var level1 = Directory.CreateDirectory(Path.Combine(work, "level1")).FullName;
        var dest = Directory.CreateDirectory(Path.Combine(level1, "out")).FullName;
        Directory.CreateDirectory(Path.Combine(root, "abs-target"));
        var (entries, sentinels) = EvilArchive(root, work, level1);

        var zip = Path.Combine(root, "evil.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            foreach (var name in entries)
                using (var writer = new StreamWriter(archive.CreateEntry(name).Open())) writer.Write("evil");

        // 入力の検証: 書庫の中の名前が、意図した不正な名前のまま入っている（ライブラリが直していない）
        using (var check = ZipFile.OpenRead(zip))
            Assert.Equal(entries, check.Entries.Select(e => e.FullName));
        Assert.True(Path.IsPathFullyQualified(entries[5]), entries[5]);
        Assert.True(Path.IsPathFullyQualified(entries[6]), entries[6]);
        // 見張りが、不正な名前の当たる所を指している（取り出し先の外で、書庫を作った後も無いことを確かめてから置く）
        Assert.All(sentinels, s => Assert.False(s.StartsWith(dest + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)));
        foreach (var sentinel in sentinels) File.WriteAllText(sentinel, "original");

        string[] watched = [root];
        var before = OutsideWriteCheck.Snapshot(watched, dest);
        var idLists = allItems ? TopLevelIdLists(zip) : [ParseIdList(Path.Combine(zip, "sub"))];
        try
        {
            Assert.NotEmpty(idLists);
            using var operation = new ShellFileOperation(IntPtr.Zero, silentOverwrite: false, noUi: true);
            foreach (var idList in idLists) operation.CopyIdList(idList, dest);
            try { operation.Execute(); }
            catch (IOException) { }   // 不正な項目で取り出しが失敗するのはよい。外へ書かないことを見る
        }
        finally { foreach (var idList in idLists) Marshal.FreeCoTaskMem(idList); }

        // 陽性対照: 正しい項目は取り出せている（何も起きなかったから合格、を防ぐ）。取り出せなければ判定できないので失敗にする
        Assert.True(File.Exists(Path.Combine(dest, "sub", "ok.txt")), "陽性対照の sub\\ok.txt が取り出されていない（判定できない）");
        Assert.Equal("evil", File.ReadAllText(Path.Combine(dest, "sub", "ok.txt")));
        // 本題: 取り出し先の外は、見張りの中身も含めて何も変わっていない（新しいファイルもできていない）
        Assert.Equal("PASS", OutsideWriteCheck.Judge(before, OutsideWriteCheck.Snapshot(watched, dest)));
        Assert.All(sentinels, s => Assert.Equal("original", File.ReadAllText(s)));
    }));

    /// <summary>解析名から識別子を作る（テストのデータ作りにだけ使う。呼び出し側が解放する）</summary>
    private static IntPtr ParseIdList(string path)
    {
        Marshal.ThrowExceptionForHR(SHParseDisplayName(path, IntPtr.Zero, out var idList, 0, out _));
        return idList;
    }

    /// <summary>書庫の直下の全項目（エクスプローラーで開いて Ctrl+A で選んだもの）。名前を解釈せず、シェルが列挙したものをそのまま使う</summary>
    private static List<IntPtr> TopLevelIdLists(string zip)
    {
        var iidItem = IID_IShellItem;
        Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(zip, IntPtr.Zero, ref iidItem, out var folder));
        var idLists = new List<IntPtr>();
        try
        {
            var bhid = BHID_EnumItems;
            var iidEnum = IID_IEnumShellItems;
            Marshal.ThrowExceptionForHR(folder.BindToHandler(IntPtr.Zero, ref bhid, ref iidEnum, out var pointer));
            var enumerator = (IEnumShellItems)Marshal.GetObjectForIUnknown(pointer);
            Marshal.Release(pointer);
            try
            {
                while (enumerator.Next(1, out var item, out var fetched) == 0 && fetched == 1)
                {
                    try
                    {
                        Marshal.ThrowExceptionForHR(SHGetIDListFromObject(item, out var idList));
                        idLists.Add(idList);
                    }
                    finally { Marshal.ReleaseComObject(item); }
                }
            }
            finally { Marshal.ReleaseComObject(enumerator); }
            return idLists;
        }
        catch
        {
            foreach (var idList in idLists) Marshal.FreeCoTaskMem(idList);
            throw;
        }
        finally { Marshal.ReleaseComObject(folder); }
    }

    private static readonly Guid IID_IShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    private static readonly Guid IID_IEnumShellItems = new("70629033-E363-4A28-A567-0DB78006E6D7");
    private static readonly Guid BHID_EnumItems = new("94F60519-2850-4924-AA5A-D15E84868039");

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITestShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr bindContext, ref Guid bhid, ref Guid riid, out IntPtr handler);
    }

    [ComImport, Guid("70629033-E363-4A28-A567-0DB78006E6D7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumShellItems
    {
        [PreserveSig] int Next(uint count, [MarshalAs(UnmanagedType.Interface)] out ITestShellItem item, out uint fetched);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out ITestShellItem item);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr idList, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHGetIDListFromObject([MarshalAs(UnmanagedType.IUnknown)] object item, out IntPtr idList);
}
