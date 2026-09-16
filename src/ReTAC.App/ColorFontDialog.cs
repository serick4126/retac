using System.Drawing;
using System.Windows.Forms;
using ReTAC.App.Rendering;

namespace ReTAC.App;

/// <summary>
/// 配色・フォントの設定（0x8151）。5-1 節の配色とファイルリストのフォントを変える。
/// R-66-3: 行の高さ・列幅はフォントの実測値から算出されるので、変更すると自動で追従する。
/// </summary>
public sealed class ColorFontDialog : Form
{
    private readonly Dictionary<string, Button> _swatches = [];
    private Theme _theme;

    public ColorFontDialog(Theme theme)
    {
        _theme = theme;

        Text = "配色・フォントの設定";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;   // ダイアログはタスクバーに出さない（既定は true）
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(520, 340);

        Controls.Add(new Label { Text = "色の四角を押すと変更できます。", AutoSize = true, Location = new Point(16, 12) });

        var y = 40;
        var x = 16;
        foreach (var slot in ThemeSlots.All)
        {
            Controls.Add(new Label { Text = slot.Label, AutoSize = true, Location = new Point(x, y + 5) });

            var swatch = new Button
            {
                Bounds = new Rectangle(x + 130, y, 60, 24),
                BackColor = slot.Get(_theme),
                FlatStyle = FlatStyle.Flat,
                Text = "",
            };
            var captured = slot;
            swatch.Click += (_, _) => PickColor(captured);
            Controls.Add(swatch);
            _swatches[slot.Key] = swatch;

            y += 32;
            if (y <= 232) continue;
            y = 40;
            x = 270;   // 2 列に折り返す
        }

        var fontButton = new Button { Text = "フォント(&F)...", Bounds = new Rectangle(16, 268, 130, 28) };
        var fontLabel = new Label { AutoSize = true, Location = new Point(156, 274) };
        fontLabel.Text = FontLabel();
        fontButton.Click += (_, _) => { PickFont(); fontLabel.Text = FontLabel(); };
        Controls.AddRange([fontButton, fontLabel]);

        var reset = new Button { Text = "既定に戻す(&D)", Bounds = new Rectangle(16, 304, 130, 28) };
        reset.Click += (_, _) =>
        {
            _theme = Theme.Default;
            foreach (var slot in ThemeSlots.All) _swatches[slot.Key].BackColor = slot.Get(_theme);
            fontLabel.Text = FontLabel();
        };

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(320, 304, 90, 28) };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(420, 304, 90, 28) };
        Controls.AddRange([reset, ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;

        // C-1: AutoScaleMode の代入はその場で PerformAutoScale を走らせる。ClientSize と
        // Controls が揃ってからでないと、まだ 96 DPI のレイアウトを拡大できない
        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16: 座標と大きさは 96 DPI（100%）で書いてある
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    /// <summary>OK で確定した配色。</summary>
    public Theme Result => _theme;

    private string FontLabel() => $"{_theme.FontFamily}  {_theme.FontSize}pt";

    private void PickColor(ThemeSlots.Slot slot)
    {
        using var picker = new System.Windows.Forms.ColorDialog { Color = slot.Get(_theme), FullOpen = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        _theme = slot.Set(_theme, picker.Color);
        _swatches[slot.Key].BackColor = picker.Color;
    }

    private void PickFont()
    {
        using var font = new Font(_theme.FontFamily, _theme.FontSize);
        using var picker = new FontDialog { Font = font, ShowEffects = false };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        _theme = _theme with { FontFamily = picker.Font.FontFamily.Name, FontSize = picker.Font.Size };
    }
}
