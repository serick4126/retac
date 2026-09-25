using ReTAC.Domain.Commands;
using ReTAC.Domain.Navigation;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

/// <summary>B-22: 設定ファイルが無いときの初期のブックマーク 6 件</summary>
public class DefaultBookmarksTests
{
    // AppContext.BaseDirectory は必ず末尾に区切りを持つので、それを模して末尾付きで渡す
    private const string AppFolder = @"C:\Apps\ReTAC\";

    [Fact]
    public void バーに6件その他は空()
    {
        var set = DefaultBookmarks.Create(AppFolder);
        Assert.Equal(6, set.Bar.Count);
        Assert.Empty(set.Other);
    }

    [Fact]
    public void 種類と登録先とアイコンだけの並び()
    {
        var set = DefaultBookmarks.Create(AppFolder);
        Assert.Equal(
        [
            (BookmarkKind.Command, new BuiltinTarget(CommandId.GoBack).Serialize(), true),
            (BookmarkKind.Command, new BuiltinTarget(CommandId.GoForward).Serialize(), true),
            (BookmarkKind.Command, new BuiltinTarget(CommandId.GoParent).Serialize(), true),
            (BookmarkKind.Command, new BuiltinTarget(CommandId.Refresh).Serialize(), false),
            (BookmarkKind.Command, new ToolTarget(DefaultExternalTools.EditorId).Serialize(), false),
            (BookmarkKind.Folder, @"C:\Apps\ReTAC", false),
        ],
        set.Bar.Select(b => (b.Kind, b.Target, b.IconOnly)));
    }

    [Fact]
    public void 名前はすべて空()
    {
        Assert.All(DefaultBookmarks.Create(AppFolder).Bar, b => Assert.Equal("", b.Title));
    }

    [Fact]
    public void フォルダの末尾の区切りは持たないがドライブルートは残す()
    {
        Assert.Equal(@"C:\Apps\ReTAC", DefaultBookmarks.Create(@"C:\Apps\ReTAC\").Bar[5].Target);
        Assert.Equal(@"C:\", DefaultBookmarks.Create(@"C:\").Bar[5].Target);
    }

    [Fact]
    public void 全件がValidateを通る()
    {
        // R-106-2: コマンドの名前は任意なので、名前が空でも通る
        Assert.All(DefaultBookmarks.Create(AppFolder).Bar, b => Assert.Null(BookmarkRules.Validate(b)));
    }
}
