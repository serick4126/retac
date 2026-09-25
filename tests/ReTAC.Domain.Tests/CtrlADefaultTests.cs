using ReTAC.App;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Keys;

namespace ReTAC.Domain.Tests;

/// <summary>B-23: Ctrl+A の既定と、利用者の割り当ての重なり</summary>
public class CtrlADefaultTests
{
    private static readonly KeyBinding CtrlA = new(Vk.Letter('A'), Ctrl: true);

    [Fact]
    public void 既定は全選択選択解除()
    {
        Assert.Equal(new BuiltinTarget(CommandId.ToggleAllMarks), DefaultKeyMap.Create().Resolve(CtrlA));
    }

    [Fact]
    public void 何も割り当てていなかった利用者には既定が効く()
    {
        var settings = new AppSettings { KeyBindings = [] };
        Assert.Equal(new BuiltinTarget(CommandId.ToggleAllMarks), settings.ToKeyMap().Resolve(CtrlA));
    }

    [Fact]
    public void 別のコマンドを割り当てていた利用者はそのまま()
    {
        var settings = new AppSettings { KeyBindings = new() { ["Ctrl+A"] = new BuiltinTarget(CommandId.Refresh).Serialize() } };
        Assert.Equal(new BuiltinTarget(CommandId.Refresh), settings.ToKeyMap().Resolve(CtrlA));
    }

    [Fact]
    public void 割り当てなしにした利用者は割り当てなし()
    {
        var settings = new AppSettings { KeyBindings = new() { ["Ctrl+A"] = "" } };
        Assert.Null(settings.ToKeyMap().Resolve(CtrlA));
    }

    [Fact]
    public void 既定のままなら設定ファイルに書き出さない()
    {
        var settings = new AppSettings();
        settings.FromKeyMap(DefaultKeyMap.Create());
        Assert.DoesNotContain("Ctrl+A", settings.KeyBindings.Keys);
    }
}
