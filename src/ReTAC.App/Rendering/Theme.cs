using System.Drawing;
using ReTAC.Domain.Entries;

namespace ReTAC.App.Rendering;

/// <summary>ドメイン定義 5-1 節「卓駆から引き継ぐ既定値」。R-20-2 により初期値として組み込む。</summary>
public sealed record Theme
{
    public Color Background { get; init; } = ColorTranslator.FromHtml("#FFFFFF");
    public Color Foreground { get; init; } = ColorTranslator.FromHtml("#000000");

    public Color MarkBackground { get; init; } = ColorTranslator.FromHtml("#408080");
    public Color MarkForeground { get; init; } = ColorTranslator.FromHtml("#FFFFFF");

    public Color CursorBackground { get; init; } = ColorTranslator.FromHtml("#0000FF");
    public Color CursorForeground { get; init; } = ColorTranslator.FromHtml("#FFFFFF");

    public Color SystemColor { get; init; } = ColorTranslator.FromHtml("#800000");
    public Color ReadOnlyColor { get; init; } = ColorTranslator.FromHtml("#008000");
    public Color HiddenColor { get; init; } = ColorTranslator.FromHtml("#000080");
    public Color CompressedColor { get; init; } = ColorTranslator.FromHtml("#800080");
    public Color EncryptedColor { get; init; } = ColorTranslator.FromHtml("#FF00FF");

    /// <summary>マーク印の★の色（R-11-5）。</summary>
    public Color MarkStarColor { get; init; } = Color.Red;

    /// <summary>ファイルリストのフォント（5-1 節: メイリオ 16pt 標準）。</summary>
    public string FontFamily { get; init; } = "メイリオ";
    public float FontSize { get; init; } = 16f;

    /// <summary>R-101: 左パネルのフォント。一覧とは別に持つ。
    /// 一覧は 1 件ずつの読みやすさを取って 16pt だが、ツリーは一度に見渡せる行数が要るので既定を小さくする。
    /// 12pt はメイリオの行の高さが約 24px になる大きさで、16pt の約 1.3 倍の行数が入る。
    /// これ以上小さくしても、アイコンの 16px が下限として効いて行数はあまり伸びず、漢字だけが潰れる。</summary>
    public string LeftPanelFontFamily { get; init; } = "メイリオ";
    public float LeftPanelFontSize { get; init; } = 12f;

    /// <summary>R-31: マークが有効なとき、属性配色より選択の配色を優先する（現行設定は「分けない」）。</summary>
    public bool SeparateMarkColorFromAttributes { get; init; } = false;

    public static readonly Theme Default = new();

    public Color ForAttribute(AttributeColor color) => color switch
    {
        AttributeColor.System => SystemColor,
        AttributeColor.ReadOnly => ReadOnlyColor,
        AttributeColor.Hidden => HiddenColor,
        AttributeColor.Compressed => CompressedColor,
        AttributeColor.Encrypted => EncryptedColor,
        _ => Foreground,
    };
}
