using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ReTAC.App.Rendering;
using ReTAC.Domain.Entries;
using ReTAC.Domain.FileOps;
using ReTAC.Domain.Listing;
using ReTAC.Domain.Selection;
using ReTAC.Shell;
using SortOrder = ReTAC.Domain.Listing.SortOrder;

namespace ReTAC.App;

/// <summary>
/// 自前描画の多段組ファイルリスト（R-01）。
/// エントリは縦に流れ、高さを超えると右隣の列へ折り返す。スクロールは横方向のみ（R-01-2）。
/// 標準 ListView では再現できない段組みと選択状態を扱うため、自前描画する。
/// </summary>
public sealed class FileListView : Control
{
    // 子は親の Cursor を引き継ぐ。見出しの境界で VSplit になったまま接するスクロールバーへ移ると、そこでも VSplit のままになるので矢印に固定する
    private readonly HScrollBar _hScrollBar = new() { Dock = DockStyle.Bottom, Visible = false, Cursor = Cursors.Default };
    private readonly VScrollBar _vScrollBar = new() { Dock = DockStyle.Right, Visible = false, Cursor = Cursors.Default };

    private Theme _theme = Theme.Default;
    private Font _font = null!;
    private TextMeasure _measure = null!;
    private ShellIcons _icons = null!;
    private ListState _state = new([]);
    private IFileViewLayout _layout = ColumnLayout.Empty;
    private FileViewMode _mode = FileViewMode.List;
    private FileViewSettings _views = new();
    private IReadOnlyDictionary<string, int?> _columnWidths = new Dictionary<string, int?>();
    private SortOrder _sortOrder = SortOrder.Default;
    private readonly ToolTip _nameTip = new();
    private int _tipIndex = -1;
    /// <summary>押した所の種類。名前以外を押したままのドラッグは D&amp;D を始めない（INV-DETAILS-ROW-HIT）。</summary>
    private FileViewArea _pressArea;
    /// <summary>R-110-3: スクロール位置（縦横の段の数）。一覧は横だけで、1 段は 1 列</summary>
    private ScrollPosition _scroll;
    /// <summary>R-76: ホイールの端数。フォルダを開き直したら捨てる。</summary>
    private readonly WheelAccumulator _wheel = new();
    /// <summary>R-11-2 / Q7: 行頭アイコン・Shift+クリックのマークは、動かさずに離した時点で変える。</summary>
    private readonly MarkOnRelease _markOnRelease = new();
    /// <summary>
    /// R-111-1: 右ボタンを押した項目・位置・Shift。右クリックのメニューは離した時点で出す（押した時点で出すと右ボタンのドラッグを始められない）。
    /// Shift は押した時点の値（R-81）。ドラッグになったら null に戻し、メニューは出さない。
    /// </summary>
    private (int Index, Point Location, bool Shift)? _rightDown;
    /// <summary>右ボタンを見出しの上で押した。離した所が見出しでも、別の所で押していたら列のメニューは出さない。</summary>
    private bool _headerRightDown;
    /// <summary>R-110-2: 落とす先として枠で囲む項目。-1 なら囲まない。</summary>
    private int _dropTarget = -1;
    /// <summary>R-110-3 / T5: 端で止めている間、この間隔で 1 列ずつスクロールする（実機で 0.3〜0.5 秒を比べて決めた）。</summary>
    private const int AutoScrollInterval = 400;
    private readonly System.Windows.Forms.Timer _autoScroll = new() { Interval = AutoScrollInterval };
    private (int X, int Y) _autoScrollDirection;
    /// <summary>R-114 / R-76: Ctrl+ホイールの端数（表示モードの段）。スクロールの端数とは別に持つ。</summary>
    private readonly WheelAccumulator _modeWheel = new();
    /// <summary>R-114: 見出しの境界をドラッグしている間の幅（物理ピクセル）。離すまで保存しない（V6）。</summary>
    private readonly Dictionary<string, int> _dragWidths = [];
    /// <summary>見出しの境界をつかんだ状態（境界の左のセルの添字・押した x・そのときの幅）。</summary>
    private (int Cell, int StartX, int StartWidth)? _headerDrag;
    /// <summary>見出しのセルを押した状態（添字）。離した所が同じセルならソートする。列の並べ替えはしない（INV-NO-COLUMN-REORDER-BY-DRAG）。</summary>
    private int _headerPress = -1;
    /// <summary>Q36: マウスが乗っている見出しのセル。境界の上・見出しの外では -1。</summary>
    private int _headerHot = -1;

    public FileListView()
    {
        AllowDrop = true;   // R-65 ②: エクスプローラー等からのドロップを受ける
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
        TabStop = true;
        // B-04: ファイルリストは 1 打鍵がコマンドである。IME がオンだと文字が食われて
        // 何も動かない。入力欄（TextInputDialog 等）は別ウィンドウなので影響しない
        ImeMode = ImeMode.Disable;
        Controls.Add(_hScrollBar);
        Controls.Add(_vScrollBar);
        _hScrollBar.Scroll += (_, e) => { _scroll = _scroll with { X = e.NewValue }; Invalidate(); };
        _vScrollBar.Scroll += (_, e) => { _scroll = _scroll with { Y = e.NewValue }; Invalidate(); };
        _autoScroll.Tick += (_, _) => AutoScrollStep();
        ShellFileType.Resolved += OnTypeResolved;
        RebuildFontResources();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // B-04: ImeMode だけでは効かない。WinForms が ImeMode を実際に適用するのは
        // CanEnableIme を override した TextBoxBase / ComboBox などに限られ、
        // Control 直系の自前描画コントロールでは値を持つだけで IME コンテキストに触れない。
        // ウィンドウから IME を切り離すのは自分でやる。ハンドル再生成のたびに掛け直す
        ImmAssociateContext(Handle, IntPtr.Zero);
    }

    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    [System.Runtime.InteropServices.DllImport("imm32.dll")]
    private static extern IntPtr ImmAssociateContext(IntPtr window, IntPtr context);

    /// <summary>カーソル位置が変わった（ステータスバーの更新契機）。</summary>
    public event EventHandler? CursorMoved;

    /// <summary>マーク集合が変わった。</summary>
    public event EventHandler? MarksChanged;

    /// <summary>Enter が押された。フォルダなら下へ、ファイルなら関連付け実行（呼び出し側が判断する）。</summary>
    public event EventHandler<Entry>? EntryActivated;

    /// <summary>BackSpace が押された。R-39 の判定は呼び出し側が行う。</summary>
    public event EventHandler? ParentRequested;

    /// <summary>固定キー（5-3 節）で処理しなかったキー。キーマップによる解決は呼び出し側が行う（R-12）。</summary>
    public event EventHandler<KeyEventArgs>? CommandKey;

    /// <summary>右クリックされた。シェルのメニューか `G` のメニューかは呼び出し側が決める。</summary>
    public event EventHandler<RightClick>? RightClicked;

    /// <summary>R-114: 見出しがクリックされた。ソートを変える列（何もしない列でも出す。判定は MainForm が HeaderSort.Click で行う）。</summary>
    public event EventHandler<DetailsColumn?>? HeaderClicked;

    /// <summary>R-114 / V6: 列の幅が変わった。論理列 ID と 96 dpi の論理ピクセル。null は自動に戻す。</summary>
    public event EventHandler<(string Key, int? Width)>? ColumnWidthChanged;

    /// <summary>R-114: すべての列を自動に戻す。</summary>
    public event EventHandler? ColumnWidthsReset;

    /// <summary>R-114: 列の表示・非表示が変わった。</summary>
    public event EventHandler<(DetailsColumn Column, bool Visible)>? ColumnVisibilityChanged;

    /// <summary>V9: Ctrl+ホイールの段の数（正で上の段）。</summary>
    public event EventHandler<int>? ViewModeWheel;

    /// <summary>Step5: Tab / Shift+Tab。左パネルが見えているときだけ、そちらへ往復する合図。</summary>
    public event EventHandler? FocusLeftPanelRequested;

    /// <param name="Index">押された行。行の外なら -1</param>
    /// <param name="Shift">
    /// R-81: 押した時点で Shift が押されていたか。MainForm が後から ModifierKeys を見ると、
    /// Shift を先に離したときに取り違える
    /// </param>
    public readonly record struct RightClick(int Index, Point ScreenPoint, bool Shift);

    /// <summary>R-78: 今いるフォルダ。ドロップの説明に使う。MainForm がフォルダを開くたびに設定する。</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string DropFolder { get; set; } = "";

    /// <summary>R-110: ファイル表示パネルの中の項目の上へ落とせるか。SetView が系統の設定から当てる。</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool InPanelDragDrop => _mode == FileViewMode.Details ? _views.Details.InPanelDragDrop : _views.List.InPanelDragDrop;

    /// <summary>ファイルが落とされた（T8-2）。修飾キーはドロップの時点の値（R-111-2）。</summary>
    public readonly record struct Drop(string[] Files, string Destination, DragDropEffects Allowed, bool Ctrl, bool Shift);

    /// <summary>他アプリからファイルが落とされた（T8-2）。転送は呼び出し側が行う。</summary>
    public event EventHandler<Drop>? FilesDropped;

    public ListState State => _state;

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Theme Theme
    {
        get => _theme;
        set { _theme = value; RebuildFontResources(); RecomputeLayout(); Invalidate(); }
    }

    /// <summary>N-06: キーボードから開くポップアップを出す位置（カーソル行の直下・クライアント座標）。</summary>
    public Point PopupAnchor()
    {
        var (x, y, _, h) = FileViewScroll.VisibleBounds(_layout, _scroll, _state.CursorIndex);
        return new Point(x + _icons.Size, y + h);
    }

    /// <summary>カーソルを移す。スクロールと再描画とイベント通知まで面倒を見る。</summary>
    public void MoveCursorTo(int index)
    {
        var before = _state.CursorIndex;
        _state.MoveCursor(index);
        Commit(before, marksChanged: false);
    }

    /// <param name="keepScroll">
    /// 同じフォルダの再表示。スクロール位置（縦横）を保つ。
    /// 自動更新のたびにカーソル列へ引き戻されると、右の方を見ている最中に読めなくなる（R-10）
    /// </param>
    public void SetEntries(IReadOnlyList<Entry> entries, int cursorIndex = 0, bool keepScroll = false)
    {
        // 自動更新などでボタンを押したまま一覧が入れ替わることがある。押した時点の添字は
        // 別の項目を指すことになるので、離した時点の処理（マーク・右ボタンのドラッグ／メニュー）は捨てる
        ResetNameTip();
        _rightDown = null;
        _markOnRelease.Cancel();
        _dragIndex = -1;
        var scroll = _scroll;
        _state = new ListState(entries);
        _state.MoveCursor(cursorIndex);
        _scroll = keepScroll ? scroll : default;
        if (!keepScroll) _wheel.Reset();
        RecomputeLayout();
        // 見えている位置ならこの中で何も起きない。カーソルが画面外のときだけ動く
        EnsureCursorVisible();
        Invalidate();
        CursorMoved?.Invoke(this, EventArgs.Empty);
        MarksChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- レイアウト -------------------------------------------------------

    private int Scaled(int logical) => logical * DeviceDpi / 96;

    private int Gap => Scaled(4);
    private int RowPadding => Scaled(2);
    /// <summary>B-07: 列の先頭と末尾の余白。卓駆と同程度。gap と同値にしてある。
    /// 見た目を詰めたい／広げたいときはここ 1 箇所を変える。</summary>
    private int ColumnPaddingValue => Scaled(4);

    /// <summary>項目を描ける領域（見出しとスクロールバーを除く）。</summary>
    private int ViewportWidth => Math.Max(0, ClientSize.Width - (_layout.ScrollBars.Vertical ? _vScrollBar.Width : 0));
    private int ViewportHeight => Math.Max(0, ClientSize.Height - _layout.HeaderHeight - (_layout.ScrollBars.Horizontal ? _hScrollBar.Height : 0));

    private void RebuildFontResources()
    {
        _font?.Dispose();
        _measure?.Dispose();
        _icons?.Dispose();
        _font = new Font(_theme.FontFamily, _theme.FontSize);
        _measure = new TextMeasure(_font, DeviceDpi);
        _icons = new ShellIcons(Scaled(16));
    }

    /// <summary>
    /// R-112-4: 表示モードと系統ごとの設定を当てる。項目・カーソル・マークは保ち（フォルダを開き直さない。R-40-2）、
    /// スクロールは新しいレイアウトでカーソルが見える位置に直す（前のモードの段の数は持ち越さない）。
    /// </summary>
    public void SetView(FileViewMode mode, FileViewSettings views, IReadOnlyDictionary<string, int?> columnWidths, SortOrder sortOrder)
    {
        ResetNameTip();
        var modeChanged = mode != _mode;
        (_mode, _views, _columnWidths, _sortOrder) = (mode, views, columnWidths, sortOrder);
        if (modeChanged) { _scroll = default; _wheel.Reset(); }
        RecomputeLayout();
        EnsureCursorVisible();
        Invalidate();
    }

    private void RecomputeLayout()
    {
        if (_mode == FileViewMode.Details) { _layout = ComputeDetails(); UpdateScrollBars(); return; }
        var textStart = ColumnPaddingValue + _icons.Size + Gap;
        // R-113: 「自動」はパネルの幅（名前の文字の外側を引いたもの）、「最大文字数」は数字 0 の幅 × 文字数（Q9）
        var cap = NameWidths.TextCap(_views.List.NameWidth, _measure.Width("0"), ClientSize.Width - textStart - ColumnPaddingValue);
        _layout = EntryMetrics.Layout(_state.Entries, _measure, ClientSize.Width, ClientSize.Height, _hScrollBar.Height,
            _icons.Size, Gap, RowPadding, ColumnPaddingValue, cap, AlignExtension, HidesExtension);
        UpdateScrollBars();
    }

    /// <summary>列の最小幅。見出しの文字と左右の余白（ドラッグでもこれより狭くしない）。ソートの印は文字の上に描くので幅に入れない（Q36）。</summary>
    private int MinColumnWidth(DetailsColumn? column) =>
        _measure.Width(DetailsCells.Header(column)) + ColumnPaddingValue * 2;

    /// <summary>R-114 / R-113 / V6: 列ごとに中身の最長と見出しの最小幅を測り、手動の幅（96 dpi の論理値）を dpi で拡大して渡す。</summary>
    private DetailsLayout ComputeDetails()
    {
        var details = _views.Details;
        var pad = ColumnPaddingValue;
        var showExtension = AlignExtension;
        var maxBase = 0;
        var maxExt = 0;
        foreach (var entry in _state.Entries)
        {
            maxBase = Math.Max(maxBase, _measure.Width(NameText(entry)));
            if (showExtension && !HidesExtension(entry)) maxExt = Math.Max(maxExt, _measure.Width(entry.Extension));
        }
        var nameTextStart = pad + _icons.Size + Gap;
        var naturalText = maxBase + (maxExt > 0 ? Gap + maxExt : 0);
        // R-113: 詳細表示の「自動」は名前の列だけでパネルの幅を超えない（Q14）
        var cap = NameWidths.TextCap(details.NameWidth, _measure.Width("0"), ClientSize.Width - nameTextStart - pad);
        var text = cap is { } c && naturalText > c ? c : naturalText;
        var baseWidth = text == naturalText ? maxBase : Math.Max(0, text - Gap - maxExt);

        var columns = new List<DetailsColumnInput> { Column(null, nameTextStart + text + pad) };
        foreach (var setting in details.Columns.Where(c => c.Visible))
        {
            var auto = _state.Entries.Count == 0 ? 0 : _state.Entries.Max(e => _measure.Width(DetailsCells.Text(e, setting.Column)));
            columns.Add(Column(setting.Column, auto + pad * 2));
        }

        return DetailsLayout.Compute(new DetailsLayoutInput
        {
            EntryCount = _state.Count,
            RowHeight = _measure.LineHeight() + RowPadding,
            IconWidth = _icons.Size,
            ColumnPadding = pad,
            HeaderHeight = _measure.LineHeight() + RowPadding + HeaderMarkHeight,   // 行の高さ + ソートの印を置く上の余白（Q36）
            StepWidth = _measure.Width("0") * 4,                 // Q28: 数字 0 の幅の 4 文字分
            Columns = columns,
            FitToWindow = details.FitColumnsToWindow,
            ClientWidth = ClientSize.Width,
            ClientHeight = ClientSize.Height,
            VerticalBarWidth = SystemInformation.VerticalScrollBarWidth,
            HorizontalBarHeight = SystemInformation.HorizontalScrollBarHeight,
            ExtensionOffset = showExtension && maxExt > 0 ? nameTextStart + baseWidth + Gap : 0,
        });

        DetailsColumnInput Column(DetailsColumn? column, int auto)
        {
            var key = DetailsColumnWidths.Key(column);
            var saved = _dragWidths.TryGetValue(key, out var dragging) ? dragging
                : _columnWidths.TryGetValue(key, out var logical) && logical is { } value
                    ? DetailsColumnWidths.ToPixels(value, DeviceDpi) : (int?)null;
            return new DetailsColumnInput(column, auto, MinColumnWidth(column), saved);
        }
    }

    /// <summary>スクロールバーの要・不要はレイアウトが決める（ScrollBars）。単位は段（一覧は列、詳細の縦は行・横は StepWidth）。</summary>
    private void UpdateScrollBars()
    {
        var (horizontal, vertical) = _layout.ScrollBars;
        _hScrollBar.Visible = horizontal;
        _vScrollBar.Visible = vertical;
        var max = _layout.MaxScrollPosition(_state.Count, ViewportWidth, ViewportHeight);
        var page = _layout.VisibleSteps(ViewportWidth, ViewportHeight);
        _scroll = new ScrollPosition(horizontal ? Math.Clamp(_scroll.X, 0, max.X) : 0, vertical ? Math.Clamp(_scroll.Y, 0, max.Y) : 0);
        // Control.Visible は親がまだ表示されていないと false を返すので、要否はレイアウトの答えで決める
        Configure(_hScrollBar, horizontal, max.X, page.X, _scroll.X);
        Configure(_vScrollBar, vertical, max.Y, page.Y, _scroll.Y);

        static void Configure(ScrollBar bar, bool needed, int max, int page, int value)
        {
            if (!needed) return;
            bar.Minimum = 0;
            bar.LargeChange = Math.Max(1, page);
            bar.SmallChange = 1;
            bar.Maximum = max + bar.LargeChange - 1;   // WinForms は Maximum - LargeChange + 1 までしか動かない
            bar.Value = Math.Clamp(value, 0, max);
        }
    }

    /// <summary>R-114: 背景で届いた種類（作業スレッドから呼ばれる）。破棄済み・ハンドルが無いなら何もしない。今の一覧にその鍵が無ければ描き直さない。</summary>
    private void OnTypeResolved(string key)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                if (IsDisposed || _mode != FileViewMode.Details) return;
                if (!_state.Entries.Any(e => ShellFileType.KeyOf(e.FullPath, e.Kind == EntryKind.Folder) == key)) return;
                RecomputeLayout();   // 種類の列の自動の幅が変わりうる
                Invalidate();
            });
        }
        catch (InvalidOperationException) { }   // 閉じる途中でハンドルが消えた
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RecomputeLayout();
        EnsureCursorVisible();
    }

    private void EnsureCursorVisible()
    {
        if (_state.Count == 0) return;
        _scroll = _layout.Reveal(_state.CursorIndex, _scroll, ViewportWidth, ViewportHeight);
        UpdateScrollBars();
    }

    // ---- 描画 -------------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(_theme.Background);
        if (_layout.HeaderHeight > 0) DrawHeader(e.Graphics);
        if (_state.Count == 0) return;
        // 詳細表示は項目を見出しの下に切り取る（一覧は見出しが無く、これまでどおり切り取らない）
        if (_layout.HeaderHeight > 0) e.Graphics.SetClip(new Rectangle(0, _layout.HeaderHeight, ViewportWidth, ViewportHeight));
        var (ox, oy) = _layout.ScrollOffset(_scroll);
        // R-01-4: 右端で切れる列も描く（切れるのはウィンドウの右端）。IndexesIn は交わる項目を返すのでそのまま入る。
        // カーソルの項目は最後に描く（全部描くときに右隣へ重ねるため）
        var visible = _layout.IndexesIn(ox, oy, ViewportWidth, ViewportHeight, _state.Count);
        foreach (var index in visible.Where(i => i != _state.CursorIndex)) DrawRow(e.Graphics, index);
        if (visible.Contains(_state.CursorIndex)) DrawRow(e.Graphics, _state.CursorIndex);
        e.Graphics.ResetClip();
    }

    /// <summary>
    /// R-114: 見出しは縦にスクロールせず、横だけ中身と一緒に動く。ソート中の列の上端に山形（HeaderSort.ShowsArrow。Q36）。
    /// 色は OS を直接読まずテーマから作る（INV-THEME-STARTUP-OS-STATE）。
    /// </summary>
    private void DrawHeader(Graphics g)
    {
        var ox = _layout.ScrollOffset(_scroll).X;
        var height = _layout.HeaderHeight;
        var (back, fore, line, hot, pressed) = RowColors.Header(_theme);
        using (var brush = new SolidBrush(back)) g.FillRectangle(brush, 0, 0, ClientSize.Width, height);
        using var linePen = new Pen(line);
        using var markPen = new Pen(fore, Math.Max(1, Scaled(1)));
        for (var i = 0; i < _layout.Header.Count; i++)
        {
            var cell = _layout.Header[i];
            var left = cell.X - ox;
            // Q36: 乗せた・押した色。幅を変えているあいだは塗らない
            if (i == _headerHot && _headerDrag is null)
                using (var brush = new SolidBrush(_headerPress == i ? pressed : hot))
                    g.FillRectangle(brush, left, 0, cell.Width - 1, height - 1);
            // 文字は印の下の残りで縦中央
            var rect = new Rectangle(left + ColumnPaddingValue, HeaderMarkHeight, Math.Max(0, cell.Width - ColumnPaddingValue * 2), height - HeaderMarkHeight);
            DrawCell(g, DetailsCells.Header(cell.Column), rect, fore, DetailsCells.RightAligned(cell.Column));
            if (HeaderSort.ShowsArrow(_sortOrder, cell.Column))
            {
                // 山形（昇順は上向き、降順は下向き）を見出しのセルの上端の中央に
                var (w, h) = (Scaled(8), Scaled(4));
                var (cx, top) = (left + cell.Width / 2, Scaled(2));
                var up = _sortOrder.Direction == SortDirection.Ascending;
                var (tip, foot) = up ? (top, top + h) : (top + h, top);
                var old = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawLines(markPen, [new Point(cx - w / 2, foot), new Point(cx, tip), new Point(cx + w / 2, foot)]);
                g.SmoothingMode = old;
            }
            g.DrawLine(linePen, left + cell.Width - 1, 0, left + cell.Width - 1, height - 1);
        }
        g.DrawLine(linePen, 0, height - 1, ClientSize.Width, height - 1);   // 見出しと項目を分ける
    }

    /// <summary>Q36: 見出しの上端に取る、ソートの印を置く余白。</summary>
    private int HeaderMarkHeight => Scaled(4);

    /// <summary>Q36: 乗せている見出しのセル（無ければ -1）を変え、変わったときだけ見出しの行を描き直す。</summary>
    private void SetHeaderHot(int cell)
    {
        if (cell == _headerHot) return;
        _headerHot = cell;
        InvalidateHeader();
    }

    private void InvalidateHeader() => Invalidate(new Rectangle(0, 0, ClientSize.Width, _layout.HeaderHeight));

    /// <summary>省略は収まらないときだけ EndEllipsis を付ける（収まる文字に「…」を出さない。R-113）。</summary>
    private void DrawCell(Graphics g, string text, Rectangle rect, Color color, bool right)
    {
        if (text.Length == 0 || rect.Width <= 0) return;
        var flags = TextMeasure.Flags | TextFormatFlags.VerticalCenter | (right ? TextFormatFlags.Right : TextFormatFlags.Left);
        if (_measure.Width(text) > rect.Width) flags |= TextFormatFlags.EndEllipsis;
        TextRenderer.DrawText(g, text, _font, rect, color, flags);
    }

    private void DrawRow(Graphics g, int index)
    {
        var entry = _state.Entries[index];
        var isCursor = index == _state.CursorIndex;
        var isMarked = _state.Marks.Contains(index);
        var rect = ToRectangle(FileViewScroll.VisibleBounds(_layout, _scroll, index));

        var (background, foreground) = RowColors.Of(_theme, AttributeColorRule.Classify(entry.Attributes), isCursor, isMarked);
        // Q12 / R-113: 一覧のカーソルの項目が省略されていたら、帯を名前の終わりまで右へ広げて右隣の上に重ねる
        var full = DrawsFullName(index);
        var band = full ? rect with { Width = Math.Max(rect.Width, FullNameWidth(entry)) } : rect;
        using (var brush = new SolidBrush(background)) g.FillRectangle(brush, band);

        var iconRect = ToRectangle(FileViewScroll.ToVisible(_layout, _scroll, _layout.IconBounds(index)));
        var icon = entry.Kind == EntryKind.File ? _icons.ForFile(entry.FullPath) : _icons.ForFolder();
        if (icon is not null) g.DrawImage(icon, iconRect with { Y = rect.Y + (rect.Height - _icons.Size) / 2, Width = _icons.Size, Height = _icons.Size });
        if (isMarked) MarkStar.Draw(g, iconRect, _theme.MarkStarColor);   // R-11-5

        DrawName(g, index, entry, foreground, full, band);

        if (_mode == FileViewMode.Details)   // R-114: 名前以外のセル
        {
            var ox = _layout.ScrollOffset(_scroll).X;
            foreach (var cell in _layout.Header.Skip(1))
            {
                var cellRect = new Rectangle(cell.X - ox + ColumnPaddingValue, rect.Y, Math.Max(0, cell.Width - ColumnPaddingValue * 2), rect.Height);
                DrawCell(g, DetailsCells.Text(entry, cell.Column!.Value), cellRect, foreground, DetailsCells.RightAligned(cell.Column));
            }
        }

        if (index == _dropTarget)   // R-110-2
        {
            using var pen = new Pen(RowColors.Frame(background, foreground), Scaled(2)) { Alignment = PenAlignment.Inset };
            g.DrawRectangle(pen, rect);
        }
    }

    /// <summary>
    /// R-01-4 / R-01-6 / R-113: 本体は名前の領域の左から、拡張子は揃えた位置から。収まらなければ本体の末尾を「…」で省略し、
    /// 拡張子は残す（拡張子そのものが入らなければ拡張子も「…」）。全部描く（full）ときは揃えから外して続けて描く（R-01-6 の例外）。
    /// ponytail: 省略は GDI の EndEllipsis 任せ。結合文字で崩れる例が見つかったら、書記素に切って自前で測る形に変える。
    /// </summary>
    private void DrawName(Graphics g, int index, Entry entry, Color foreground, bool full, Rectangle band)
    {
        var name = ToRectangle(FileViewScroll.ToVisible(_layout, _scroll, _layout.NameBounds(index)));
        var ext = ToRectangle(FileViewScroll.ToVisible(_layout, _scroll, _layout.ExtensionBounds(index)));
        var top = name.Y + (name.Height - _measure.LineHeight()) / 2;
        if (full)
        {
            var all = new Rectangle(name.X + Gap, top, band.Right - name.X - Gap, _measure.LineHeight());
            TextRenderer.DrawText(g, FullNameText(entry), _font, all, foreground, TextMeasure.Flags);
            return;
        }

        var showExtension = ext.Width > 0 && entry.Extension.Length > 0 && !HidesExtension(entry);
        var baseRight = showExtension ? ext.X - Gap : name.Right;
        var baseRect = new Rectangle(name.X + Gap, top, Math.Max(0, baseRight - name.X - Gap), _measure.LineHeight());
        // R-01-4: すべて表示では「…」を出さない。実際に収まらないときだけ EndEllipsis を付ける（1px の測り違いで出さない）
        var truncated = IsTruncated(index);
        var ellipsis = truncated ? TextFormatFlags.EndEllipsis : 0;
        var baseText = showExtension ? entry.BaseName : NameText(entry);
        // R-01-6: 続けて描くときの省略は、拡張子を残して本体の末尾を「…」にする（拡張子が入らなければ全体の末尾）
        if (truncated && !showExtension && !AlignExtension && !HidesExtension(entry)) baseText = TogetherText(entry, baseRect.Width);
        TextRenderer.DrawText(g, baseText, _font, baseRect, foreground, TextMeasure.Flags | ellipsis);
        if (showExtension)
            TextRenderer.DrawText(g, entry.Extension, _font, ext with { Y = top, Height = _measure.LineHeight() }, foreground,
                TextMeasure.Flags | ellipsis);
    }

    /// <summary>R-01-6 / Q35: いまのモードで拡張子を揃えて描くか（ビューごとの設定）。</summary>
    private bool AlignExtension => _mode == FileViewMode.Details ? _views.Details.AlignExtension : _views.List.AlignExtension;

    /// <summary>
    /// R-01-7 / Q20: この項目の拡張子を隠すか（項目ごと）。設定がオンで、ファイルで、拡張子があり、OS に登録されているとき。
    /// フォルダの「.」は拡張子として扱わない。
    /// </summary>
    internal bool HidesExtension(Entry entry) =>
        _views.Common.HideKnownExtensions && entry.Kind == EntryKind.File && RegisteredExtensions.IsRegistered(entry.Extension);

    /// <summary>R-01-7: カーソルの項目を全部描くときの文字。揃えから外して続けて描くが、隠す項目は本体だけ。</summary>
    internal string FullNameText(Entry entry) => HidesExtension(entry) ? entry.BaseName : entry.Name;

    /// <summary>揃えた拡張子の領域を使わないときに、名前の領域へ描く文字。</summary>
    internal string NameText(Entry entry) => !HidesExtension(entry) && !AlignExtension ? entry.Name : entry.BaseName;

    /// <summary>
    /// Q12 / Q20: 名前のツールチップを出すか。拡張子を隠す設定のあいだは、ファイルなら省略していなくても出す
    /// （隠していない項目も含む。何のファイルかを拡張子で確かめられるように）。それ以外は省略しているときだけ。
    /// </summary>
    internal bool ShowsNameTip(int index) =>
        index >= 0 && index < _state.Count
        && ((_views.Common.HideKnownExtensions && _state.Entries[index].Kind == EntryKind.File) || IsTruncated(index));

    /// <summary>
    /// 続けて描く名前が width に入らないときの文字。「本体…」に拡張子を続ける。
    /// 拡張子と「…」だけで入らないときは本体を出せないので、全体を返して描画側の EndEllipsis に任せる。
    /// </summary>
    internal string TogetherText(Entry entry, int width)
    {
        var tail = "…" + entry.Extension;
        if (entry.Extension.Length == 0 || _measure.Width(tail) >= width) return entry.Name;
        var length = entry.BaseName.Length;
        while (length > 0 && _measure.Width(entry.BaseName[..length] + tail) > width) length--;
        return entry.BaseName[..length] + tail;
    }

    /// <summary>R-113: 省略して描いているか。本体の実測が本体の領域より広いか、拡張子の実測が拡張子の領域より広いとき。</summary>
    internal bool IsTruncated(int index)
    {
        if (index < 0 || index >= _state.Count) return false;
        var entry = _state.Entries[index];
        var (nx, _, nw, _) = _layout.NameBounds(index);
        var (ex, _, ew, _) = _layout.ExtensionBounds(index);
        var showExtension = ew > 0 && entry.Extension.Length > 0 && !HidesExtension(entry);
        var baseWidth = (showExtension ? ex - Gap : nx + nw) - nx - Gap;
        return _measure.Width(showExtension ? entry.BaseName : NameText(entry)) > baseWidth
               || showExtension && _measure.Width(entry.Extension) > ew;
    }

    /// <summary>
    /// Q12 / Q27 / R-113: カーソルの項目を省略せず全部描くか。描くのは一覧と小〜特大アイコンだけ（Phase 15 では一覧）。
    /// 詳細表示では、名前を右へ重ねるとその行自身のサイズ・日時が隠れるので描かない。
    /// </summary>
    internal bool DrawsFullName(int index) => index == _state.CursorIndex && _mode == FileViewMode.List && IsTruncated(index);

    /// <summary>カーソルの項目を全部描くときの帯の幅（左の余白・アイコン・間・名前・右の余白）。</summary>
    private int FullNameWidth(Entry entry) => ColumnPaddingValue + _icons.Size + Gap + _measure.Width(FullNameText(entry)) + ColumnPaddingValue;

    private static Rectangle ToRectangle((int X, int Y, int Width, int Height) r) => new(r.X, r.Y, r.Width, r.Height);

    /// <summary>見えている座標の点の、項目と押した所の種類。見出しの上なら (-1, None)。</summary>
    private (int Index, FileViewArea Area) HitAt(Point point)
    {
        if (point.Y < _layout.HeaderHeight) return (-1, FileViewArea.None);
        var offset = _layout.ScrollOffset(_scroll);
        return _layout.HitTest(point.X + offset.X, point.Y - _layout.HeaderHeight + offset.Y, _state.Count);
    }

    // ---- 固定キー -----------------------------------------------------------

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) switch
    {
        Keys.Up or Keys.Down or Keys.Left or Keys.Right => true,
        Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End => true,
        Keys.Enter or Keys.Space or Keys.Back => true,
        _ => base.IsInputKey(keyData),
    };

    /// <summary>
    /// Step5: Tab / Shift+Tab は、上端の選択欄や上部バーへの既定のダイアログ移動をさせず、
    /// 表示中の左パネルとの往復だけに使う（左パネル非表示なら MainForm 側で何もしない）。
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is Keys.Tab or (Keys.Tab | Keys.Shift))
        {
            FocusLeftPanelRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // 空のフォルダでも移動系のコマンドは効く必要がある
        if (_state.Count == 0) { CommandKey?.Invoke(this, e); return; }

        // 同じ計算を 2 か所に持たない（片方だけ 0 除算の防御が無かった。V-14）
        var page = _layout.PageItems(ViewportWidth, ViewportHeight);
        var before = _state.CursorIndex;
        var marksChanged = false;

        switch (e.KeyCode)
        {
            case Keys.Up: _state.MoveCursor(_layout.Arrow(_state.CursorIndex, 0, -1, _state.Count)); break;
            case Keys.Down: _state.MoveCursor(_layout.Arrow(_state.CursorIndex, 0, 1, _state.Count)); break;
            // R-01-5 / Q28: 一覧は隣の列の同じ高さへ（隣が無ければ動かない）。詳細は横スクロールでカーソルは動かさない
            case Keys.Left or Keys.Right when _layout.ArrowsScrollHorizontally:
                ScrollBy(e.KeyCode == Keys.Left ? -1 : 1, 0);
                e.Handled = true;
                return;
            case Keys.Left: _state.MoveCursor(_layout.Arrow(_state.CursorIndex, -1, 0, _state.Count)); break;
            case Keys.Right: _state.MoveCursor(_layout.Arrow(_state.CursorIndex, 1, 0, _state.Count)); break;
            // ponytail: ページ = 1 画面分（列数 × 行数）。卓駆の実測と食い違うようなら 1 列分に変える
            case Keys.PageUp: _state.MoveCursorBy(-page); break;
            case Keys.PageDown: _state.MoveCursorBy(page); break;

            case Keys.Home when e.Shift:
                _state.ToggleAllMarks();          // 5-3 節: Shift+Home は全選択／全解除
                marksChanged = true;
                break;
            case Keys.Home: _state.MoveCursor(0); break;
            case Keys.End: _state.MoveCursor(_state.Count - 1); break;

            case Keys.Space:                      // R-11-3
                _state.ToggleMarkAndAdvance();
                marksChanged = true;
                break;

            // B-11: Enter / Shift+Enter はキーマップを通す（R-12）。ここで直接開くと
            // Shift+Enter（既定はテキストエディタ）まで関連付けで開かれ、割り当ての変更も効かなかった
            case Keys.Back when !e.Control:   // Ctrl+BackSpace は割り当ての枠（F-06）なのでキーマップへ流す
                ParentRequested?.Invoke(this, EventArgs.Empty);
                return;

            default:
                CommandKey?.Invoke(this, e);
                return;
        }

        e.Handled = true;
        Commit(before, marksChanged);
    }

    // ---- マウス（R-11: クリックはカーソル移動のみ） ------------------------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        if (e.Y < _layout.HeaderHeight)
        {
            var x = e.X + _layout.ScrollOffset(_scroll).X;
            var border = DetailsLayout.HeaderBorderAt(_layout.Header, x, Scaled(4));
            _rightDown = null;
            _headerRightDown = e.Button == MouseButtons.Right;
            if (e.Button != MouseButtons.Left) return;
            if (border >= 0) _headerDrag = (border, e.X, _layout.Header[border].Width);   // 幅のドラッグを始める
            else { _headerPress = DetailsLayout.HeaderCellAt(_layout.Header, x); InvalidateHeader(); }   // 離した時点でソート
            return;
        }

        var index = HitAt(e.Location).Index;
        _headerRightDown = false;

        if (e.Button == MouseButtons.Right)
        {
            _rightDown = (index, e.Location, ModifierKeys.HasFlag(Keys.Shift));
            return;
        }
        if (e.Button != MouseButtons.Left || index < 0) return;

        PressLeft(e.Location, ModifierKeys.HasFlag(Keys.Shift));
    }

    /// <summary>
    /// 左ボタンを押した処理（R-11-2）。ModifierKeys は押した瞬間の値をテストから再現できないので、
    /// shift を引数で渡して OnMouseDown から切り離す（テスト用の入口。挙動は変えない）。
    /// </summary>
    internal void PressLeft(Point location, bool shift)
    {
        var (index, area) = HitAt(location);
        if (index < 0) return;

        _pressArea = area;

        _dragOrigin = location;
        _dragIndex = index;

        // Q7: マークを変えるのは離した時点（MarkOnRelease）。押した時点ではカーソルだけ移す。
        // 行頭アイコンの当たり判定（左の余白を含む。B-07）はレイアウトが決める
        var before = _state.CursorIndex;
        _markOnRelease.Press(_state, index, area, shift);
        // R-11-2: Shift の押下ではカーソルが動かない（Press が動かさない）。ここで無条件に Commit すると、
        // EnsureCursorVisible が古いカーソル（横スクロールでは画面外かもしれない）の列へ表示を戻してしまい、
        // 押した項目の真下からマウスがずれる。動いていなければ何もしない
        if (_state.CursorIndex != before) Commit(before, marksChanged: false);
    }

    // ---- ドラッグ＆ドロップ（R-65） ---------------------------------------

    /// <summary>ドラッグの開始位置。左ボタンが押されたまま一定距離動いたらドラッグとみなす。</summary>
    private Point _dragOrigin;
    private int _dragIndex = -1;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        SetHeaderHot(_layout.HeaderHeight > 0 && e.Y >= 0 && e.Y < _layout.HeaderHeight
                     && DetailsLayout.HeaderBorderAt(_layout.Header, e.X + _layout.ScrollOffset(_scroll).X, Scaled(4)) < 0
            ? DetailsLayout.HeaderCellAt(_layout.Header, e.X + _layout.ScrollOffset(_scroll).X) : -1);

        if (_headerDrag is { } drag && e.Button == MouseButtons.Left)
        {
            var column = _layout.Header[drag.Cell].Column;
            var min = MinColumnWidth(column);
            var width = Math.Clamp(drag.StartWidth + e.X - drag.StartX, min, Math.Max(min, DetailsColumnWidths.ToPixels(DetailsColumnWidths.Max, DeviceDpi)));
            _dragWidths[DetailsColumnWidths.Key(column)] = width;
            RecomputeLayout();
            Invalidate();
            return;
        }
        if (e.Button == MouseButtons.None && _layout.HeaderHeight > 0)
        {
            var onBorder = e.Y < _layout.HeaderHeight
                           && DetailsLayout.HeaderBorderAt(_layout.Header, e.X + _layout.ScrollOffset(_scroll).X, Scaled(4)) >= 0;
            Cursor = onBorder ? Cursors.VSplit : Cursors.Default;
        }

        if (e.Button == MouseButtons.Right && _rightDown is { } down && down.Index >= 0 && !_state.Entries[down.Index].IsParent
            && (Math.Abs(e.X - down.Location.X) >= SystemInformation.DragSize.Width
                || Math.Abs(e.Y - down.Location.Y) >= SystemInformation.DragSize.Height))
        {
            _rightDown = null;
            // R-111: 右クリックと同じく押した項目へカーソルを移す。対象はマークがあればマーク集合、無ければその項目（R-10 / R-65-2）。
            // A-01: マークは変えない
            MoveCursorTo(down.Index);
            var rightTargets = _state.EffectiveTarget();
            if (rightTargets.Count == 0) return;
            using var rightImage = DragImage(rightTargets);
            ShellDrag.Start(this, rightTargets.Select(target => target.FullPath).ToList(), rightImage, new Point(Scaled(8), Scaled(8)), rightButton: true);
            return;
        }

        if (e.Button == MouseButtons.None) UpdateNameTip(e.Location);
        if (e.Button != MouseButtons.Left || _dragIndex < 0) return;

        var moved = Math.Abs(e.X - _dragOrigin.X) >= SystemInformation.DragSize.Width
                 || Math.Abs(e.Y - _dragOrigin.Y) >= SystemInformation.DragSize.Height;
        if (!moved) return;
        // INV-DETAILS-ROW-HIT: 名前以外を押したままのドラッグは D&D にしない（マークも変えない）
        // Phase 15 の間の形。D&D にしない
        if (_pressArea == FileViewArea.Other) { _markOnRelease.Moved(); _dragIndex = -1; _pressArea = FileViewArea.None; return; }

        // R-65-2 / R-11-2: 対象は実効対象の規則。マークがあればマーク集合、
        // なければ押した位置のエントリ。Shift+クリックはカーソルを押した時点で動かさないので、
        // ここでカーソルの項目（EffectiveTarget の既定）を見ると離す前の古いカーソル位置を拾ってしまう
        IReadOnlyList<Entry> targets = _state.Marks.Count > 0
            ? _state.EffectiveTarget()
            : _dragIndex >= 0 && !_state.Entries[_dragIndex].IsParent ? [_state.Entries[_dragIndex]] : [];
        _dragIndex = -1;
        if (targets.Count == 0) return;

        _markOnRelease.DragStarted();   // Q7: D&D になったらマークは変えない
        // A-01: ドラッグしてもマークは変わらない
        // R-78: 画像付きで始める。画像の無いドラッグには、落とす先が説明（「◯◯へ移動」）を出せない
        using var image = DragImage(targets);
        ShellDrag.Start(this, targets.Select(target => target.FullPath).ToList(), image, new Point(Scaled(8), Scaled(8)));
    }

    /// <summary>Q12: 省略している名前の項目の上では、全部の名前をツールチップに出す。</summary>
    private void UpdateNameTip(Point location)
    {
        var index = HitAt(location).Index;
        if (index == _tipIndex) return;
        _tipIndex = index;
        _nameTip.SetToolTip(this, ShowsNameTip(index) ? _state.Entries[index].Name : "");
    }

    private void ResetNameTip()
    {
        _tipIndex = -1;
        _nameTip.SetToolTip(this, "");
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        ResetNameTip();
        SetHeaderHot(-1);
    }

    /// <summary>R-78: ドラッグ中にカーソルに付ける画像。先頭の項目のアイコンと名前、複数なら件数。</summary>
    private Bitmap DragImage(IReadOnlyList<Entry> targets)
    {
        var first = targets[0];
        var text = targets.Count == 1 ? first.Name : $"{first.Name} ほか {targets.Count - 1} 件";
        var icon = first.Kind == EntryKind.File ? _icons.ForFile(first.FullPath) : _icons.ForFolder();
        return DragImageRenderer.Render(icon, _icons.Size, text, _font, _theme.Foreground, _theme.Background, Gap);
    }

    // R-111-2: 右ボタンの印は効果を決める前に覚える（DropFeedback が右ボタンの効果を返すため）。
    // 4 つの受け口はすべて DropButton.Guard を通す（INV-RIGHT-DROP-SAME-ROUTE）。素通しだと、
    // ここから先が例外を投げたとき OLE が DragEnter 失敗と見なして以後 DragLeave を呼ばなくなり、
    // 右ボタンの印（DragButtonState.Right）が残ったまま次の左ドロップが右ドロップのメニュー扱いになる
    protected override void OnDragEnter(DragEventArgs e) =>
        DropButton.Guard(e, () => { DropButton.Enter(e); UpdateDrop(e); }, EndDrop);

    protected override void OnDragOver(DragEventArgs e) =>
        DropButton.Guard(e, () => { DropButton.Over(e); UpdateDrop(e); }, EndDrop);

    // Esc での取り消しも OLE は DragLeave を呼ぶ。枠と自動スクロールはここで片付く
    protected override void OnDragLeave(EventArgs e) =>
        DropButton.Guard(null, () => { base.OnDragLeave(e); EndDrop(); DropButton.Leave(); }, EndDrop);

    /// <summary>R-110-1: 落とした位置の項目と宛先。宛先を決めるのはドメイン（DropRouting）で、項目はレイアウトに聞く。</summary>
    private (string Destination, int Index, Entry? Hit) DropTargetAt(DragEventArgs e)
    {
        var point = PointToClient(new Point(e.X, e.Y));
        // 縦横どちらのずれも足す（Phase 15 の詳細表示は縦と横のスクロールが同時にある）
        var index = FileViewScroll.IndexAt(_layout, _scroll, point.X, point.Y, _state.Count);
        var hit = index >= 0 ? _state.Entries[index] : null;
        return (DropRouting.FileListDestination(InPanelDragDrop, hit, DropFolder), index, hit);
    }

    private void UpdateDrop(DragEventArgs e)
    {
        var (destination, index, hit) = DropTargetAt(e);
        // R-78: 表示と実際の転送は同じ判定。ドラッグしている項目自身・その中は DropRules が断る（「不可」。新しい判定は足さない）
        DropFeedback.Apply(e, destination, DropFeedback.FolderLabel(destination));
        var frame = e.Effect != DragDropEffects.None && DropRouting.TargetsItem(InPanelDragDrop, hit) ? index : -1;
        if (frame != _dropTarget) { _dropTarget = frame; Invalidate(); }
        var point = PointToClient(new Point(e.X, e.Y));
        SetAutoScroll(InPanelDragDrop ? _layout.AutoScrollDirection(point.X, point.Y - _layout.HeaderHeight, ViewportWidth, ViewportHeight) : (0, 0));
    }

    private void EndDrop()
    {
        SetAutoScroll((0, 0));
        if (_dropTarget != -1) { _dropTarget = -1; Invalidate(); }
    }

    private void SetAutoScroll((int X, int Y) direction)
    {
        if (direction == _autoScrollDirection) return;
        _autoScrollDirection = direction;
        _autoScroll.Stop();
        if (direction != (0, 0)) _autoScroll.Start();
    }

    /// <summary>R-110-3: 1 段。向きも段の量もレイアウトが決める（今の一覧では横に 1 列）。スクロールできる端まで来たら止める。</summary>
    private void AutoScrollStep()
    {
        var next = FileViewScroll.Next(_layout, _scroll, _autoScrollDirection, _state.Count, ViewportWidth, ViewportHeight);
        if (next == _scroll) { SetAutoScroll((0, 0)); return; }
        _scroll = next;
        UpdateScrollBars();
        Invalidate();   // 枠の位置は次の DragOver で決め直す（OLE はマウスが止まっていても DragOver を呼び続ける）
    }

    protected override void OnDragDrop(DragEventArgs e) => DropButton.Guard(e, () =>
    {
        base.OnDragDrop(e);
        DropButton.Drop(e, () =>   // R-111-2 / T6: 右ボタンなら None を返す。例外でも印を残さない
        {
            EndDrop();   // DropTargetAt が例外を投げても枠と自動スクロールのタイマーは必ず片付く（_scroll は変えない）
            var (destination, _, _) = DropTargetAt(e);
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
            {
                var (ctrl, shift) = DropFeedback.Modifiers(e);
                FilesDropped?.Invoke(this, new Drop(paths, destination, e.AllowedEffect, ctrl, shift));
            }
        });
    }, EndDrop);

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button != MouseButtons.Left) return;
        if (e.Y < _layout.HeaderHeight)
        {
            // 境界の上なら自動の幅に戻す。ダブルクリックの 2 度目の押下でつかんだ境界は、離しても幅を保存しない
            var border = DetailsLayout.HeaderBorderAt(_layout.Header, e.X + _layout.ScrollOffset(_scroll).X, Scaled(4));
            _headerDrag = null;
            _dragWidths.Clear();
            if (border >= 0) ColumnWidthChanged?.Invoke(this, (DetailsColumnWidths.Key(_layout.Header[border].Column), null));
            return;
        }
        // 項目の無い余白では何も起こさない。OnMouseDown は余白でカーソルを動かさないので、
        // ここで見ないと「余白を叩いたらカーソル位置の項目が起動した」になる（V-09）
        if (HitAt(e.Location).Index < 0) return;
        if (_state.Cursor is { } cursor) EntryActivated?.Invoke(this, cursor);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragIndex = -1;

        if (e.Button == MouseButtons.Left && (_headerDrag is not null || _headerPress >= 0))
        {
            var drag = _headerDrag;
            var press = _headerPress;
            (_headerDrag, _headerPress) = (null, -1);
            InvalidateHeader();
            if (drag is { } d && _dragWidths.Count > 0)   // 動かしていなければ保存しない
            {
                var column = _layout.Header[d.Cell].Column;
                var width = _dragWidths[DetailsColumnWidths.Key(column)];
                _dragWidths.Clear();
                ColumnWidthChanged?.Invoke(this, (DetailsColumnWidths.Key(column), DetailsColumnWidths.ToLogical(width, DeviceDpi)));
            }
            else if (press >= 0 && e.Y < _layout.HeaderHeight
                     && DetailsLayout.HeaderCellAt(_layout.Header, e.X + _layout.ScrollOffset(_scroll).X) == press)
                HeaderClicked?.Invoke(this, _layout.Header[press].Column);
            return;
        }
        if (e.Button == MouseButtons.Right && _headerRightDown && e.Y < _layout.HeaderHeight)
        {
            _headerRightDown = false;
            // 見出しの上では項目のメニューではなく列のメニュー
            DetailsHeaderMenu(DetailsLayout.HeaderCellAt(_layout.Header, e.X + _layout.ScrollOffset(_scroll).X)).Show(this, e.Location);
            return;
        }

        if (e.Button == MouseButtons.Right && _rightDown is { } down)
        {
            _rightDown = null;
            // R-111-1: 対象は押した位置の項目、出す位置は離した位置（マウスの位置。INV-POPUP-POSITION）
            RightClicked?.Invoke(this, new RightClick(down.Index, PointToScreen(e.Location), down.Shift));
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            // R-11-2: Shift の範囲マークは、離した時点でカーソルも押した項目へ動く（MarkOnRelease.Release の中で）。
            // 動いたかどうかは離す前のカーソル位置と比べる必要があるので、Release より前に取っておく
            var before = _state.CursorIndex;
            var marksChanged = _markOnRelease.Release(_state);
            // INV-DETAILS-ROW-HIT: 名前以外はマークを変えずにカーソルだけ動く
            if (marksChanged || _state.CursorIndex != before) Commit(before, marksChanged);
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        // V9: Ctrl+ホイールは表示モードの段（奥へ回すと上の段）。スクロールはしない
        if (ModifierKeys.HasFlag(Keys.Control))
        {
            var notches = _modeWheel.Add(e.Delta, SystemInformation.MouseWheelScrollDelta);
            if (notches != 0) ViewModeWheel?.Invoke(this, notches);
            return;
        }
        // Ctrl を離したあとに、たまった分が次の Ctrl+ホイールへ持ち越されないようにする
        _modeWheel.Reset();
        var (horizontal, vertical) = _layout.ScrollBars;
        // スクロールできない間にたまった分が、後でまとめて効かないようにする
        if (!horizontal && !vertical) { _wheel.Reset(); return; }
        // R-76: 一覧は 1 ノッチ = 1 列（左端は常に列の境界に揃う）。詳細は縦のバーがあれば縦に MouseWheelScrollLines 行、無ければ横に 1 段
        var steps = _wheel.Add(e.Delta, SystemInformation.MouseWheelScrollDelta);
        if (vertical) ScrollBy(0, -steps * Math.Max(1, SystemInformation.MouseWheelScrollLines));
        else ScrollBy(-steps, 0);
    }

    /// <summary>
    /// R-114 / V6 / Q21: 見出しの右クリックのメニュー（settingsOwners の menu:FileListView#DetailsHeader）。
    /// 「列のサイズを自動的に変更する」は押した列（名前の見出しなら名前の列）、「すべての列…」はすべての手動の幅を消す。
    /// 区切りの下に、名前以外の列を設定の並び順にチェック付きで置く。変更は MainForm が保存して全ウィンドウへ当てる。
    /// </summary>
    internal ContextMenuStrip DetailsHeaderMenu(int cellIndex)
    {
        var menu = new ContextMenuStrip();
        var key = DetailsColumnWidths.Key(cellIndex >= 0 && cellIndex < _layout.Header.Count ? _layout.Header[cellIndex].Column : null);
        // 見出しの無い余白（cellIndex < 0）では、Key(null) が名前の列を指してしまうので、対象の列が無いこの項目は使えなくする
        var autoWidth = menu.Items.Add("列のサイズを自動的に変更する", null, (_, _) => ColumnWidthChanged?.Invoke(this, (key, null)));
        autoWidth.Enabled = cellIndex >= 0 && cellIndex < _layout.Header.Count;
        menu.Items.Add("すべての列のサイズを自動的に変更する", null, (_, _) => ColumnWidthsReset?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        foreach (var setting in _views.Details.Columns)
        {
            var item = new ToolStripMenuItem(DetailsCells.Header(setting.Column)) { Checked = setting.Visible };
            var column = setting.Column;
            item.Click += (_, _) => ColumnVisibilityChanged?.Invoke(this, (column, !item.Checked));
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) => { if (IsHandleCreated) BeginInvoke(menu.Dispose); };
        return menu;
    }

    /// <summary>
    /// カーソルを動かさずに横スクロールだけ進める（テスト用にホイールの計算から切り出した。R-11-2）。
    /// delta の符号は WheelAccumulator.Add の戻り値と同じ（正で列 0 の方向へ戻る）。
    /// </summary>
    internal void ScrollColumns(int delta) => ScrollBy(-delta, 0);

    private void ScrollBy(int dx, int dy)
    {
        _scroll = new ScrollPosition(_scroll.X + dx, _scroll.Y + dy);
        UpdateScrollBars();   // 範囲へのクランプもここ
        Invalidate();
    }

    internal new IFileViewLayout Layout => _layout;

    internal ScrollPosition ScrollPosition => _scroll;

    private void Commit(int cursorBefore, bool marksChanged)
    {
        EnsureCursorVisible();
        // Invalidate だけだと再描画が予約されるだけで、ダブルクリックの 2 打目や
        // その後のフォルダ遷移が先に走り、カーソルが移った姿が一度も画面に出ない。
        // Update で同期的に描き切ってから次の処理へ渡す
        Invalidate();
        Update();
        if (_state.CursorIndex != cursorBefore) CursorMoved?.Invoke(this, EventArgs.Empty);
        if (marksChanged) MarksChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>R-66: 拡大率が変わっても再起動なしで正しく描き直す。</summary>
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        RebuildFontResources();   // フォント・計測面・アイコンを新しい DPI で作り直す
        RecomputeLayout();        // R-66-3: 行高・列幅・拡張子の位置を再計算する
        EnsureCursorVisible();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _font?.Dispose();
            _measure?.Dispose();
            _icons?.Dispose();
            _autoScroll.Dispose();
            _nameTip.Dispose();
        }
        ShellFileType.Resolved -= OnTypeResolved;
        base.Dispose(disposing);
    }
}
