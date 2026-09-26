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
        // 暗い地では明るさで分ける余地が小さい。カーソル（OS の Highlight。#2864B4 など）と同じ明るさの青緑では
        // マークした行の上のカーソルが埋もれ、暗くすると背景に沈んだ。色味を青の反対の暖色にして分ける
        MarkBackground = ColorTranslator.FromHtml("#6B5418"),
        MarkForeground = ColorTranslator.FromHtml("#FFFFFF"),
        MarkStarColor = ColorTranslator.FromHtml("#FF6B6B"),
    };

    /// <summary>R-108-2: 「Windows の設定に従う」の 8 色の推奨値。ライト用は 5-1 節の既定値そのもの。</summary>
    public static Theme Recommended(bool dark) => dark ? DarkRecommended : Default;

    /// <summary>
    /// R-108: ファイルリストに実際に渡す配色。<paramref name="stored"/> は設定に保存されている独自の配色で、
    /// フォントはどのモードでもここから取る。モードと OS の状態は起動時のものを渡す（切り替えは再起動で反映する）。
    /// 今の OS の状態を読むと、実行中に OS をダークにしてから設定を適用したとき、白い地にダーク用の色が載る。
    /// 設定画面へは <paramref name="stored"/> のほうを渡すこと。解決後の色を渡すと、OS の色が独自の配色として保存されてしまう。
    /// </summary>
    /// <param name="system">
    /// R-108-2: <paramref name="os"/> の側（ライト／ダーク）の 8 色。使うのは <see cref="ThemeSlots.SystemMode"/> の項目だけ。
    /// null なら推奨値。反対側の組は渡さない（実画面に出さない。§2.3）
    /// </param>
    public static Theme Resolve(Theme stored, ColorMode mode, OsTheme os, Theme? system = null)
    {
        // R-108-3: ハイコントラストでは利用者の色を使わない。属性による色分けもしない
        if (os.HighContrast)
            return stored with
            {
                Background = os.Window,
                Foreground = os.WindowText,
                CursorBackground = os.Highlight,
                CursorForeground = os.HighlightText,
                MarkBackground = os.WindowText,
                MarkForeground = os.Window,
                SystemColor = os.WindowText,
                ReadOnlyColor = os.WindowText,
                HiddenColor = os.WindowText,
                CompressedColor = os.WindowText,
                EncryptedColor = os.WindowText,
                MarkStarColor = os.WindowText,
            };
        if (mode == ColorMode.Custom) return stored;

        // R-108-1: 背景と文字はツリー（NSTC・ブックマークビュー）と同じ Window / WindowText にする
        var chosen = system ?? Recommended(os.Dark);
        var resolved = stored with
        {
            Background = os.Window,
            Foreground = os.WindowText,
            // ライトの Highlight（#0078D7）はマークの地（#408080）と明るさが近く、マークした行の上のカーソルが
            // 埋もれる。黒い文字なら見分けられるが、実機では読みにくかった（Q1）。独自の配色の既定と同じ純粋な青にする
            CursorBackground = os.Dark ? os.Highlight : Default.CursorBackground,
            // OS の組は使わない。ダークの HighlightText は黒で、青地（#2864B4）では読みにくい
            CursorForeground = Color.White,
        };
        foreach (var slot in ThemeSlots.SystemMode) resolved = slot.Set(resolved, slot.Get(chosen));
        return resolved;
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

/// <summary>
/// R-108: 起動時の OS の配色の状態。WinForms の Application.IsDarkModeEnabled・SystemInformation.HighContrast は
/// 今の OS の設定を読むので、実行中に OS を切り替えると、作ってあるコントロールの色と食い違う。
/// 切り替えは再起動で反映する仕様なので、描き分けはすべて起動時に取ったこれで判断する。
/// </summary>
public sealed record OsTheme
{
    public required bool Dark { get; init; }
    public required bool HighContrast { get; init; }
    public required Color Window { get; init; }
    public required Color WindowText { get; init; }
    public required Color Highlight { get; init; }
    public required Color HighlightText { get; init; }

    /// <summary>Application.SetColorMode の後に呼ぶ。前に呼ぶと SystemColors がライトの値のまま取れる。</summary>
    public static OsTheme Capture() => new()
    {
        Dark = Application.IsDarkModeEnabled,
        HighContrast = SystemInformation.HighContrast,
        Window = Fixed(SystemColors.Window),
        WindowText = Fixed(SystemColors.WindowText),
        Highlight = Fixed(SystemColors.Highlight),
        HighlightText = Fixed(SystemColors.HighlightText),
    };

    /// <summary>
    /// R-108-2: 設定画面のプレビューで、起動時の OS と反対側の組を描くときの参照色。起動中の OS からは反対側の
    /// 本当の色が取れないので、Windows の既定の値で代わりに描く（参考表示）。ダーク側は実測した値。
    /// </summary>
    public static OsTheme Reference(bool dark) => dark
        ? new()
        {
            Dark = true, HighContrast = false,
            Window = ColorTranslator.FromHtml("#323232"), WindowText = ColorTranslator.FromHtml("#F0F0F0"),
            Highlight = ColorTranslator.FromHtml("#2864B4"), HighlightText = ColorTranslator.FromHtml("#000000"),
        }
        : new()
        {
            Dark = false, HighContrast = false,
            Window = ColorTranslator.FromHtml("#FFFFFF"), WindowText = ColorTranslator.FromHtml("#000000"),
            Highlight = ColorTranslator.FromHtml("#0078D7"), HighlightText = ColorTranslator.FromHtml("#FFFFFF"),
        };

    // SystemColors の Color は KnownColor を持つだけで、値は読むたびに今のシステム色から引かれる。
    // そのまま持っても起動時の色にならないので、ARGB に直して固定する
    private static Color Fixed(Color color) => Color.FromArgb(color.ToArgb());
}
