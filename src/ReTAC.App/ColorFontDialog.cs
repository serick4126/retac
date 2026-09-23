using System.Drawing;
using System.Windows.Forms;
using ReTAC.App.Rendering;

namespace ReTAC.App;

/// <summary>
/// 配色・フォントの設定（0x8151）。左で設定する対象を選び、右でその対象の中身を変える
/// （キー割り当て・外部ツール・ブックマークの設定と同じ形。R-101）。
/// R-66-3: 行の高さ・列幅はフォントの実測値から算出されるので、変更すると自動で追従する。
/// </summary>
public sealed class ColorFontDialog : Form
{
    /// <summary>プレビューの 1 行。<paramref name="Back"/> が <see cref="Color.Empty"/> なら地の色のまま塗らない。</summary>
    private sealed record PreviewRow(string Text, Color Fore, Color Back, int Indent);

    private const int FileList = 0;

    private readonly ListBox _targets = new()
    {
        Bounds = new Rectangle(14, 40, 180, 422),
        IntegralHeight = false,
    };
    private readonly ComboBox _family = new()
    {
        Bounds = new Rectangle(286, 12, 296, 23),
        DropDownStyle = ComboBoxStyle.DropDownList,
    };
    private readonly ComboBox _size = new()
    {
        Bounds = new Rectangle(590, 12, 62, 23),
        DropDownStyle = ComboBoxStyle.DropDown,
    };
    // R-101: メイリオ 16pt は 1 行が約 37px。4 行とも見えるだけの高さを取る
    private readonly PreviewBox _preview = new() { Bounds = new Rectangle(210, 64, 530, 160) };
    private readonly Panel _colorArea = new() { Bounds = new Rectangle(210, 234, 530, 192) };
    /// <summary>配色欄が出ない対象で、無いことの理由を書いておく（空白だけだと設定漏れに見える）。</summary>
    private readonly Label _colorNote = new()
    {
        Text = "左パネルの配色は Windows の設定に従います。ここで変えられるのはフォントだけです。",
        Bounds = new Rectangle(210, 238, 530, 40),
    };
    private readonly Button _reset = new() { Text = "この対象を既定に戻す(&D)", Bounds = new Rectangle(600, 434, 140, 28) };
    private readonly Dictionary<string, Button> _swatches = [];

    private Theme _theme;
    private Font? _previewFont;
    /// <summary>欄へ流し込む間は、変更を拾わない（ExternalToolDialog と同じ）</summary>
    private bool _loading;

    public ColorFontDialog(Theme theme)
    {
        _theme = theme;

        Text = "配色・フォントの設定";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;   // ダイアログはタスクバーに出さない（既定は true）
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(754, 530);

        _targets.Items.AddRange(["ファイル一覧", "左パネル"]);

        // 描画できない字体を選ばせない。等幅かどうかは問わない（ファイル名は比例字体でも読める）
        _family.Items.AddRange([.. FontFamily.Families.Select(f => f.Name).Order(StringComparer.CurrentCulture)]);
        _size.Items.AddRange([.. new[] { 9, 10, 11, 12, 14, 16, 18, 20, 24 }.Select(n => (object)n.ToString())]);

        BuildColorArea();

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(550, 488, 90, 28) };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(650, 488, 90, 28) };

        // ラベルは入力欄の直前に足す。ニーモニック（&F など）はタブ順で次のコントロールへ移るため
        Controls.AddRange(
        [
            new Label { Text = "設定する対象を選びます。", AutoSize = true, Location = new Point(14, 16) },
            _targets,
            new Label { Text = "フォント(&F):", AutoSize = true, Location = new Point(210, 16) }, _family, _size,
            new Label { Text = "pt", AutoSize = true, Location = new Point(658, 16) },
            new Label { Text = "プレビュー", AutoSize = true, Location = new Point(210, 44) }, _preview,
            _colorArea, _colorNote, _reset, ok, cancel,
        ]);
        AcceptButton = ok;
        CancelButton = cancel;

        _targets.SelectedIndexChanged += (_, _) => ShowTarget();
        _family.SelectedIndexChanged += (_, _) => CommitFont();
        _size.TextChanged += (_, _) => CommitFont();
        _reset.Click += (_, _) => ResetTarget();

        _targets.SelectedIndex = FileList;

        // C-1: AutoScaleMode の代入はその場で PerformAutoScale を走らせる。ClientSize と
        // Controls が揃ってからでないと、まだ 96 DPI のレイアウトを拡大できない
        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16: 座標と大きさは 96 DPI（100%）で書いてある
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    /// <summary>OK で確定した配色とフォント。</summary>
    public Theme Result => _theme;

    protected override void Dispose(bool disposing)
    {
        if (disposing) _previewFont?.Dispose();
        base.Dispose(disposing);
    }

    private bool FileListSelected => _targets.SelectedIndex == FileList;

    private void BuildColorArea()
    {
        _colorArea.Controls.Add(new Label { Text = "配色", AutoSize = true, Location = new Point(0, 4) });

        var y = 26;
        var x = 0;
        foreach (var slot in ThemeSlots.All)
        {
            _colorArea.Controls.Add(new Label { Text = slot.Label, AutoSize = true, Location = new Point(x, y + 5) });

            var swatch = new Button
            {
                Bounds = new Rectangle(x + 116, y, 60, 24),
                BackColor = slot.Get(_theme),
                FlatStyle = FlatStyle.Flat,
                Text = "",
            };
            var captured = slot;
            swatch.Click += (_, _) => PickColor(captured);
            _colorArea.Controls.Add(swatch);
            _swatches[slot.Key] = swatch;

            y += 28;
            if (y <= 166) continue;
            y = 26;
            x = 266;   // 2 列に折り返す
        }
    }

    private void ShowTarget()
    {
        _loading = true;
        var (family, size) = FileListSelected
            ? (_theme.FontFamily, _theme.FontSize)
            : (_theme.LeftPanelFontFamily, _theme.LeftPanelFontSize);

        // 設定ファイルを手で書けば、入っていない字体の名前も来る（R-55）。選べる形にして落とさない
        if (!_family.Items.Contains(family)) _family.Items.Insert(0, family);
        _family.SelectedItem = family;
        _size.Text = FormatSize(size);
        _loading = false;

        _colorArea.Visible = FileListSelected;
        _colorNote.Visible = !FileListSelected;
        RefreshPreview();
    }

    private void CommitFont()
    {
        if (_loading || _family.SelectedItem is not string family) return;
        if (!float.TryParse(_size.Text, out var size) || size is < 1f or > 128f) return;   // 打鍵の途中は無視する

        _theme = FileListSelected
            ? _theme with { FontFamily = family, FontSize = size }
            : _theme with { LeftPanelFontFamily = family, LeftPanelFontSize = size };
        RefreshPreview();
    }

    private void ResetTarget()
    {
        var defaults = Theme.Default;
        if (FileListSelected)
        {
            foreach (var slot in ThemeSlots.All) _theme = slot.Set(_theme, slot.Get(defaults));
            foreach (var slot in ThemeSlots.All) _swatches[slot.Key].BackColor = slot.Get(_theme);
            _theme = _theme with { FontFamily = defaults.FontFamily, FontSize = defaults.FontSize };
        }
        else
        {
            _theme = _theme with { LeftPanelFontFamily = defaults.LeftPanelFontFamily, LeftPanelFontSize = defaults.LeftPanelFontSize };
        }
        ShowTarget();
    }

    private void PickColor(ThemeSlots.Slot slot)
    {
        using var picker = new System.Windows.Forms.ColorDialog { Color = slot.Get(_theme), FullOpen = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        _theme = slot.Set(_theme, picker.Color);
        _swatches[slot.Key].BackColor = picker.Color;
        RefreshPreview();
    }

    /// <summary>OK を押すまで結果が見えないと、地の色と文字色が潰れる組み合わせに気づけない（R-101）。</summary>
    private void RefreshPreview()
    {
        // Control.Font は、値の等しい Font を代入しても差し替えず前の実体を持ち続ける。
        // 受け取られなかったほうを捨てないと、描いている最中のフォントを壊す
        // （ファイル一覧と左パネルを同じ大きさにすると必ず起きる）
        var wanted = FileListSelected
            ? new Font(_theme.FontFamily, _theme.FontSize)
            : new Font(_theme.LeftPanelFontFamily, _theme.LeftPanelFontSize);
        _preview.Font = wanted;
        if (ReferenceEquals(_preview.Font, wanted))
        {
            _previewFont?.Dispose();
            _previewFont = wanted;
        }
        else
        {
            wanted.Dispose();
        }

        (_preview.Surface, _preview.Rows) = FileListSelected
            ? (_theme.Background, new PreviewRow[]
            {
                new("Documents", _theme.Foreground, Color.Empty, 0),
                new("報告書_2026.xlsx", _theme.CursorForeground, _theme.CursorBackground, 0),
                new("setup.log", _theme.MarkForeground, _theme.MarkBackground, 0),
                new("desktop.ini", _theme.HiddenColor, Color.Empty, 0),
            })
            // 左パネルは配色を持たず、選択の色も OS に従う。フォントだけを当てて行の詰まり方を見せる
            : (SystemColors.Window, new PreviewRow[]
            {
                new("PC", SystemColors.WindowText, Color.Empty, 0),
                new("ローカル ディスク (C:)", SystemColors.WindowText, Color.Empty, 1),
                new("Users", SystemColors.HighlightText, SystemColors.Highlight, 2),
                new("Documents", SystemColors.WindowText, Color.Empty, 2),
            });
        _preview.Invalidate();
    }

    /// <summary>小数のある大きさだけ小数点以下を出す（16 を「16」、10.5 を「10.5」と見せる）。</summary>
    private static string FormatSize(float size) => size % 1f == 0f ? ((int)size).ToString() : size.ToString("0.#");

    /// <summary>フォントと配色を同時に当てたサンプル。入る行だけ描き、はみ出す行は描かない
    /// （大きな字を選んだときに、行が半分だけ出るのを避ける）。</summary>
    private sealed class PreviewBox : Control
    {
        public PreviewBox() =>
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);

        // プロパティにするとデザイナ用の直列化属性を求められる（WFO1000）。designer からは使わないので欄で持つ
        public IReadOnlyList<PreviewRow> Rows = [];
        public Color Surface = SystemColors.Window;

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var surface = new SolidBrush(Surface)) e.Graphics.FillRectangle(surface, ClientRectangle);

            var pad = LogicalToDeviceUnits(4);
            var step = LogicalToDeviceUnits(16);
            var height = Font.Height;
            var y = pad;
            foreach (var row in Rows)
            {
                if (y + height > ClientSize.Height - pad) break;
                var bounds = new Rectangle(pad, y, ClientSize.Width - pad * 2, height);
                if (row.Back != Color.Empty)
                    using (var back = new SolidBrush(row.Back)) e.Graphics.FillRectangle(back, bounds);
                TextRenderer.DrawText(e.Graphics, row.Text, Font, bounds with { X = bounds.X + row.Indent * step }, row.Fore,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                y += height;
            }

            ControlPaint.DrawBorder(e.Graphics, ClientRectangle, SystemColors.ControlDark, ButtonBorderStyle.Solid);
        }
    }
}
