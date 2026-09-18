using ReTAC.Domain.Keys;
using ReTAC.Domain.Commands;

namespace ReTAC.Domain.Tests;

/// <summary>5-2 節 / F-06: キー割り当ての枠</summary>
public class KeySlotListTests
{
    [Fact]
    public void 修飾なしとShiftの64枠とマウスの3枠にCtrlの47枠を足した114枠()
    {
        Assert.Equal(114, KeySlotList.All.Count);
        Assert.Equal(67, KeySlotList.All.Count(s => !s.Ctrl));   // 従来の 64 ＋ マウス 3
        Assert.Equal(47, KeySlotList.All.Count(s => s.Ctrl));    // C / X / V / Z は固定のキー（R-25 / R-83）
        Assert.Equal(KeySlotList.All.Count, KeySlotList.All.Distinct().Count());
    }

    [Fact]
    public void マウスはボタン3と4と5の3枠だけで修飾付きは無い()
    {
        // R-73: 左（VK_LBUTTON = 0x01）と右（VK_RBUTTON = 0x02）は固定なので枠に入れない
        Assert.Contains(new KeyBinding(Vk.MButton), KeySlotList.All);
        Assert.Contains(new KeyBinding(Vk.XButton1), KeySlotList.All);
        Assert.Contains(new KeyBinding(Vk.XButton2), KeySlotList.All);
        Assert.DoesNotContain(KeySlotList.All, s => s.VirtualKey is 0x01 or 0x02);
        Assert.DoesNotContain(KeySlotList.All, s => IsMouse(s) && (s.Shift || s.Ctrl));
    }

    private static bool IsMouse(KeyBinding slot) =>
        slot.VirtualKey is Vk.MButton or Vk.XButton1 or Vk.XButton2;

    [Theory]
    [InlineData('C')]
    [InlineData('X')]
    [InlineData('V')]
    [InlineData('Z')]
    public void CtrlのCとXとVとZは固定のキーなので枠に無い(char letter)
    {
        // R-25 / R-83
        Assert.DoesNotContain(new KeyBinding(Vk.Letter(letter), Ctrl: true), KeySlotList.All);
    }

    [Fact]
    public void CtrlのEnterとBackSpaceとDeleteは枠にありCtrlShiftとEscは無い()
    {
        Assert.Contains(new KeyBinding(Vk.Enter, Ctrl: true), KeySlotList.All);
        Assert.Contains(new KeyBinding(Vk.Back, Ctrl: true), KeySlotList.All);
        Assert.Contains(new KeyBinding(Vk.Delete, Ctrl: true), KeySlotList.All);
        Assert.DoesNotContain(KeySlotList.All, s => s.Ctrl && s.Shift);
        Assert.DoesNotContain(KeySlotList.All, s => s.VirtualKey == Vk.Escape);   // R-18
    }

    [Fact]
    public void 既定の割り当てはすべて枠の中にある()
    {
        Assert.All(DefaultKeyMap.Create().Bindings.Keys, key => Assert.Contains(key, KeySlotList.All));
    }

    [Fact]
    public void マウスのサイドボタンの既定は戻ると進む()
    {
        // B-17: エクスプローラーやブラウザと同じ Windows 標準の挙動
        var map = DefaultKeyMap.Create();
        Assert.Equal(new BuiltinTarget(CommandId.GoBack), map.Resolve(new KeyBinding(Vk.XButton1)));
        Assert.Equal(new BuiltinTarget(CommandId.GoForward), map.Resolve(new KeyBinding(Vk.XButton2)));
        // ホイール押し込みは標準の挙動がアプリごとに違うので空にする
        Assert.Null(map.Resolve(new KeyBinding(Vk.MButton)));
    }
}
