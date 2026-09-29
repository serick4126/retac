using System.Drawing;
using System.Windows.Forms;
using ReTAC.Domain.Listing;

namespace ReTAC.App;

/// <summary>
/// ファイルビューの設定（R-112-2）。統合設定画面（R-102）のページ。左で系統（共通と 4 つ）を選び、
/// 右にその系統の部品だけを出す（INV-FILEVIEW-SETTINGS-PER-GROUP）。
///
/// 下書きの <see cref="SettingsDraft.FileViews"/> は不変の record なので、部品が変わるたびに
/// with で作った新しい値に差し替える。列の並びと情報の選択も、画面の並びから新しい配列を作って入れる。
/// 下書きの持つ列は共有の設定と同じインスタンスなので、書き換えると確定前に共有の設定が変わる（INV-SETTINGS-DRAFT）。
/// </summary>
public sealed class FileViewPage : UserControl
{
    /// <summary>一覧の項目。値と画面の名前の組（列挙の名前は英語なので、表示の文字列を別に持つ）。</summary>
    private sealed record Choice<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }

    private const int InputLeft = 230;
    private const int RowHeight = 32;

    private static readonly string[] WidthModes = ["すべて表示", "自動", "最大文字数"];   // NameWidthMode の並び
    private static readonly string[] CheckBoxModes = ["ホバー中とマーク済み", "常に表示"];   // CheckBoxMode の並び
    private static readonly Dictionary<DetailsColumn, string> ColumnNames = new()
    {
        [DetailsColumn.Extension] = "拡張子",
        [DetailsColumn.Size] = "サイズ",
        [DetailsColumn.Modified] = "更新日時",
        [DetailsColumn.Created] = "作成日時",
        [DetailsColumn.Type] = "種類",
        [DetailsColumn.Attributes] = "属性",
    };
    private static readonly Dictionary<TileInfo, string> InfoNames = new()
    {
        [TileInfo.Type] = "種類",
        [TileInfo.Size] = "サイズ",
        [TileInfo.Modified] = "更新日時",
        [TileInfo.Created] = "作成日時",
        [TileInfo.Attributes] = "属性",
    };

    private const string DragDropHelp = "ON: フォルダの項目の上に落とすと、そのフォルダへコピー・移動する。\nOFF: 今のフォルダへ入れる。ファイル表示パネルの中へは落とせない。";

    private readonly SettingsDraft _draft;
    /// <summary>部品へ流し込む間は、変更を拾わない</summary>
    private bool _loading;
    private readonly Panel[] _panels;
    private readonly ToolTip _tips = new();

    internal ListBox Groups { get; } = new() { Bounds = new Rectangle(14, 14, 220, 428), IntegralHeight = false };

    // 共通
    internal CheckBox Overlays { get; } = Check("同期状態などの印をアイコンに表示する(&O)");
    private readonly CheckBox _hideKnownExtensions = Check("登録されている拡張子は表示しない(&X)");
    // 一覧
    private readonly CheckBox _listAlignExtension = Check("拡張子を揃えて表示する(&X)");
    internal CheckBox ListDragDrop { get; } = Check("ファイル表示パネル内でドラッグ＆ドロップを使用する(&D)");
    private readonly ComboBox _listWidth = Combo(WidthModes, 130);
    private readonly NumericUpDown _listChars = Chars();
    // 詳細
    private readonly CheckBox _detailsDragDrop = Check("ファイル表示パネル内でドラッグ＆ドロップを使用する(&D)");
    private readonly ComboBox _detailsWidth = Combo(WidthModes, 130);
    private readonly NumericUpDown _detailsChars = Chars();
    private readonly CheckBox _detailsAlignExtension = Check("拡張子を揃えて表示する(&X)");
    internal CheckedListBox DetailsColumns { get; } = new() { Size = new Size(200, 124), CheckOnClick = true, IntegralHeight = false };
    internal Button MoveColumnUp { get; } = new() { Text = "上へ(&U)", Bounds = new Rectangle(210, 0, 90, 28) };
    private readonly Button _moveColumnDown = new() { Text = "下へ(&N)", Bounds = new Rectangle(210, 0, 90, 28) };
    private readonly CheckBox _fitColumns = Check("列幅をウィンドウ幅に合わせて縮める(&F)");
    // アイコン
    private readonly CheckBox _iconsDragDrop = Check("ファイル表示パネル内でドラッグ＆ドロップを使用する(&D)");
    private readonly ComboBox _mediumSize = SizeCombo();
    private readonly ComboBox _largeSize = SizeCombo();
    private readonly ComboBox _extraLargeSize = SizeCombo();
    internal ComboBox IconsCheckBoxes { get; } = Combo(CheckBoxModes, 200);
    internal CheckBox IconsThumbnails { get; } = Check("サムネイルを表示する(&T)");
    internal CheckBox IconsFolderThumbnails { get; } = Check("フォルダに中身のサムネイルを表示する(&H)");
    internal NumericUpDown NameLines { get; } = new() { Width = 60, Minimum = FileViewLimits.MinNameLines, Maximum = FileViewLimits.MaxNameLines };
    internal ComboBox SmallIconWidth { get; } = Combo(WidthModes, 130);
    internal NumericUpDown SmallIconChars { get; } = Chars();
    // 並べて表示・コンテンツ
    private readonly CheckBox _tilesDragDrop = Check("ファイル表示パネル内でドラッグ＆ドロップを使用する(&D)");
    private readonly CheckedListBox _tileInfo = new() { Size = new Size(200, 106), CheckOnClick = true, IntegralHeight = false };
    private readonly ComboBox _tilesSize = SizeCombo();
    private readonly ComboBox _contentSize = SizeCombo();
    internal ComboBox TilesCheckBoxes { get; } = Combo(CheckBoxModes, 200);
    internal CheckBox TilesThumbnails { get; } = Check("サムネイルを表示する(&T)");
    internal CheckBox TilesFolderThumbnails { get; } = Check("フォルダに中身のサムネイルを表示する(&H)");

    public FileViewPage(SettingsDraft draft)
    {
        _draft = draft;
        AutoScaleMode = AutoScaleMode.Inherit;   // R-102-3: 拡大は SettingsDialog だけが行う
        Size = new Size(754, 448);

        // ラベルは入力欄の直前に足す。ニーモニック（&W など）はタブ順で次のコントロールへ移るため。
        // アクセスキーはパネルの中で重ねず、ダイアログの「適用(&S)」の S と、ダイアログで避けている A は使わない
        _panels =
        [
            Arrange(new Panel(), (Overlays, 0), (_hideKnownExtensions, 32)),
            Arrange(new Panel(),
                (ListDragDrop, 0),
                (Row("名前の列の幅(&W):", _listWidth), 36),
                (Row("文字数(&R):", _listChars), 36 + RowHeight),
                (_listAlignExtension, 106)),
            Arrange(new Panel(),
                (_detailsDragDrop, 0),
                (Row("名前の列の幅(&W):", _detailsWidth), 36),
                (Row("文字数(&R):", _detailsChars), 36 + RowHeight),
                (_detailsAlignExtension, 106),
                (Caption("表示する列(&L):"), 140),
                (DetailsColumns, 160),
                (MoveColumnUp, 160),
                (_moveColumnDown, 194),
                (_fitColumns, 296)),
            Arrange(new Panel(),
                (_iconsDragDrop, 0),
                (Row("中アイコンの大きさ(&M):", _mediumSize), 36),
                (Row("大アイコンの大きさ(&L):", _largeSize), 36 + RowHeight),
                (Row("特大アイコンの大きさ(&E):", _extraLargeSize), 36 + RowHeight * 2),
                (Row("チェックボックス(&K):", IconsCheckBoxes), 36 + RowHeight * 3),
                (IconsThumbnails, 172),
                (IconsFolderThumbnails, 200),
                (Row("名前の行数(&I):", NameLines), 234),
                (Row("小アイコンの項目の幅(&W):", SmallIconWidth), 234 + RowHeight),
                (Row("文字数(&R):", SmallIconChars), 234 + RowHeight * 2)),
            Arrange(new Panel(),
                (_tilesDragDrop, 0),
                (Caption("名前の横に出す情報(&I):"), 36),
                (_tileInfo, 56),
                (Row("並べて表示のアイコンの大きさ(&Z):", _tilesSize), 176),
                (Row("コンテンツのアイコンの大きさ(&C):", _contentSize), 176 + RowHeight),
                (Row("チェックボックス(&K):", TilesCheckBoxes), 176 + RowHeight * 2),
                (TilesThumbnails, 280),
                (TilesFolderThumbnails, 308)),
        ];

        foreach (var name in new[] { "共通", "一覧", "詳細", "アイコン", "並べて表示・コンテンツ" }) Groups.Items.Add(name);
        Controls.Add(Groups);
        foreach (var panel in _panels)
        {
            panel.Bounds = new Rectangle(250, 14, 490, 428);
            Controls.Add(panel);
        }

        // D&D だけに説明を付ける（ほかの部品は機能が付くときに付ける）。「?」は部品と同じパネルに置く
        foreach (var (box, panel) in new[] { (ListDragDrop, _panels[1]), (_detailsDragDrop, _panels[2]), (_iconsDragDrop, _panels[3]), (_tilesDragDrop, _panels[4]) })
            OptionHelp.Attach(panel, _tips, (box, DragDropHelp));

        Fill();
        Wire();

        Groups.SelectedIndexChanged += (_, _) =>
        {
            for (var i = 0; i < _panels.Length; i++) _panels[i].Visible = i == Groups.SelectedIndex;
        };
        Groups.SelectedIndex = 0;
    }

    /// <summary>
    /// SettingsDialog は隠れたページも PerformLayout で測り直させる（AutoSize の二重拡大を防ぐ）。
    /// パネルはページの子なので、中の AutoSize の部品まで届くようにここで配る。
    /// </summary>
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        foreach (var panel in _panels ?? []) panel.PerformLayout();
    }

    private void Fill()
    {
        _loading = true;
        var views = _draft.FileViews;

        Overlays.Checked = views.Common.ShowOverlays;
        _hideKnownExtensions.Checked = views.Common.HideKnownExtensions;
        _listAlignExtension.Checked = views.List.AlignExtension;
        _detailsAlignExtension.Checked = views.Details.AlignExtension;

        ListDragDrop.Checked = views.List.InPanelDragDrop;
        LoadWidth(_listWidth, _listChars, views.List.NameWidth);

        _detailsDragDrop.Checked = views.Details.InPanelDragDrop;
        LoadWidth(_detailsWidth, _detailsChars, views.Details.NameWidth);
        foreach (var column in views.Details.Columns)
            DetailsColumns.Items.Add(new Choice<DetailsColumn>(column.Column, ColumnNames[column.Column]), column.Visible);
        _fitColumns.Checked = views.Details.FitColumnsToWindow;

        _iconsDragDrop.Checked = views.Icons.InPanelDragDrop;
        LoadSize(_mediumSize, views.Icons.MediumSize);
        LoadSize(_largeSize, views.Icons.LargeSize);
        LoadSize(_extraLargeSize, views.Icons.ExtraLargeSize);
        IconsCheckBoxes.SelectedIndex = (int)views.Icons.CheckBoxes;
        IconsThumbnails.Checked = views.Icons.Thumbnails;
        IconsFolderThumbnails.Checked = views.Icons.FolderThumbnails;
        NameLines.Value = Math.Clamp(views.Icons.NameLines, FileViewLimits.MinNameLines, FileViewLimits.MaxNameLines);
        LoadWidth(SmallIconWidth, SmallIconChars, views.Icons.SmallIconWidth);

        _tilesDragDrop.Checked = views.Tiles.InPanelDragDrop;
        // R-115: 並びは候補の順で固定。選ぶだけで並べ替えはしない
        foreach (var info in Enum.GetValues<TileInfo>())
            _tileInfo.Items.Add(new Choice<TileInfo>(info, InfoNames[info]), views.Tiles.Info.Contains(info));
        LoadSize(_tilesSize, views.Tiles.TilesSize);
        LoadSize(_contentSize, views.Tiles.ContentSize);
        TilesCheckBoxes.SelectedIndex = (int)views.Tiles.CheckBoxes;
        TilesThumbnails.Checked = views.Tiles.Thumbnails;
        TilesFolderThumbnails.Checked = views.Tiles.FolderThumbnails;

        _loading = false;
    }

    private void Wire()
    {
        OnCheck(Overlays, (v, on) => v with { Common = v.Common with { ShowOverlays = on } });
        OnCheck(_hideKnownExtensions, (v, on) => v with { Common = v.Common with { HideKnownExtensions = on } });
        OnCheck(_listAlignExtension, (v, on) => v with { List = v.List with { AlignExtension = on } });

        OnCheck(ListDragDrop, (v, on) => v with { List = v.List with { InPanelDragDrop = on } });
        OnWidth(_listWidth, _listChars, (v, w) => v with { List = v.List with { NameWidth = w } });

        OnCheck(_detailsDragDrop, (v, on) => v with { Details = v.Details with { InPanelDragDrop = on } });
        OnWidth(_detailsWidth, _detailsChars, (v, w) => v with { Details = v.Details with { NameWidth = w } });
        OnCheck(_detailsAlignExtension, (v, on) => v with { Details = v.Details with { AlignExtension = on } });
        // ItemCheck はチェックが変わる前に来るので、変わる項目だけ新しい値で読む
        DetailsColumns.ItemCheck += (_, e) => CommitColumns(e.Index, e.NewValue == CheckState.Checked);
        MoveColumnUp.Click += (_, _) => MoveColumn(-1);
        _moveColumnDown.Click += (_, _) => MoveColumn(1);
        OnCheck(_fitColumns, (v, on) => v with { Details = v.Details with { FitColumnsToWindow = on } });

        OnCheck(_iconsDragDrop, (v, on) => v with { Icons = v.Icons with { InPanelDragDrop = on } });
        OnSize(_mediumSize, (v, size) => v with { Icons = v.Icons with { MediumSize = size } });
        OnSize(_largeSize, (v, size) => v with { Icons = v.Icons with { LargeSize = size } });
        OnSize(_extraLargeSize, (v, size) => v with { Icons = v.Icons with { ExtraLargeSize = size } });
        IconsCheckBoxes.SelectedIndexChanged += (_, _) =>
            Change(v => v with { Icons = v.Icons with { CheckBoxes = (CheckBoxMode)IconsCheckBoxes.SelectedIndex } });
        OnCheck(IconsThumbnails, (v, on) => v with { Icons = v.Icons with { Thumbnails = on } });
        OnCheck(IconsFolderThumbnails, (v, on) => v with { Icons = v.Icons with { FolderThumbnails = on } });
        NameLines.ValueChanged += (_, _) => Change(v => v with { Icons = v.Icons with { NameLines = (int)NameLines.Value } });
        OnWidth(SmallIconWidth, SmallIconChars, (v, w) => v with { Icons = v.Icons with { SmallIconWidth = w } });

        OnCheck(_tilesDragDrop, (v, on) => v with { Tiles = v.Tiles with { InPanelDragDrop = on } });
        _tileInfo.ItemCheck += (_, e) =>
        {
            var info = Enumerable.Range(0, _tileInfo.Items.Count)
                .Where(i => i == e.Index ? e.NewValue == CheckState.Checked : _tileInfo.GetItemChecked(i))
                .Select(i => ((Choice<TileInfo>)_tileInfo.Items[i]).Value)
                .ToArray();
            Change(v => v with { Tiles = v.Tiles with { Info = info } });
        };
        OnSize(_tilesSize, (v, size) => v with { Tiles = v.Tiles with { TilesSize = size } });
        OnSize(_contentSize, (v, size) => v with { Tiles = v.Tiles with { ContentSize = size } });
        TilesCheckBoxes.SelectedIndexChanged += (_, _) =>
            Change(v => v with { Tiles = v.Tiles with { CheckBoxes = (CheckBoxMode)TilesCheckBoxes.SelectedIndex } });
        OnCheck(TilesThumbnails, (v, on) => v with { Tiles = v.Tiles with { Thumbnails = on } });
        OnCheck(TilesFolderThumbnails, (v, on) => v with { Tiles = v.Tiles with { FolderThumbnails = on } });
    }

    private void Change(Func<FileViewSettings, FileViewSettings> change)
    {
        if (_loading) return;
        _draft.FileViews = change(_draft.FileViews);
    }

    private void OnCheck(CheckBox box, Func<FileViewSettings, bool, FileViewSettings> change) =>
        box.CheckedChanged += (_, _) => Change(v => change(v, box.Checked));

    private void OnSize(ComboBox combo, Func<FileViewSettings, int, FileViewSettings> change) =>
        combo.SelectedIndexChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0) Change(v => change(v, FileViewLimits.IconSizes[combo.SelectedIndex]));
        };

    private void OnWidth(ComboBox mode, NumericUpDown chars, Func<FileViewSettings, NameWidthSetting, FileViewSettings> change)
    {
        void Changed()
        {
            chars.Enabled = mode.SelectedIndex == (int)NameWidthMode.MaxChars;
            Change(v => change(v, new NameWidthSetting { Mode = (NameWidthMode)mode.SelectedIndex, MaxChars = (int)chars.Value }));
        }
        mode.SelectedIndexChanged += (_, _) => Changed();
        chars.ValueChanged += (_, _) => Changed();
    }

    private static void LoadWidth(ComboBox mode, NumericUpDown chars, NameWidthSetting width)
    {
        mode.SelectedIndex = (int)width.Mode;
        chars.Value = Math.Clamp(width.MaxChars, FileViewLimits.MinChars, FileViewLimits.MaxChars);
        // R-113: 文字数は方式が「最大文字数」のときだけ効く
        chars.Enabled = width.Mode == NameWidthMode.MaxChars;
    }

    // 候補にない値は設定の読み込みで既定値に直っている（FileViewSettings.Normalize）。直っていなければ未選択のまま
    private static void LoadSize(ComboBox combo, int size) => combo.SelectedIndex = FileViewLimits.IconSizes.ToList().IndexOf(size);

    private void CommitColumns(int changedIndex = -1, bool changedVisible = false)
    {
        if (_loading) return;
        var columns = Enumerable.Range(0, DetailsColumns.Items.Count)
            .Select(i => new DetailsColumnSetting
            {
                Column = ((Choice<DetailsColumn>)DetailsColumns.Items[i]).Value,
                Visible = i == changedIndex ? changedVisible : DetailsColumns.GetItemChecked(i),
            })
            .ToArray();
        Change(v => v with { Details = v.Details with { Columns = columns } });
    }

    private void MoveColumn(int delta)
    {
        var from = DetailsColumns.SelectedIndex;
        var to = from + delta;
        if (from < 0 || to < 0 || to >= DetailsColumns.Items.Count) return;

        // 入れ替えの途中の ItemCheck で半端な並びを書き込まないよう、書き戻しは最後に 1 回
        _loading = true;
        var item = DetailsColumns.Items[from];
        var visible = DetailsColumns.GetItemChecked(from);
        DetailsColumns.Items.RemoveAt(from);
        DetailsColumns.Items.Insert(to, item);
        DetailsColumns.SetItemChecked(to, visible);
        DetailsColumns.SelectedIndex = to;
        _loading = false;
        CommitColumns();
    }

    private static CheckBox Check(string text) => new() { Text = text, AutoSize = true };

    private static Label Caption(string text) => new() { Text = text, AutoSize = true };

    private static ComboBox Combo(string[] items, int width)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width };
        combo.Items.AddRange(items);
        return combo;
    }

    private static ComboBox SizeCombo() => Combo([.. FileViewLimits.IconSizes.Select(size => $"{size} px")], 130);

    private static NumericUpDown Chars() => new() { Width = 80, Minimum = FileViewLimits.MinChars, Maximum = FileViewLimits.MaxChars };

    /// <summary>左にラベル、右の列（<see cref="InputLeft"/>）に入力欄を置く 1 行。ラベルを先に足す。</summary>
    private static Control[] Row(string label, Control input)
    {
        input.Left = InputLeft;
        return [Caption(label), input];
    }

    /// <summary>部品を上から順に置き、足した順をタブ順にする。行（<see cref="Row"/>）のラベルは入力欄より 4px 下げて文字の高さをそろえる。</summary>
    private static Panel Arrange(Panel panel, params (object Item, int Top)[] rows)
    {
        foreach (var (item, top) in rows)
        {
            if (item is Control[] row)
            {
                row[0].Location = new Point(0, top + 4);
                row[1].Top = top;
                panel.Controls.AddRange(row);
            }
            else
            {
                var control = (Control)item;
                control.Location = new Point(control.Left, top);
                panel.Controls.Add(control);
            }
        }
        return panel;
    }
}
