using System.Windows.Forms;
using ReTAC.Domain.Keys;

namespace ReTAC.App;

/// <summary>
/// キー割り当ての枠の表記（5-2 節 / F-06）。枠そのものは <see cref="KeySlotList"/>。
/// 初期状態では修飾なし・Shift の 39 枠が有効（X-03）。Ctrl の枠の既定は Ctrl+F（B-18）だけ。
/// </summary>
public static class KeySlots
{
    public static IReadOnlyList<KeyBinding> All => KeySlotList.All;

    /// <summary>「C」「Shift+F3」「Ctrl+E」のような表示・保存用の文字列。</summary>
    public static string Label(KeyBinding binding) =>
        Prefix(binding) + ((Keys)binding.VirtualKey).ToString();

    /// <summary>
    /// 画面に出す名前。<see cref="Label"/> は設定ファイルの表記なので変えられない（読めなくなる）。
    /// 上段の数字とテンキーは働きが違うので、区別が付く書き方にする。
    /// </summary>
    public static string Display(KeyBinding binding)
    {
        var key = (Keys)binding.VirtualKey;
        var name = key switch
        {
            >= Keys.D0 and <= Keys.D9 => $"{(char)('0' + (key - Keys.D0))}（上段）",
            Keys.Return => "Enter",
            Keys.Back => "BackSpace",
            // R-73: XButton1 のままでは何のことか分からない。一方で「戻る側」のような
            // 既定の意味は名前に焼き付けない。割り当ては変えられるので嘘になる。
            // 「（ホイール押し込み）」まで書くと列幅（Scaled(90)）に収まらず途中で切れる
            Keys.MButton => "マウスボタン3",
            Keys.XButton1 => "マウスボタン4",
            Keys.XButton2 => "マウスボタン5",
            _ => key.ToString(),
        };
        return Prefix(binding) + name;
    }

    /// <summary>
    /// 大文字小文字は無視する（Ctrl+/Shift+ の接頭辞だけでなくキー名も）。手で書き換える設定ファイルと
    /// インポートするキー割り当てファイルの両方がこの経路を通るため、"ctrl+e" のような崩れた表記も
    /// "Ctrl+E" と同じ枠として読めないと、表記揺れが「読めないキー」に化けてしまう。
    /// 正規の表記は変わらず <see cref="Label"/> が決める（Parse は入力の大小文字をそのまま返さない）。
    /// </summary>
    public static KeyBinding? Parse(string label)
    {
        var ctrl = label.StartsWith("Ctrl+", StringComparison.OrdinalIgnoreCase);
        var rest = ctrl ? label[5..] : label;
        var shift = rest.StartsWith("Shift+", StringComparison.OrdinalIgnoreCase);
        var name = shift ? rest[6..] : rest;
        // Enum.TryParse は数値文字列も通してしまう（"65" が Keys.A になる）。
        // 設定ファイルは手で直せることを柱にしているので、打ち間違いが
        // 黙って別のキーに化けないようにする（V-15）
        if (name.Length == 0 || char.IsAsciiDigit(name[0]) || name[0] == '-') return null;
        return Enum.TryParse<Keys>(name, ignoreCase: true, out var key) ? new KeyBinding((ushort)key, shift, ctrl) : null;
    }

    private static string Prefix(KeyBinding binding) =>
        (binding.Ctrl ? "Ctrl+" : "") + (binding.Shift ? "Shift+" : "");
}
