using System.Drawing;
using ReTAC.App;
using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Tests;

/// <summary>R-113: 一覧の名前の列の方式。</summary>
public class FileListViewNameTests
{
    private static FileListView List(NameWidthMode mode, int chars = 10, params string[] names)
    {
        var list = new FileListView { Size = new Size(600, 200) };
        var views = new FileViewSettings { List = new() { NameWidth = new() { Mode = mode, MaxChars = chars } } };
        list.SetView(FileViewMode.List, views, new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries(names.Select(n => TestEntries.File(n)).ToList());
        return list;
    }

    private static FileListView Styled(bool hideKnown, bool align, int chars, params string[] names)
    {
        var list = new FileListView { Size = new Size(600, 200) };
        var views = new FileViewSettings
        {
            Common = new() { HideKnownExtensions = hideKnown },
            List = new() { AlignExtension = align, NameWidth = new() { Mode = NameWidthMode.MaxChars, MaxChars = chars } },
        };
        list.SetView(FileViewMode.List, views, new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries(names.Select(n => TestEntries.File(n)).ToList());
        return list;
    }

    private const string Long = "とても長い資料の名前がここに続いていてまだ終わらないもっと長い名前.xlsx";

    [Fact]
    public void すべて表示では省略しない()
    {
        using var list = List(NameWidthMode.ShowAll, names: [Long, "a.txt"]);
        Assert.False(list.IsTruncated(0));
    }

    [Fact]
    public void 最大文字数では長い名前だけを省略し列はその幅を超えない()
    {
        using var list = List(NameWidthMode.MaxChars, chars: 10, names: [Long, "a.txt"]);
        Assert.True(list.IsTruncated(0));
        Assert.False(list.IsTruncated(1));
        var column = (ColumnLayout)list.Layout;
        Assert.True(column.ColumnWidth < 600);
    }

    [Fact]
    public void 自動ではパネルの幅を超えない()
    {
        using var list = List(NameWidthMode.Auto, names: [Long + Long + Long]);
        Assert.True(((ColumnLayout)list.Layout).ColumnWidth <= 600);
        Assert.True(list.IsTruncated(0));
    }

    [Fact]
    public void 一覧では省略したカーソルの項目を全部描く()
    {
        using var list = List(NameWidthMode.MaxChars, chars: 10, names: [Long, "a.txt"]);
        Assert.True(list.DrawsFullName(0));    // カーソルは先頭
        Assert.False(list.DrawsFullName(1));   // カーソルではない
    }

    private const string Unregistered = "a.retac-unregistered-ext-test";

    [Fact]
    public void 登録済みの拡張子を隠す設定では登録済みの項目だけ本体だけを描く()
    {
        using var on = Styled(hideKnown: true, align: true, chars: 200, "a.txt", Unregistered);
        Assert.Equal("a", on.NameText(TestEntries.File("a.txt")));
        Assert.Equal(Unregistered, on.NameText(TestEntries.File(Unregistered)) + ".retac-unregistered-ext-test");   // 本体は "a" のまま、拡張子は揃えて描く
        Assert.True(on.HidesExtension(TestEntries.File("a.txt")));
        Assert.False(on.HidesExtension(TestEntries.File(Unregistered)));
        Assert.False(on.HidesExtension(TestEntries.Folder("dir.txt")));   // フォルダの「.」は拡張子ではない
        using var off = Styled(hideKnown: false, align: false, chars: 200, "a.txt", Unregistered);
        Assert.Equal("a.txt", off.NameText(TestEntries.File("a.txt")));
        Assert.Equal(Unregistered, off.NameText(TestEntries.File(Unregistered)));
    }

    [Fact]
    public void 隠す項目の拡張子は揃えの位置に入れず揃えなくても続けて描かない()
    {
        using var aligned = Styled(hideKnown: true, align: true, chars: 200, "a.txt");
        Assert.Equal(0, aligned.Layout.ExtensionBounds(0).Width);   // 隠す項目だけなので揃えの領域を持たない
        using var together = Styled(hideKnown: true, align: false, chars: 200, "a.txt", Unregistered);
        Assert.Equal("a", together.NameText(TestEntries.File("a.txt")));
        Assert.Equal(Unregistered, together.NameText(TestEntries.File(Unregistered)));
    }

    [Fact]
    public void 拡張子を隠す設定のあいだ全部描きも隠す項目は本体だけ()
    {
        var name = "とても長い資料の名前がここに続いていてまだ終わらないもっと長い名前";
        using var hidden = Styled(hideKnown: true, align: true, chars: 10, name + ".txt");
        using var shown = Styled(hideKnown: false, align: true, chars: 10, name + ".txt");
        var entry = TestEntries.File(name + ".txt");
        Assert.Equal(name, hidden.FullNameText(entry));
        Assert.Equal(name + ".txt", shown.FullNameText(entry));
    }

    [Fact]
    public void 名前のツールチップは拡張子を隠す設定のあいだファイルなら省略していなくても出す()
    {
        var entries = new[] { TestEntries.File("a.txt"), TestEntries.Folder("dir") };
        using var on = new FileListView { Size = new Size(600, 200) };
        on.SetView(FileViewMode.List, new FileViewSettings { Common = new() { HideKnownExtensions = true } }, new Dictionary<string, int?>(), SortOrder.Default);
        on.SetEntries(entries);
        Assert.True(on.ShowsNameTip(0));    // ファイルはいつも
        Assert.False(on.ShowsNameTip(1));   // フォルダは省略したときだけ
        using var off = List(NameWidthMode.ShowAll, names: ["a.txt"]);
        Assert.False(off.ShowsNameTip(0));  // オフなら省略したときだけ
    }

    [Fact]
    public void 揃えないと拡張子を本体に続けて描き省略しても拡張子を残す()
    {
        using var list = Styled(hideKnown: false, align: false, chars: 10, Long);
        var entry = TestEntries.File(Long);
        Assert.Equal(Long, list.NameText(entry));
        Assert.Equal(0, list.Layout.ExtensionBounds(0).Width);
        var shown = list.TogetherText(entry, 150);
        Assert.EndsWith("….xlsx", shown);
        Assert.NotEqual(Long, shown);
        Assert.Equal(Long, list.TogetherText(entry, 5));   // 拡張子も入らなければ全体（末尾を EndEllipsis で省略）
    }

    [Fact]
    public void 中身が短ければ列も短い()
    {
        using var wide = List(NameWidthMode.MaxChars, chars: 200, names: ["a.txt"]);
        using var all = List(NameWidthMode.ShowAll, names: ["a.txt"]);
        Assert.Equal(((ColumnLayout)all.Layout).ColumnWidth, ((ColumnLayout)wide.Layout).ColumnWidth);
    }
}
