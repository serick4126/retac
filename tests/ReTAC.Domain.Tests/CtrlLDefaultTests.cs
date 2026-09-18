using ReTAC.App;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Keys;

namespace ReTAC.Domain.Tests;

/// <summary>B-19: Ctrl+L の既定と、利用者の割り当ての重なり</summary>
public class CtrlLDefaultTests
{
    private static readonly KeyBinding CtrlL = new(Vk.Letter('L'), Ctrl: true);

    [Fact]
    public void 既定はダイレクトジャンプ()
    {
        Assert.Equal(new BuiltinTarget(CommandId.DirectJump), DefaultKeyMap.Create().Resolve(CtrlL));
    }

    [Fact]
    public void 何も割り当てていなかった利用者には既定が効く()
    {
        var settings = new AppSettings { KeyBindings = [] };
        Assert.Equal(new BuiltinTarget(CommandId.DirectJump), settings.ToKeyMap().Resolve(CtrlL));
    }

    [Fact]
    public void 別のコマンドを割り当てていた利用者はそのまま()
    {
        var settings = new AppSettings { KeyBindings = new() { ["Ctrl+L"] = new BuiltinTarget(CommandId.Refresh).Serialize() } };
        Assert.Equal(new BuiltinTarget(CommandId.Refresh), settings.ToKeyMap().Resolve(CtrlL));
    }

    [Fact]
    public void 割り当てなしにした利用者は割り当てなし()
    {
        var settings = new AppSettings { KeyBindings = new() { ["Ctrl+L"] = "" } };
        Assert.Null(settings.ToKeyMap().Resolve(CtrlL));
    }

    [Fact]
    public void 既定のままなら設定ファイルに書き出さない()
    {
        var settings = new AppSettings();
        settings.FromKeyMap(DefaultKeyMap.Create());
        Assert.DoesNotContain("Ctrl+L", settings.KeyBindings.Keys);
    }
}
