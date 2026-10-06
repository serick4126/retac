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
        // 例外の前に取れた分も失われない
        var results = BookmarkItems.LoadAll([("a", "good"), ("b", "bad"), ("c", "good")],
            path => path == "bad" ? throw new InvalidOperationException() : new Bitmap(ok));

        Assert.Equal(["a", "b", "c"], results.Select(r => r.Item));
        Assert.NotNull(results[0].Image);
        Assert.Null(results[1].Image);
        Assert.NotNull(results[2].Image);
        foreach (var (_, image) in results) image?.Dispose();
    }

    [Fact]
    public void ドロップ指定のフォルダはアイコンが取れなければ名前に印を付けアイコンがあれば絵に描く()
    {
        var host = DispatchProxy.Create<IBookmarkHost, Host>();
        var items = new BookmarkItems(host, new Control());
        var folder = new Bookmark("保存先", BookmarkKind.Folder, @"C:\work") { DropMode = BookmarkDropMode.Copy };
        var failed = items.BarItems([folder], BookmarkBarStyle.IconAndText).Single();
        BookmarkItems.ApplyIcons([(failed, (Bitmap?)null)]);
        BookmarkItems.ApplyIcons([(failed, (Bitmap?)null)]);   // 二重に付かない
        Assert.Equal("[+] 保存先", failed.Text);

        var ok = items.BarItems([folder], BookmarkBarStyle.IconAndText).Single();
        Assert.Equal("保存先", ok.Text);
        BookmarkItems.ApplyIcons([(ok, new Bitmap(16, 16))]);
        Assert.Equal(0x008000, ok.Image is Bitmap bmp ? bmp.GetPixel(10, 9).ToArgb() & 0xFFFFFF : -1);   // 縦線
        Assert.Contains("ドロップ: 常にコピー", ok.ToolTipText);

        var textOnly = items.BarItems([folder], BookmarkBarStyle.TextOnly).Single();
        Assert.Equal("[+] 保存先", textOnly.Text);
    }
}
