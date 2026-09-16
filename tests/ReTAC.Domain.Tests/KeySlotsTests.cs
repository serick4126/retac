using ReTAC.App;
using ReTAC.Domain.Keys;

namespace ReTAC.Domain.Tests;

/// <summary>R-73: マウスの枠の表記。設定ファイルの表記は後から変えられない（読めなくなる）</summary>
public class KeySlotsTests
{
    [Theory]
    [InlineData(Vk.MButton, "MButton")]
    [InlineData(Vk.XButton1, "XButton1")]
    [InlineData(Vk.XButton2, "XButton2")]
    public void マウスの枠は設定ファイルの表記を往復する(ushort virtualKey, string label)
    {
        var slot = new KeyBinding(virtualKey);
        Assert.Equal(label, KeySlots.Label(slot));
        Assert.Equal(slot, KeySlots.Parse(label));
    }

    [Fact]
    public void マウスの枠の画面表記はマウスボタンで始まる()
    {
        // XButton1 のままでは何を指すか分からない
        Assert.Equal("マウスボタン3", KeySlots.Display(new KeyBinding(Vk.MButton)));
        Assert.Equal("マウスボタン4", KeySlots.Display(new KeyBinding(Vk.XButton1)));
        Assert.Equal("マウスボタン5", KeySlots.Display(new KeyBinding(Vk.XButton2)));
    }
}
