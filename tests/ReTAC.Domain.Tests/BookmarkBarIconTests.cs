using System.Drawing;
using System.Reflection;
using ReTAC.App;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Navigation;
using System.Windows.Forms;

namespace ReTAC.Domain.Tests;

/// <summary>R-106 / P11-3: 「アイコンだけ」の外部ツールのボタンが、名前も画像も無い空のボタンにならない。</summary>
public class BookmarkBarIconTests
{
    /// <summary>外部ツールのパスだけを返し、ほかは既定値を返すホスト。</summary>
    public class Host : DispatchProxy
    {
        public string? ToolPath;
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method!.Name == nameof(IBookmarkHost.IconPathOf) ? ToolPath
            : method.ReturnType == typeof(string) ? ""
            : method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType)
            : null;
    }

    private static ToolStripItem ToolButton(string toolPath, BookmarkBarStyle style = BookmarkBarStyle.IconAndText)
    {
        var host = DispatchProxy.Create<IBookmarkHost, Host>();
        ((Host)(object)host).ToolPath = toolPath;
        var items = new BookmarkItems(host, new Control());
        var tool = new Bookmark("エディタ", BookmarkKind.Command, new ToolTarget(1).Serialize()) { IconOnly = true };
        return items.BarItems([tool], style).Single();
    }

    [Theory]
    [InlineData("", BookmarkBarStyle.IconAndText)]
    [InlineData("   ", BookmarkBarStyle.IconAndText)]
    [InlineData("", BookmarkBarStyle.IconOnly)]
    public void パスが空の外部ツールは最初から名前を出す(string path, BookmarkBarStyle style)
    {
        Assert.NotEqual(ToolStripItemDisplayStyle.Image, ToolButton(path, style).DisplayStyle);
    }

    [Fact]
    public void アイコンが取れなかったら名前を出す()
    {
        var item = ToolButton(@"C:\no\such\tool.exe");
        Assert.Equal(ToolStripItemDisplayStyle.Image, item.DisplayStyle);   // 取る前はアイコンがある前提
        BookmarkItems.ApplyIcons([(item, (Bitmap?)null)]);
        Assert.Equal(ToolStripItemDisplayStyle.Text, item.DisplayStyle);
    }

    [Fact]
    public void アイコンの取得が例外を出してもその項目だけアイコンなしにしてほかは続ける()
    {
        using var ok = new Bitmap(16, 16);
        var results = BookmarkItems.LoadAll([("a", "bad"), ("b", "good")],
            path => path == "bad" ? throw new InvalidOperationException() : new Bitmap(ok));

        Assert.Null(results[0].Image);
        Assert.NotNull(results[1].Image);
        results[1].Image!.Dispose();
    }
}
