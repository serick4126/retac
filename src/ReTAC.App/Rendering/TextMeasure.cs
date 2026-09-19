using System.Drawing;
using System.Windows.Forms;

namespace ReTAC.App.Rendering;

/// <summary>
/// R-66-3: <b>文字幅の計測を、描画面に紐づく Graphics で行ってはならない。</b>
/// GDI+ は GetHdc のたびにビットマップ全体を同期するため、描画面が大きいほど計測が遅くなる。
/// 1600x700 の描画面で 5,121 件を計測すると 4,029 ms、1x1 の計測専用面なら 58 ms（68 倍）。
/// 列幅の自動決定はフォルダを開くたびに全件を走査するので、ここを外すと実用にならない。
/// </summary>
public sealed class TextMeasure : IDisposable
{
    /// <summary>描画と計測で同じ結果を得るため、DrawText 側と必ず同じフラグを使う。</summary>
    public const TextFormatFlags Flags =
        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

    private static readonly Size Unbounded = new(int.MaxValue, int.MaxValue);

    private readonly Bitmap _surface;
    private readonly Graphics _graphics;

    /// <param name="dpi">描画先と同じ DPI を渡す。ここがずれると計測幅と描画幅が食い違う。</param>
    public TextMeasure(Font font, float dpi)
    {
        _surface = new Bitmap(1, 1);
        _surface.SetResolution(dpi, dpi);
        _graphics = Graphics.FromImage(_surface);
        Font = font;
    }

    public Font Font { get; }

    public int Width(string text) =>
        text.Length == 0 ? 0 : TextRenderer.MeasureText(_graphics, text, Font, Unbounded, Flags).Width;

    public int LineHeight() => TextRenderer.MeasureText(_graphics, "Mg", Font, Unbounded, Flags).Height;

    public void Dispose()
    {
        _graphics.Dispose();
        _surface.Dispose();
    }
}
