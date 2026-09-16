using ReTAC.Domain.Navigation;

namespace ReTAC.Domain.Tests;

/// <summary>フォルダ履歴（N-02）と前後フォルダ</summary>
public class FolderHistoryTests
{
    [Fact]
    public void 新しい順に並び同じフォルダは重複しない()
    {
        var history = new FolderHistory();
        history.Remember(@"C:\a");
        history.Remember(@"C:\b");
        history.Remember(@"C:\a\");   // 末尾の区切りと大小文字は同一視する

        Assert.Equal([@"C:\a\", @"C:\b"], history.Recent);
    }

    [Fact]
    public void 過去16件までしか保たない()
    {
        var history = new FolderHistory();
        for (var i = 0; i < 20; i++) history.Remember($@"C:\{i}");

        Assert.Equal(FolderHistory.Capacity, history.Recent.Count);
        Assert.Equal(@"C:\19", history.Recent[0]);
        Assert.DoesNotContain(@"C:\3", history.Recent);
    }

    [Fact]
    public void 前後フォルダを往復できる()
    {
        var history = new FolderHistory();
        history.Visit(null, @"C:\a");
        history.Visit(@"C:\a", @"C:\b");
        history.Visit(@"C:\b", @"C:\c");

        Assert.Equal(@"C:\b", history.Back(@"C:\c"));
        Assert.Equal(@"C:\a", history.Back(@"C:\b"));
        Assert.False(history.CanGoBack);
        Assert.Equal(@"C:\b", history.Forward(@"C:\a"));
        Assert.Equal(@"C:\c", history.Forward(@"C:\b"));
        Assert.False(history.CanGoForward);
    }

    [Fact]
    public void 戻ったあとに別の場所へ移動すると進む先は捨てられる()
    {
        var history = new FolderHistory();
        history.Visit(null, @"C:\a");
        history.Visit(@"C:\a", @"C:\b");
        history.Back(@"C:\b");

        history.Visit(@"C:\a", @"C:\x");

        Assert.False(history.CanGoForward);
        Assert.Equal(@"C:\a", history.Back(@"C:\x"));
    }

    [Fact]
    public void 同じフォルダへの移動は戻り先を積まない()
    {
        var history = new FolderHistory();
        history.Visit(null, @"C:\a");
        history.Visit(@"C:\a", @"C:\a\");

        Assert.False(history.CanGoBack);
    }

    [Fact]
    public void 存在しなくなったフォルダを履歴から外せる()
    {
        var history = new FolderHistory();
        history.Remember(@"C:\a");
        history.Remember(@"C:\b");

        history.Forget(@"C:\A");

        Assert.Equal([@"C:\b"], history.Recent);
    }

    [Fact]
    public void 履歴には出ていったフォルダが積まれる()
    {
        var history = new FolderHistory();
        history.Visit(null, @"C:	emp");              // 起動直後は何も積まない
        Assert.Empty(history.Recent);

        history.Visit(@"C:	emp", @"D:	emp");        // C:	emp を出た
        Assert.Equal([@"C:	emp"], history.Recent);

        history.Visit(@"D:	emp", @"D:\");            // D:	emp を出た
        Assert.Equal([@"D:	emp", @"C:	emp"], history.Recent);

        // 今いるフォルダも、以前そこを出ていれば一覧に残る（卓駆と同じ）
        history.Visit(@"D:\", @"C:	emp");
        Assert.Equal([@"D:\", @"D:	emp", @"C:	emp"], history.Recent);
    }

    [Fact]
    public void 履歴のクリアは前後フォルダに触らない()
    {
        var history = new FolderHistory();
        history.Visit(null, @"C:");
        history.Visit(@"C:", @"C:");

        history.Clear();
        Assert.Empty(history.Recent);
        Assert.True(history.CanGoBack);
        Assert.Equal(@"C:", history.Back(@"C:"));
    }
}
