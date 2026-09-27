using System.Drawing;
using System.Windows.Forms;
using ReTAC.App.Rendering;

namespace ReTAC.App;

/// <summary>
/// 配色・フォントの設定（0x8151）。統合設定画面（R-102）のページ。左で設定する対象を選び、右でその対象の中身を変える
/// （キー割り当て・外部ツール・ブックマークの設定と同じ形。R-101）。変更はその場で <see cref="SettingsDraft.Theme"/> へ書く。
/// R-66-3: 行の高さ・列幅はフォントの実測値から算出されるので、変更すると自動で追従する。
/// </summary>
public sealed class ColorFontPage : UserControl
{
    /// <summary>プレビューの 1 行。<paramref name="Back"/> が <see cref="Color.Empty"/> なら地の色のまま塗らない。</summary>
    private sealed record PreviewRow(string Text, Color Fore, Color Back, int Indent);

    private const int FileList = 0;

    private readonly SettingsDraft _draft;

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
    private readonly Panel _colorArea = new() { Bounds = new Rectangle(210, 234, 530, 200) };
    /// <summary>配色欄が出ない対象で、無いことの理由を書いておく（空白だけだと設定漏れに見える）。</summary>
    private readonly Label _colorNote = new()
    {
        Text = "左パネルの配色は Windows の設定に従います。ここで変えられるのはフォントだけです。",
        Bounds = new Rectangle(210, 238, 530, 40),
    };
    private readonly Button _reset = new() { Text = "この対象を既定に戻す(&D)", Bounds = new Rectangle(600, 434, 140, 28) };
    private readonly Dictionary<string, Button> _swatches = [];
    /// <summary>独自の配色のときだけ出す、12 項目の欄（ラベルと色のボタン）。</summary>
    private readonly List<Control> _customColorControls = [];
    // R-108: 配色モード。欄の最上段に置く
    private readonly RadioButton _followWindows = new() { Text = "Windows の設定に従う(&W)", AutoSize = true, Location = new Point(0, 2) };
    private readonly RadioButton _customColors = new() { Text = "独自の配色(&C)", AutoSize = true, Location = new Point(190, 2) };
    /// <summary>「Windows の設定に従う」のときの説明。起動時の OS と反対側の組のプレビューが参考表示だとも書く（3 行）。</summary>
    private readonly Label _systemNote = new() { Bounds = new Rectangle(0, 24, 530, 48) };
    // R-108-2: 「Windows の設定に従う」で編集する組と、その 8 色の欄
    // 組のラジオボタンは別の入れ物に入れる。モードのラジオボタンと同じ入れ物だと 1 つのグループになり、
    // 組を選ぶとモードの選択が外れて「独自の配色」に切り替わってしまう
    private readonly Panel _sides = new() { Bounds = new Rectangle(0, 72, 230, 26) };
    private readonly RadioButton _lightSide = new() { Text = "ライト用(&L)", AutoSize = true, Location = new Point(0, 2) };
    private readonly RadioButton _darkSide = new() { Text = "ダーク用(&K)", AutoSize = true, Location = new Point(110, 2) };
    // 名前に選んでいる側を入れる（ShowSystemSide）。「推奨値に戻す」だけでは、どちらの色が戻るのかもフォントも戻るのかも読めない
    private readonly Button _resetSystem = new() { Bounds = new Rectangle(310, 72, 220, 26) };
    private readonly Dictionary<string, Button> _systemSwatches = [];
    /// <summary>「Windows の設定に従う」のときだけ出す欄（組の切り替え・8 色・推奨値に戻す）。</summary>
    private readonly List<Control> _systemColorControls = [];
    /// <summary>R-108-3: ハイコントラスト中は、どちらのモードでも配色の設定が画面に出ない。黙っていると設定が効かない不具合に見える。</summary>
    private readonly Label _highContrastNote = new()
    {
        Text = "ハイコントラストが有効な間は、配色の設定は反映されません。",
        Bounds = new Rectangle(210, 438, 380, 20),
        Visible = false,
    };

    private Theme _theme;
    private Font? _previewFont;
    /// <summary>欄へ流し込む間は、変更を拾わない（ExternalToolPage と同じ）</summary>
    private bool _loading;

    public ColorFontPage(SettingsDraft draft)
    {
        _draft = draft;
        _theme = draft.Theme;

        AutoScaleMode = AutoScaleMode.Inherit;   // R-102-3: 拡大は SettingsDialog だけが行う
        Size = new Size(754, 488);   // 旧ダイアログのクライアント領域から OK/キャンセルの行を除いた大きさ

        _targets.Items.AddRange(["ファイル一覧", "左パネル"]);

        // 描画できない字体を選ばせない。等幅かどうかは問わない（ファイル名は比例字体でも読める）
        _family.Items.AddRange([.. FontFamily.Families.Select(f => f.Name).Order(StringComparer.CurrentCulture)]);
        _size.Items.AddRange([.. new[] { 9, 10, 11, 12, 14, 16, 18, 20, 24 }.Select(n => (object)n.ToString())]);

        BuildColorArea();

        // ラベルは入力欄の直前に足す。ニーモニック（&F など）はタブ順で次のコントロールへ移るため
        Controls.AddRange(
        [
            new Label { Text = "設定する対象を選びます。", AutoSize = true, Location = new Point(14, 16) },
            _targets,
            new Label { Text = "フォント(&F):", AutoSize = true, Location = new Point(210, 16) }, _family, _size,
            new Label { Text = "pt", AutoSize = true, Location = new Point(658, 16) },
            new Label { Text = "プレビュー", AutoSize = true, Location = new Point(210, 44) }, _preview,
            _colorArea, _colorNote, _highContrastNote, _reset,
        ]);

        _targets.SelectedIndexChanged += (_, _) => ShowTarget();
        _family.SelectedIndexChanged += (_, _) => CommitFont();
        _size.TextChanged += (_, _) => CommitFont();
        _reset.Click += (_, _) => ResetTarget();
        _followWindows.Checked = draft.ColorMode == ColorMode.System;
        _customColors.Checked = !_followWindows.Checked;
        _followWindows.CheckedChanged += (_, _) => CommitColorMode();
        // 開いたときは、今の OS の側を選んでおく（R-108-2）
        _darkSide.Checked = Program.StartupOs.Dark;
        _lightSide.Checked = !_darkSide.Checked;
        _darkSide.CheckedChanged += (_, _) => ShowSystemSide();
        _resetSystem.Click += (_, _) => ResetSystemColors();

        _targets.SelectedIndex = FileList;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _previewFont?.Dispose();
        base.Dispose(disposing);
    }

    private bool FileListSelected => _targets.SelectedIndex == FileList;

    private bool DarkSideSelected => _darkSide.Checked;

    /// <summary>選んでいる組の 8 色（下書きの値）。</summary>
    private Theme SystemSide
    {
        get => DarkSideSelected ? _draft.SystemDark : _draft.SystemLight;
        set
        {
            if (DarkSideSelected) _draft.SystemDark = value;
            else _draft.SystemLight = value;
        }
    }

    /// <summary>テストが組を選ぶための入口（ラジオボタンは表示しないと押せない）。</summary>
    internal void SelectSystemSide(bool dark) => (dark ? _darkSide : _lightSide).Checked = true;

    /// <summary>「推奨値に戻す」の本体。選んでいる組の 8 色だけを戻す。反対側の組・独自の配色・フォントには触れない（R-108-2）。</summary>
    internal void ResetSystemColors()
    {
        var recommended = Theme.Recommended(DarkSideSelected);
        var side = SystemSide;
        foreach (var slot in ThemeSlots.SystemMode) side = slot.Set(side, slot.Get(recommended));
        SystemSide = side;
        ShowSystemSide();
    }

    /// <summary>下書きへ書きつつ、局所の作業用コピーも合わせる（with 式で作り直すたびに両方直す手間を1箇所にまとめる）。</summary>
    private void SetTheme(Theme theme)
    {
        _theme = theme;
        _draft.Theme = theme;
    }

    private void BuildColorArea()
    {
        // 切り替えは再起動で反映する（R-108）。実行中に変えても、作ってしまったコントロールの色は変わらない
        _colorArea.Controls.AddRange(
        [
            _followWindows, _customColors,
            new Label { Text = "切り替えは再起動後に反映されます。", AutoSize = true, Location = new Point(320, 5) },
            _systemNote,
        ]);

        _sides.Controls.AddRange([_lightSide, _darkSide]);
        _colorArea.Controls.AddRange([_sides, _resetSystem]);
        _systemColorControls.AddRange([_sides, _resetSystem]);
        // 説明が 3 行あるので、欄は詰めて 4 段で _colorArea の高さ（200）に収める
        var sy = 100;
        var sx = 0;
        foreach (var slot in ThemeSlots.SystemMode)
        {
            var label = new Label { Text = slot.Label, AutoSize = true, Location = new Point(sx, sy + 5) };
            var swatch = new Button { Bounds = new Rectangle(sx + 116, sy, 60, 24), FlatStyle = FlatStyle.Flat, Text = "" };
            var captured = slot;
            swatch.Click += (_, _) => PickSystemColor(captured);
            _colorArea.Controls.AddRange([label, swatch]);
            _systemColorControls.AddRange([label, swatch]);
            _systemSwatches[slot.Key] = swatch;

            sy += 24;
            if (sy <= 172) continue;
            sy = 100;
            sx = 266;   // 2 列に折り返す
        }

        var y = 26;
        var x = 0;
        foreach (var slot in ThemeSlots.All)
        {
            var label = new Label { Text = slot.Label, AutoSize = true, Location = new Point(x, y + 5) };
            _colorArea.Controls.Add(label);
            _customColorControls.Add(label);

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
            _customColorControls.Add(swatch);
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
        _highContrastNote.Visible = FileListSelected && Program.StartupOs.HighContrast;
        ShowColorMode();
    }

    private void CommitColorMode()
    {
        _draft.ColorMode = _followWindows.Checked ? ColorMode.System : ColorMode.Custom;
        ShowColorMode();
    }

    private void ShowColorMode()
    {
        var custom = _draft.ColorMode == ColorMode.Custom;
        foreach (var control in _customColorControls) control.Visible = custom;
        foreach (var control in _systemColorControls) control.Visible = !custom;
        _systemNote.Visible = !custom;
        ShowSystemSide();
    }

    /// <summary>選んでいる組の 8 色を欄へ出し、説明とプレビューを合わせる。</summary>
    private void ShowSystemSide()
    {
        foreach (var slot in ThemeSlots.SystemMode) _systemSwatches[slot.Key].BackColor = slot.Get(SystemSide);
        _resetSystem.Text = (DarkSideSelected ? "ダーク用" : "ライト用") + "の色を推奨値に戻す(&R)";
        // R-108-3: ハイコントラスト中は属性とマークも OS の色で描くので、組の話をしない
        // 組を切り替えるたびに説明を差し替えると読み直しになるので、両方の組の話を 1 つの文にまとめる。
        // 起動中の OS からは反対側の本当の背景と文字が取れない（R-108-2）
        _systemNote.Text = Program.StartupOs.HighContrast
            ? "背景と文字は Windows の設定に従います。"
            : "背景と文字は Windows の設定（ライト／ダーク）に従います。属性とマークの色は、ライト用・ダーク用を別々に変えられます。"
              + (Program.StartupOs.Dark ? "ライト用" : "ダーク用") + "のプレビューは参考表示です。実際の背景と文字は Windows の設定で決まります。";
        RefreshPreview();
    }

    private void PickSystemColor(ThemeSlots.Slot slot)
    {
        using var picker = new System.Windows.Forms.ColorDialog { Color = slot.Get(SystemSide), FullOpen = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        SystemSide = slot.Set(SystemSide, picker.Color);
        ShowSystemSide();
    }

    private void CommitFont()
    {
        if (_loading || _family.SelectedItem is not string family) return;
        if (!float.TryParse(_size.Text, out var size) || size is < 1f or > 128f) return;   // 打鍵の途中は無視する

        SetTheme(FileListSelected
            ? _theme with { FontFamily = family, FontSize = size }
            : _theme with { LeftPanelFontFamily = family, LeftPanelFontSize = size });
        RefreshPreview();
    }

    /// <summary>「この対象を既定に戻す」の本体。ボタンは表示しないと押せないので、テストから直接呼べるよう internal にしてある。</summary>
    internal void ResetTarget()
    {
        var defaults = Theme.Default;
        if (FileListSelected)
        {
            var theme = _theme;
            // 12 色は独自の配色のときしか見えていない。Windows の設定に従うときに戻すと、見えないまま
            // 独自の配色が消え、モードを戻したときに元の色が無い（R-108）
            if (_draft.ColorMode == ColorMode.Custom)
                foreach (var slot in ThemeSlots.All) theme = slot.Set(theme, slot.Get(defaults));
            theme = theme with { FontFamily = defaults.FontFamily, FontSize = defaults.FontSize };
            SetTheme(theme);
            foreach (var slot in ThemeSlots.All) _swatches[slot.Key].BackColor = slot.Get(_theme);
        }
        else
        {
            SetTheme(_theme with { LeftPanelFontFamily = defaults.LeftPanelFontFamily, LeftPanelFontSize = defaults.LeftPanelFontSize });
        }
        ShowTarget();
    }

    private void PickColor(ThemeSlots.Slot slot)
    {
        using var picker = new System.Windows.Forms.ColorDialog { Color = slot.Get(_theme), FullOpen = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        SetTheme(slot.Set(_theme, picker.Color));
        _swatches[slot.Key].BackColor = picker.Color;
        RefreshPreview();
    }

    /// <summary>閉じるまで結果が見えないと、地の色と文字色が潰れる組み合わせに気づけない（R-101）。</summary>
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

        // 選んでいるモードで実際に描かれる色を見せる。Windows の設定に従うなら、選んでいる組を OS の色で解決する（R-108）。
        // 起動時の OS と反対側の組は、Windows の既定の色で代わりに描く（参考表示。ハイコントラスト中は OS の色のまま）
        var os = Program.StartupOs.HighContrast || DarkSideSelected == Program.StartupOs.Dark
            ? Program.StartupOs
            : OsTheme.Reference(DarkSideSelected);
        var shown = Theme.Resolve(_theme, _draft.ColorMode, os, SystemSide);
        (_preview.Surface, _preview.Rows) = FileListSelected
            ? (shown.Background, new PreviewRow[]
            {
                new("Documents", shown.Foreground, Color.Empty, 0),
                new("報告書_2026.xlsx", shown.CursorForeground, shown.CursorBackground, 0),
                new("setup.log", shown.MarkForeground, shown.MarkBackground, 0),
                new("desktop.ini", shown.HiddenColor, Color.Empty, 0),
            })
            // 左パネルは配色を持たず、選択の色も OS に従う。フォントだけを当てて行の詰まり方を見せる
            : (Program.StartupOs.Window, new PreviewRow[]
            {
                new("PC", Program.StartupOs.WindowText, Color.Empty, 0),
                new("ローカル ディスク (C:)", Program.StartupOs.WindowText, Color.Empty, 1),
                new("Users", Program.StartupOs.HighlightText, Program.StartupOs.Highlight, 2),
                new("Documents", Program.StartupOs.WindowText, Color.Empty, 2),
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
        public Color Surface = Program.StartupOs.Window;

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
