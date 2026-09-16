using System.Drawing;
using ReTAC.App.Rendering;

namespace ReTAC.App;

/// <summary>
/// 配色の設定項目（5-1 節）。設定ダイアログと JSON の両方がこの 1 つの表を見る。
/// 増やすときはここに 1 行足すだけでよい。
/// </summary>
public static class ThemeSlots
{
    public sealed record Slot(string Key, string Label, Func<Theme, Color> Get, Func<Theme, Color, Theme> Set);

    public static readonly Slot[] All =
    [
        new("Background", "背景", t => t.Background, (t, c) => t with { Background = c }),
        new("Foreground", "文字", t => t.Foreground, (t, c) => t with { Foreground = c }),
        new("MarkBackground", "選択の背景", t => t.MarkBackground, (t, c) => t with { MarkBackground = c }),
        new("MarkForeground", "選択の文字", t => t.MarkForeground, (t, c) => t with { MarkForeground = c }),
        new("CursorBackground", "カーソルの背景", t => t.CursorBackground, (t, c) => t with { CursorBackground = c }),
        new("CursorForeground", "カーソルの文字", t => t.CursorForeground, (t, c) => t with { CursorForeground = c }),
        new("SystemColor", "システム属性", t => t.SystemColor, (t, c) => t with { SystemColor = c }),
        new("ReadOnlyColor", "書込禁止属性", t => t.ReadOnlyColor, (t, c) => t with { ReadOnlyColor = c }),
        new("HiddenColor", "隠し属性", t => t.HiddenColor, (t, c) => t with { HiddenColor = c }),
        new("CompressedColor", "圧縮属性", t => t.CompressedColor, (t, c) => t with { CompressedColor = c }),
        new("EncryptedColor", "暗号化属性", t => t.EncryptedColor, (t, c) => t with { EncryptedColor = c }),
        new("MarkStarColor", "マークの★", t => t.MarkStarColor, (t, c) => t with { MarkStarColor = c }),
    ];

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static Color? FromHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return ColorTranslator.FromHtml(value); }
        catch (Exception ex) when (ex is ArgumentException or FormatException) { return null; }
    }
}
