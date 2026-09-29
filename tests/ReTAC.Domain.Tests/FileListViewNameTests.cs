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

    private static FileListView Styled(bool showExtension, bool align, int chars, params string[] names)
    {
        var list = new FileListView { Size = new Size(600, 200) };
        var views = new FileViewSettings
        {
            Common = new() { ShowExtension = showExtension },
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

    [Fact]
    public void 拡張子を表示しないと本体だけを描き揃えの領域も持たない()
    {
        using var list = Styled(showExtension: false, align: true, chars: 200, "資料.xlsx");
        var entry = TestEntries.File("資料.xlsx");
        Assert.Equal("資料", list.NameText(entry));
        Assert.Equal(0, list.Layout.ExtensionBounds(0).Width);
    }

    [Fact]
    public void 拡張子を表示しないとカーソルの全部描きも本体だけ()
    {
        using var hidden = Styled(showExtension: false, align: true, chars: 10, Long);
        using var shown = Styled(showExtension: true, align: true, chars: 10, Long);
        var entry = TestEntries.File(Long);
        Assert.Equal("とても長い資料の名前がここに続いていてまだ終わらないもっと長い名前", hidden.FullNameText(entry));
        Assert.Equal(Long, shown.FullNameText(entry));
    }

    [Fact]
    public void 揃えないと拡張子を本体に続けて描き省略しても拡張子を残す()
    {
        using var list = Styled(showExtension: true, align: false, chars: 10, Long);
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
