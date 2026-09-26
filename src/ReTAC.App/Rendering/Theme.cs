using System.Drawing;
using System.Windows.Forms;
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

    /// <summary>
    /// R-108-2: 「Windows の設定に従う」でダークのときの推奨値。5-1 節の既定値は暗い背景では読めない
    /// （隠し属性の #000080 は #323232 との比が 1.2）。どれも #323232 との比を 4.5 以上にしてある。
    /// 背景・文字・カーソルはここでは決めない（OS の色を使う）。
    /// </summary>
    public static readonly Theme DarkRecommended = Default with
    {
        SystemColor = ColorTranslator.FromHtml("#FF8A80"),
        ReadOnlyColor = ColorTranslator.FromHtml("#7CD67C"),
        HiddenColor = ColorTranslator.FromHtml("#8CB4FF"),
        CompressedColor = ColorTranslator.FromHtml("#E09AE0"),
        EncryptedColor = ColorTranslator.FromHtml("#FF80FF"),
        MarkBackground = ColorTranslator.FromHtml("#1F6B6B"),
        MarkForeground = ColorTranslator.FromHtml("#FFFFFF"),
        MarkStarColor = ColorTranslator.FromHtml("#FF6B6B"),
    };

    /// <summary>
    /// R-108: ファイルリストに実際に渡す配色。<paramref name="stored"/> は設定に保存されている独自の配色で、
    /// フォントはどのモードでもここから取る。モードは起動時のものを渡す（切り替えは再起動で反映する）。
    /// 設定画面へは <paramref name="stored"/> のほうを渡すこと。解決後の色を渡すと、OS の色が独自の配色として保存されてしまう。
    /// </summary>
    public static Theme Resolve(Theme stored, ColorMode mode)
    {
        // R-108-3: ハイコントラストでは利用者の色を使わない。属性による色分けもしない
        if (SystemInformation.HighContrast)
            return stored with
            {
                Background = SystemColors.Window,
                Foreground = SystemColors.WindowText,
                CursorBackground = SystemColors.Highlight,
                CursorForeground = SystemColors.HighlightText,
                MarkBackground = SystemColors.WindowText,
                MarkForeground = SystemColors.Window,
                SystemColor = SystemColors.WindowText,
                ReadOnlyColor = SystemColors.WindowText,
                HiddenColor = SystemColors.WindowText,
                CompressedColor = SystemColors.WindowText,
                EncryptedColor = SystemColors.WindowText,
                MarkStarColor = SystemColors.WindowText,
            };
        if (mode == ColorMode.Custom) return stored;

        // R-108-1: 背景と文字はツリー（NSTC・ブックマークビュー）と同じ SystemColors.Window / WindowText にする
        var recommended = Application.IsDarkModeEnabled ? DarkRecommended : Default;
        return stored with
        {
            Background = SystemColors.Window,
            Foreground = SystemColors.WindowText,
            CursorBackground = SystemColors.Highlight,
            // OS の組は使わない。ダークの HighlightText は黒で、青地（#2864B4）との比が 3.6 しかない。
            // 見やすさを最優先する（Q1）ので、僅差でも比の高いほうを毎回選ぶ（ライトの #0078D7 では黒 4.6 ＞ 白 4.47）
            CursorForeground = ReadableTextOn(SystemColors.Highlight),
            MarkBackground = recommended.MarkBackground,
            MarkForeground = recommended.MarkForeground,
            SystemColor = recommended.SystemColor,
            ReadOnlyColor = recommended.ReadOnlyColor,
            HiddenColor = recommended.HiddenColor,
            CompressedColor = recommended.CompressedColor,
            EncryptedColor = recommended.EncryptedColor,
            MarkStarColor = recommended.MarkStarColor,
        };
    }

    /// <summary>白と黒のうち、<paramref name="background"/> とのコントラスト比が高いほう。</summary>
    public static Color ReadableTextOn(Color background) =>
        Contrast(background, Color.White) >= Contrast(background, Color.Black) ? Color.White : Color.Black;

    /// <summary>WCAG のコントラスト比（1〜21）。</summary>
    public static double Contrast(Color a, Color b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var x = v / 255.0;
            return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

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

/// <summary>R-108: 配色モード。切り替えは再起動後に反映する。</summary>
public enum ColorMode
{
    /// <summary>Windows の設定に従う。背景・文字・カーソルは OS の色、属性とマークは推奨値</summary>
    System,
    /// <summary>独自の配色。ファイルリストに 12 項目の色を当てる（ツリーなどは OS に従う）</summary>
    Custom,
}
