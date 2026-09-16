namespace ReTAC.Domain.Keys;

/// <summary>
/// キー割り当ての枠（5-2 節 / F-06）。表示と保存の表記は App の KeySlots が受け持つ。
/// 修飾なし・Shift の 64 枠: 英字 26 ＋ 数字 10 ＋ F キー 12 ＋ Shift+F キー 12 ＋ その他 4（Enter・Shift+Enter・BackSpace・Delete）。
/// 卓駆の `TACKEY.KFM` は 65 枠だが、Esc は割り当てさせない（R-18 により常に「中止」として働く）。
/// Ctrl の 48 枠: 英字 23（C / X / V は固定のキー・R-25）＋ 数字 10 ＋ F キー 12 ＋ Enter・BackSpace・Delete。Ctrl+Shift は作らない。
/// マウスの 3 枠（R-73）: ボタン3（ホイール押し込み）・ボタン4・ボタン5。左右のボタンは固定なので枠に無い。
/// </summary>
public static class KeySlotList
{
    public static IReadOnlyList<KeyBinding> All { get; } = Build();

    private static List<KeyBinding> Build()
    {
        var slots = new List<KeyBinding>();
        for (var c = 'A'; c <= 'Z'; c++) slots.Add(new KeyBinding(Vk.Letter(c)));
        for (var n = 0; n <= 9; n++) slots.Add(new KeyBinding(Vk.Digit(n)));
        for (var n = 1; n <= 12; n++) slots.Add(new KeyBinding(Vk.Function(n)));
        for (var n = 1; n <= 12; n++) slots.Add(new KeyBinding(Vk.Function(n), Shift: true));
        slots.Add(new KeyBinding(Vk.Enter));
        slots.Add(new KeyBinding(Vk.Enter, Shift: true));
        slots.Add(new KeyBinding(Vk.Back));
        slots.Add(new KeyBinding(Vk.Delete));

        for (var c = 'A'; c <= 'Z'; c++)
            if (c is not ('C' or 'X' or 'V')) slots.Add(new KeyBinding(Vk.Letter(c), Ctrl: true));
        for (var n = 0; n <= 9; n++) slots.Add(new KeyBinding(Vk.Digit(n), Ctrl: true));
        for (var n = 1; n <= 12; n++) slots.Add(new KeyBinding(Vk.Function(n), Ctrl: true));
        slots.Add(new KeyBinding(Vk.Enter, Ctrl: true));
        slots.Add(new KeyBinding(Vk.Back, Ctrl: true));
        slots.Add(new KeyBinding(Vk.Delete, Ctrl: true));

        // R-73: マウスボタン3/4/5。修飾なしのみ（Shift+ボタン4 のような押し分けは作らない）。
        // ボタン6 以降は Windows が XBUTTON1/2 までしか区別しないため枠を用意しても押せない
        slots.Add(new KeyBinding(Vk.MButton));
        slots.Add(new KeyBinding(Vk.XButton1));
        slots.Add(new KeyBinding(Vk.XButton2));

        return slots;
    }
}
