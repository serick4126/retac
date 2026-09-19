using ReTAC.Domain.Navigation;

namespace ReTAC.Domain.Tests;

/// <summary>R-94: パンくずの段への分け方と畳み方</summary>
public class BreadcrumbTests
{
    private static string[] Names(string folder) => [.. Breadcrumb.Split(folder).Select(s => s.Name)];
    private static string[] Paths(string folder) => [.. Breadcrumb.Split(folder).Select(s => s.Path)];

    [Fact]
    public void ドライブ直下はドライブの1段でパスはルート()
    {
        Assert.Equal(["C:"], Names(@"C:\"));
        Assert.Equal([@"C:\"], Paths(@"C:\"));
    }

    [Fact]
    public void 深いパスは段ごとに累積したパスを持つ()
    {
        Assert.Equal(["C:", "Users", "serick"], Names(@"C:\Users\serick"));
        Assert.Equal([@"C:\", @"C:\Users", @"C:\Users\serick"], Paths(@"C:\Users\serick"));
    }

    [Fact]
    public void 末尾の区切りは無視する()
    {
        Assert.Equal(["C:", "Users"], Names(@"C:\Users\"));
    }

    [Fact]
    public void UNCは共有までを1段にする()
    {
        Assert.Equal([@"\\server\share", "a"], Names(@"\\server\share\a"));
        Assert.Equal(@"\\server\share\a", Paths(@"\\server\share\a")[^1]);
    }

    [Fact]
    public void 空のパスは段を持たない()
    {
        Assert.Empty(Breadcrumb.Split(""));
    }

    [Fact]
    public void 幅がちょうど一致すれば畳まず_1足りなければ先頭を畳む()
    {
        int[] widths = [10, 20, 30, 40];   // 合計 100
        Assert.Equal(0, Breadcrumb.FirstShown(widths, ellipsisWidth: 5, available: 100));
        Assert.Equal(1, Breadcrumb.FirstShown(widths, ellipsisWidth: 5, available: 99));
    }

    [Fact]
    public void 省略記号を足すとあふれるならもう1段畳む()
    {
        int[] widths = [10, 20, 30, 40];
        // 1 段目だけ畳むと 20+30+40 = 90 だが、… を足すと 90+15 = 105 > 100 − 1
        Assert.Equal(2, Breadcrumb.FirstShown(widths, ellipsisWidth: 15, available: 99));
    }

    [Fact]
    public void 畳んだら省略記号の幅も足して数える()
    {
        int[] widths = [10, 20, 30, 40];
        Assert.Equal(1, Breadcrumb.FirstShown(widths, 5, 95));   // 20+30+40+5 = 95
        Assert.Equal(2, Breadcrumb.FirstShown(widths, 5, 94));   // 30+40+5 = 75
        Assert.Equal(3, Breadcrumb.FirstShown(widths, 5, 45));   // 40+5 = 45
    }

    [Fact]
    public void 最後の段は必ず残す()
    {
        int[] widths = [10, 20, 30, 40];
        Assert.Equal(3, Breadcrumb.FirstShown(widths, 5, 44));   // 最後の段も入らない
        Assert.Equal(3, Breadcrumb.FirstShown(widths, 5, 0));
        Assert.Equal(3, Breadcrumb.FirstShown(widths, 5, -10));
    }

    [Fact]
    public void 一段だけなら畳まない()
    {
        Assert.Equal(0, Breadcrumb.FirstShown([40], 5, 100));
        Assert.Equal(0, Breadcrumb.FirstShown([40], 5, 10));   // 入らなくても最後の段
        Assert.Equal(0, Breadcrumb.FirstShown([], 5, 10));
    }
}
