using ReTAC.App;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Keys;

namespace ReTAC.Domain.Tests;

/// <summary>B-21: B はブックマークバーへ移動、Ctrl+B はブックマークを表示。どちらも枠が空いていたので新たに足す</summary>
public class CtrlBDefaultTests
{
    private static readonly KeyBinding B = new(Vk.Letter('B'));
    private static readonly KeyBinding CtrlB = new(Vk.Letter('B'), Ctrl: true);

    [Fact]
    public void 無印の既定はブックマークバーへ移動()
    {
        Assert.Equal(new BuiltinTarget(CommandId.FocusBookmarkBar), DefaultKeyMap.Create().Resolve(B));
    }

    [Fact]
    public void Ctrlの既定はブックマークを表示()
    {
        Assert.Equal(new BuiltinTarget(CommandId.ShowBookmarksView), DefaultKeyMap.Create().Resolve(CtrlB));
    }

    [Fact]
    public void 何も割り当てていなかった利用者には両方の既定が効く()
    {
        var settings = new AppSettings { KeyBindings = [] };
        Assert.Equal(new BuiltinTarget(CommandId.FocusBookmarkBar), settings.ToKeyMap().Resolve(B));
        Assert.Equal(new BuiltinTarget(CommandId.ShowBookmarksView), settings.ToKeyMap().Resolve(CtrlB));
    }

    [Fact]
    public void 別のコマンドを割り当てていた利用者はそのまま()
    {
        var settings = new AppSettings { KeyBindings = new() { ["Ctrl+B"] = new BuiltinTarget(CommandId.Refresh).Serialize() } };
        Assert.Equal(new BuiltinTarget(CommandId.Refresh), settings.ToKeyMap().Resolve(CtrlB));
    }

    [Fact]
    public void 割り当てなしにした利用者は割り当てなし()
    {
        var settings = new AppSettings { KeyBindings = new() { ["B"] = "", ["Ctrl+B"] = "" } };
        Assert.Null(settings.ToKeyMap().Resolve(B));
        Assert.Null(settings.ToKeyMap().Resolve(CtrlB));
    }

    [Fact]
    public void 既定のままなら設定ファイルに書き出さない()
    {
        var settings = new AppSettings();
        settings.FromKeyMap(DefaultKeyMap.Create());
        Assert.DoesNotContain("B", settings.KeyBindings.Keys);
        Assert.DoesNotContain("Ctrl+B", settings.KeyBindings.Keys);
    }
}
