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
    public void 中身が短ければ列も短い()
    {
        using var wide = List(NameWidthMode.MaxChars, chars: 200, names: ["a.txt"]);
        using var all = List(NameWidthMode.ShowAll, names: ["a.txt"]);
        Assert.Equal(((ColumnLayout)all.Layout).ColumnWidth, ((ColumnLayout)wide.Layout).ColumnWidth);
    }
}
