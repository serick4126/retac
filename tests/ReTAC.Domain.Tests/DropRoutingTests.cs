using System.IO;
using ReTAC.Domain.Entries;
using ReTAC.Domain.FileOps;

namespace ReTAC.Domain.Tests;

/// <summary>R-110-1 / R-111-2: 受け口 × ドラッグ元 × ボタンの経路と、右ボタンのメニュー</summary>
public class DropRoutingTests
{
    // INV-INPANEL-DROP-VIA-DIALOG / Q4: ReTAC から始めたドラッグは、ファイルリストでもダイアログを経る。
    // 外からはファイルリストとドライブバーだけダイアログなし（ドライブバーは ReTAC からでもなし。P3 の例外）
    [Theory]
    [InlineData(DropReceptacle.FileList, true, DropRoute.Dialog)]
    [InlineData(DropReceptacle.FileList, false, DropRoute.Direct)]
    [InlineData(DropReceptacle.DriveBar, true, DropRoute.Direct)]
    [InlineData(DropReceptacle.DriveBar, false, DropRoute.Direct)]
    [InlineData(DropReceptacle.DriveTree, true, DropRoute.Dialog)]
    [InlineData(DropReceptacle.DriveTree, false, DropRoute.Dialog)]
    [InlineData(DropReceptacle.DesktopTree, true, DropRoute.Dialog)]
    [InlineData(DropReceptacle.DesktopTree, false, DropRoute.Dialog)]
    [InlineData(DropReceptacle.AddressBar, true, DropRoute.Dialog)]
    [InlineData(DropReceptacle.AddressBar, false, DropRoute.Dialog)]
    [InlineData(DropReceptacle.BookmarkFolder, true, DropRoute.Dialog)]
    [InlineData(DropReceptacle.BookmarkFolder, false, DropRoute.Dialog)]
    // fromReTAC は同じプロセスで始めたドラッグ。「新しいウィンドウ」で開いた別のウィンドウのファイルリストからも true（Q6）。
    // そのとき設定オフでも、宛先は落とした先のウィンドウの今のフォルダになり、ダイアログを経る（右ボタンならメニューが出る）
    public void 経路は受け口とドラッグ元で決まりボタンでは変わらない(DropReceptacle receptacle, bool fromReTAC, DropRoute expected)
    {
        // INV-RIGHT-DROP-SAME-ROUTE: 右ボタンでも経路は同じ。メニューを出すかだけが変わる
        Assert.Equal(new DropPlan(expected, ShowMenu: false), DropRouting.Plan(receptacle, fromReTAC, rightButton: false));
        Assert.Equal(new DropPlan(expected, ShowMenu: true), DropRouting.Plan(receptacle, fromReTAC, rightButton: true));
    }

    private static readonly Entry Folder = Entry.ForFolder(@"C:\a\sub", "sub", FileAttributes.Directory, default);
    private static readonly Entry File = Entry.ForFile(@"C:\a\x.txt", "x.txt", FileAttributes.Normal, 0, default);
    private static readonly Entry Parent = Entry.ForParent(@"C:\");

    [Fact]
    public void 設定オンならフォルダと親フォルダの項目が宛先になる()
    {
        Assert.Equal(@"C:\a\sub", DropRouting.FileListDestination(true, Folder, @"C:\a"));
        Assert.Equal(@"C:\", DropRouting.FileListDestination(true, Parent, @"C:\a"));   // Q2
        Assert.Equal(@"C:\a", DropRouting.FileListDestination(true, File, @"C:\a"));
        Assert.Equal(@"C:\a", DropRouting.FileListDestination(true, null, @"C:\a"));
        Assert.True(DropRouting.TargetsItem(true, Folder));
        Assert.True(DropRouting.TargetsItem(true, Parent));
        Assert.False(DropRouting.TargetsItem(true, File));
        Assert.False(DropRouting.TargetsItem(true, null));
    }

    [Fact]
    public void 設定オフならどこでも今のフォルダ()
    {
        foreach (var hit in new[] { Folder, Parent, File, null })
        {
            Assert.Equal(@"C:\a", DropRouting.FileListDestination(false, hit, @"C:\a"));
            Assert.False(DropRouting.TargetsItem(false, hit));
        }
    }

    /// <summary>
    /// 許す効果の 8 通り × 同じドライブ・別のドライブ。灰色は許さない項目だけ。メニューを出さないのは、宛先が不可の所か、
    /// 何も許さないときだけ。太字は左ボタンで落としたときに起きるほう（それが起きない（許されない）ときは太字なし。
    /// </summary>
    [Theory]
    //          copy   move   link   同じドライブの太字   別のドライブの太字
    [InlineData(false, false, false, null, null)]
    [InlineData(false, false, true, DropAction.None, DropAction.None)]      // リンクだけ: 「ショートカットをここに作成」だけ使える
    [InlineData(false, true, false, DropAction.Move, DropAction.None)]      // 別のドライブで移動だけ: 移動が使える。左ボタンなら何も起きないので太字なし
    [InlineData(false, true, true, DropAction.Move, DropAction.None)]
    [InlineData(true, false, false, DropAction.Copy, DropAction.Copy)]     // R-78: 移動を許さなければ左ボタンはコピー
    [InlineData(true, false, true, DropAction.Copy, DropAction.Copy)]
    [InlineData(true, true, false, DropAction.Move, DropAction.Copy)]
    [InlineData(true, true, true, DropAction.Move, DropAction.Copy)]
    public void 許す効果の組み合わせごとの項目と太字(bool copy, bool move, bool link, DropAction? sameDrive, DropAction? otherDrive)
    {
        foreach (var (destination, expected) in new[] { (@"C:\b", sameDrive), (@"D:\b", otherDrive) })
        {
            var model = DropRouting.Menu(@"C:\a\x.txt", destination, copy, move, link);
            if (expected is null) { Assert.Null(model); continue; }
            Assert.Equal(new DropMenuModel(expected.Value, copy, move, link), model);
        }
    }

    [Fact]
    public void 不可の所ではメニューを出さない()
    {
        Assert.Null(DropRouting.Menu(@"C:\a\x.txt", @"C:\a", true, true, true));        // 同じフォルダ
        Assert.Null(DropRouting.Menu(@"C:\a\sub", @"C:\a\sub\inner", true, true, true)); // 自分の中
        Assert.Null(DropRouting.Menu(@"C:\a\sub", @"C:\a\sub", true, true, true));       // 自分自身
    }

    [Theory]
    [InlineData(true, true, true, DropChoice.Copy)]    // 太字があればそれ（別のドライブなのでコピー）
    [InlineData(false, true, false, DropChoice.Move)]  // 太字なし: 使える項目の先頭
    [InlineData(false, true, true, DropChoice.Move)]
    [InlineData(false, false, true, DropChoice.Link)]  // リンクだけ
    public void 右ボタンのドラッグ中は左ボタンで何も起きなくてもメニューが出る所なら落とせる(bool copy, bool move, bool link, DropChoice expected)
    {
        // 別のドライブで移動だけ・リンクだけのとき、ドラッグ中の効果を None にすると OLE は Drop を呼ばず、メニューを出す機会が無い
        var model = DropRouting.Menu(@"C:\a\x.txt", @"D:\b", copy, move, link)!.Value;
        Assert.Equal(expected, DropRouting.RightDragEffect(model));
    }

    [Fact]
    public void コピーと移動が混ざっても太字は先頭の項目で決まる()
    {
        // 先頭が C: → C:\b は移動。2 件目が D: でも太字は 1 つ（DropFeedback と同じ）
        Assert.Equal(DropAction.Move, DropRouting.Menu(@"C:\a\x.txt", @"C:\b", true, true, true)!.Value.Default);
        Assert.Equal(DropAction.Copy, DropRouting.Menu(@"D:\y.txt", @"C:\b", true, true, true)!.Value.Default);
    }

    [Fact]
    public void 選んだ操作はすべての項目に同じ修飾キーとして効く()
    {
        Assert.Equal((true, false), DropRouting.ModifiersFor(DropChoice.Copy));
        Assert.Equal((false, true), DropRouting.ModifiersFor(DropChoice.Move));
        var (copies, moves) = DropRules.Split([@"C:\a\x.txt", @"D:\y.txt"], @"C:\b", ctrl: false, shift: true, copyAllowed: true, moveAllowed: true);
        Assert.Empty(copies);
        Assert.Equal(2, moves.Count);
    }
}
