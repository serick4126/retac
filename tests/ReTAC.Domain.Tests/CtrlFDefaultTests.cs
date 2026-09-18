using ReTAC.App;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Keys;

namespace ReTAC.Domain.Tests;

/// <summary>B-18: Ctrl+F の既定と、利用者の割り当ての重なり</summary>
public class CtrlFDefaultTests
{
    private static readonly KeyBinding CtrlF = new(Vk.Letter('F'), Ctrl: true);

    [Fact]
    public void 既定はインクリメンタルサーチ()
    {
        Assert.Equal(new BuiltinTarget(CommandId.IncrementalSearch), DefaultKeyMap.Create().Resolve(CtrlF));
    }

    [Fact]
    public void 何も割り当てていなかった利用者には既定が効く()
    {
        var settings = new AppSettings { KeyBindings = [] };
        Assert.Equal(new BuiltinTarget(CommandId.IncrementalSearch), settings.ToKeyMap().Resolve(CtrlF));
    }

    [Fact]
    public void 別のコマンドを割り当てていた利用者はそのまま()
    {
        var settings = new AppSettings { KeyBindings = new() { ["Ctrl+F"] = new BuiltinTarget(CommandId.Refresh).Serialize() } };
        Assert.Equal(new BuiltinTarget(CommandId.Refresh), settings.ToKeyMap().Resolve(CtrlF));
    }

    [Fact]
    public void 割り当てなしにした利用者は割り当てなし()
    {
        var settings = new AppSettings { KeyBindings = new() { ["Ctrl+F"] = "" } };
        Assert.Null(settings.ToKeyMap().Resolve(CtrlF));
    }

    [Fact]
    public void 既定のままなら設定ファイルに書き出さない()
    {
        var settings = new AppSettings();
        settings.FromKeyMap(DefaultKeyMap.Create());
        Assert.DoesNotContain("Ctrl+F", settings.KeyBindings.Keys);
    }

    [Theory]
    [InlineData(CommandId.ToggleDriveBar)]
    [InlineData(CommandId.ShowFolderBackgroundMenu)]
    [InlineData(CommandId.ToggleAddressBar)]
    [InlineData(CommandId.ToggleBookmarkBar)]
    [InlineData(CommandId.BookmarkManage)]
    [InlineData(CommandId.BookmarkAddCurrentFolder)]
    [InlineData(CommandId.BookmarkAddCursorItem)]
    public void 背景メニューとドライブバーの切り替えには既定のキーが無い(CommandId id)
    {
        Assert.DoesNotContain(new BuiltinTarget(id), DefaultKeyMap.Create().Bindings.Values);
    }

    [Fact]
    public void CtrlZへの割り当ては読み込み時に無視される()
    {
        // R-83: 2.2.0 までに Ctrl+Z を割り当てていた設定ファイル。移行はせず、枠に無いキーとして捨てる
        var settings = new AppSettings { KeyBindings = new() { ["Ctrl+Z"] = new BuiltinTarget(CommandId.Refresh).Serialize() } };
        Assert.Null(settings.ToKeyMap().Resolve(new KeyBinding(Vk.Letter('Z'), Ctrl: true)));
    }
}
