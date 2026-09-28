using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ReTAC.App.Rendering;
using ReTAC.Domain.Entries;
using ReTAC.Domain.FileOps;
using ReTAC.Domain.Listing;
using ReTAC.Domain.Selection;
using ReTAC.Shell;

namespace ReTAC.App;

/// <summary>
/// 自前描画の多段組ファイルリスト（R-01）。
/// エントリは縦に流れ、高さを超えると右隣の列へ折り返す。スクロールは横方向のみ（R-01-2）。
/// 標準 ListView では再現できない段組みと選択状態を扱うため、自前描画する。
/// </summary>
public sealed class FileListView : Control
{
    private readonly HScrollBar _scrollBar = new() { Dock = DockStyle.Bottom, Visible = false };

    private Theme _theme = Theme.Default;
    private Font _font = null!;
    private TextMeasure _measure = null!;
    private ShellIcons _icons = null!;
    private ListState _state = new([]);
    private ColumnLayout _layout = ColumnLayout.Empty;
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
    /// <summary>R-110-2: 落とす先として枠で囲む項目。-1 なら囲まない。</summary>
    private int _dropTarget = -1;
    /// <summary>R-110-3 / T5: 端で止めている間、この間隔で 1 列ずつスクロールする（実機で 0.3〜0.5 秒を比べて決めた）。</summary>
    private const int AutoScrollInterval = 400;
    private readonly System.Windows.Forms.Timer _autoScroll = new() { Interval = AutoScrollInterval };
    private (int X, int Y) _autoScrollDirection;

    public FileListView()
    {
        AllowDrop = true;   // R-65 ②: エクスプローラー等からのドロップを受ける
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
        TabStop = true;
        // B-04: ファイルリストは 1 打鍵がコマンドである。IME がオンだと文字が食われて
        // 何も動かない。入力欄（TextInputDialog 等）は別ウィンドウなので影響しない
        ImeMode = ImeMode.Disable;
        Controls.Add(_scrollBar);
        _scrollBar.Scroll += (_, e) => { _scroll = _scroll with { X = e.NewValue }; Invalidate(); };
        _autoScroll.Tick += (_, _) => AutoScrollStep();
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

    /// <summary>R-110: ファイル表示パネルの中の項目の上へ落とせるか。MainForm が設定から当てる。</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool InPanelDragDrop { get; set; }

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
    public Point PopupAnchor() => new(
        _layout.XOf(_state.CursorIndex) - _layout.ScrollOffset(_scroll).X + _icons.Size,
        _layout.YOf(_state.CursorIndex) + _layout.RowHeight);

    /// <summary>カーソルを移す。スクロールと再描画とイベント通知まで面倒を見る。</summary>
    public void MoveCursorTo(int index)
    {
        var before = _state.CursorIndex;
        _state.MoveCursor(index);
        Commit(before, marksChanged: false);
    }

    /// <param name="keepScroll">
    /// 同じフォルダの再表示。横スクロール位置を保つ。
    /// 自動更新のたびにカーソル列へ引き戻されると、右の方を見ている最中に読めなくなる（R-10）
    /// </param>
    public void SetEntries(IReadOnlyList<Entry> entries, int cursorIndex = 0, bool keepScroll = false)
    {
        // 自動更新などでボタンを押したまま一覧が入れ替わることがある。押した時点の添字は
        // 別の項目を指すことになるので、離した時点の処理（マーク・右ボタンのドラッグ／メニュー）は捨てる
        _rightDown = null;
        _markOnRelease.Cancel();
        _dragIndex = -1;
        var scroll = _scroll.X;
        _state = new ListState(entries);
        _state.MoveCursor(cursorIndex);
        _scroll = _scroll with { X = keepScroll ? scroll : 0 };
        if (!keepScroll) _wheel.Reset();
        RecomputeLayout();
        _scroll = _scroll with { X = Math.Clamp(_scroll.X, 0, MaxScrollColumn) };   // 件数が減って列が消えた場合
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

    /// <summary>左端は常に列の境界。列幅の途中で止めない（実機確認・2026-09-09）。</summary>
    private int ScrollX => _layout.ScrollOffset(_scroll).X;

    /// <summary>丸ごと収まる列の数。端数の列は右端で切れるが、それは実機と同じ。</summary>
    private int VisibleColumns => Math.Max(1, ClientSize.Width / Math.Max(1, _layout.ColumnWidth));

    private int MaxScrollColumn => _layout.MaxScrollPosition(_state.Count, ClientSize.Width, ViewportHeight).X;

    private int ViewportHeight => Math.Max(0, ClientSize.Height - (_scrollBar.Visible ? _scrollBar.Height : 0));

    private void RebuildFontResources()
    {
        _font?.Dispose();
        _measure?.Dispose();
        _icons?.Dispose();
        _font = new Font(_theme.FontFamily, _theme.FontSize);
        _measure = new TextMeasure(_font, DeviceDpi);
        _icons = new ShellIcons(Scaled(16));
    }

    private void RecomputeLayout()
    {
        // R-01-3: 列幅はウィンドウ幅から独立し、フォルダの最長のファイル名で決まる
        _layout = EntryMetrics.Layout(
            _state.Entries, _measure,
            viewportHeight: ViewportHeight,
            iconWidth: _icons.Size,
            gap: Gap,
            rowPadding: RowPadding,
            columnPadding: ColumnPaddingValue);

        UpdateScrollBar();
    }

    private void UpdateScrollBar()
    {
        var total = _layout.TotalWidth;
        var needed = total > ClientSize.Width;
        if (_scrollBar.Visible != needed)
        {
            _scrollBar.Visible = needed;
            // 表示の有無でビューポート高が変わるため、行数を計算し直す
            _layout = EntryMetrics.Layout(
                _state.Entries, _measure,
                viewportHeight: ViewportHeight,
                iconWidth: _icons.Size,
                gap: Gap,
                rowPadding: RowPadding,
                columnPadding: ColumnPaddingValue);
            total = _layout.TotalWidth;
        }

        if (!needed) { _scroll = _scroll with { X = 0 }; return; }

        // スクロールバーの単位も列にする
        _scrollBar.Minimum = 0;
        _scrollBar.Maximum = Math.Max(0, _layout.ColumnCount - 1);
        _scrollBar.LargeChange = VisibleColumns;
        _scrollBar.SmallChange = 1;
        SyncScrollBar();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RecomputeLayout();
        EnsureCursorVisible();
    }

    private void EnsureCursorVisible()
    {
        if (!_scrollBar.Visible || _state.Count == 0) return;

        var cursorColumn = _layout.ColumnOf(_state.CursorIndex);
        if (cursorColumn < _scroll.X) _scroll = _scroll with { X = cursorColumn };
        else if (cursorColumn > _scroll.X + VisibleColumns - 1) _scroll = _scroll with { X = cursorColumn - VisibleColumns + 1 };
        SyncScrollBar();
    }

    private void SyncScrollBar()
    {
        _scroll = _scroll with { X = Math.Clamp(_scroll.X, 0, MaxScrollColumn) };
        if (_scrollBar.Visible) _scrollBar.Value = Math.Min(_scroll.X, _scrollBar.Maximum);
    }

    // ---- 描画 -------------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(_theme.Background);
        if (_state.Count == 0) return;

        // 可視範囲の列だけを描く（件数に依存しない・R-01）。
        // 右端で切れる 1 列ぶんを余分に描く（R-01-4: 切れるのはウィンドウの右端）
        var firstColumn = _scroll.X;
        var lastColumn = Math.Min(_layout.ColumnCount - 1, _scroll.X + VisibleColumns);

        for (var column = firstColumn; column <= lastColumn; column++)
        {
            for (var row = 0; row < _layout.RowsPerColumn; row++)
            {
                var index = column * _layout.RowsPerColumn + row;
                if (index >= _state.Count) break;
                DrawRow(e.Graphics, index, Gap);
            }
        }
    }

    private void DrawRow(Graphics g, int index, int gap)
    {
        var entry = _state.Entries[index];
        var isCursor = index == _state.CursorIndex;
        var isMarked = _state.Marks.Contains(index);

        // N-04-4: 塗りつぶしの範囲は文字列の長さではなく列の幅で決まる
        // R-110-2: 矩形はレイアウトから取る。縦横どちらのずれも引く
        var (x, y, width, height) = FileViewScroll.VisibleBounds(_layout, _scroll, index);
        var rect = new Rectangle(x, y, width, height);

        // R-11-6: カーソルとマークが重なる行は背景をカーソル色にし、★は残す
        var (background, foreground) = RowColors.Of(_theme, AttributeColorRule.Classify(entry.Attributes), isCursor, isMarked);
        using (var brush = new SolidBrush(background))
            g.FillRectangle(brush, rect);

        var iconRect = new Rectangle(rect.X + _layout.ColumnPadding,
            rect.Y + (rect.Height - _icons.Size) / 2, _icons.Size, _icons.Size);
        var icon = entry.Kind == EntryKind.File ? _icons.ForFile(entry.FullPath) : _icons.ForFolder();
        if (icon is not null) g.DrawImage(icon, iconRect);
        if (isMarked) MarkStar.Draw(g, iconRect, _theme.MarkStarColor);   // R-11-5

        var textTop = rect.Y + (rect.Height - _measure.LineHeight()) / 2;
        var baseX = rect.X + _layout.ColumnPadding + _icons.Size + gap;

        // R-01-4: 省略記号を使わない。列の矩形でクリップする
        var baseRect = new Rectangle(baseX, textTop, Math.Max(0, rect.X + _layout.ExtensionOffset - baseX), _layout.RowHeight);
        TextRenderer.DrawText(g, entry.BaseName, _font, baseRect, foreground, TextMeasure.Flags);

        // R-01-6 / R-07: 拡張子は列の先頭からの固定オフセット。フォルダは Extension が空なので何も出ない
        if (entry.Extension.Length > 0)
        {
            var extRect = new Rectangle(
                rect.X + _layout.ExtensionOffset, textTop,
                Math.Max(0, rect.Right - (rect.X + _layout.ExtensionOffset)), _layout.RowHeight);
            TextRenderer.DrawText(g, entry.Extension, _font, extRect, foreground, TextMeasure.Flags);
        }

        // R-110-2: 落とす先はその項目の文字の色の枠。塗りはカーソルとマークが使っているので使わない
        if (index == _dropTarget)
        {
            using var pen = new Pen(RowColors.Frame(background, foreground), Scaled(2)) { Alignment = PenAlignment.Inset };
            g.DrawRectangle(pen, rect);
        }
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
        var page = _layout.RowsPerColumn * VisibleColumns;
        var before = _state.CursorIndex;
        var marksChanged = false;

        switch (e.KeyCode)
        {
            case Keys.Up: _state.MoveCursorBy(-1); break;
            case Keys.Down: _state.MoveCursorBy(1); break;
            // R-01-5: 隣の列の同じ高さへ。隣に項目が無ければ動かさない
            case Keys.Left: _state.MoveCursorToNeighborColumn(-_layout.RowsPerColumn); break;
            case Keys.Right: _state.MoveCursorToNeighborColumn(_layout.RowsPerColumn); break;
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

        var index = _layout.IndexAt(e.X + ScrollX, e.Y, _state.Count);

        if (e.Button == MouseButtons.Right)
        {
            _rightDown = (index, e.Location, ModifierKeys.HasFlag(Keys.Shift));
            return;
        }
        if (e.Button != MouseButtons.Left || index < 0) return;

        _dragOrigin = e.Location;
        _dragIndex = index;

        // R-11-2 / B-07: 先頭の余白もアイコンの当たり判定に含める。
        // 卓駆も左端の余白でマークがトグルする
        var onIcon = e.X - (_layout.ColumnOf(index) - _scroll.X) * _layout.ColumnWidth
                     < _layout.ColumnPadding + _icons.Size;

        // Q7: マークを変えるのは離した時点（MarkOnRelease）。押した時点ではカーソルだけ移す
        var before = _state.CursorIndex;
        _markOnRelease.Press(_state, index, onIcon, ModifierKeys.HasFlag(Keys.Shift));
        Commit(before, marksChanged: false);
    }

    // ---- ドラッグ＆ドロップ（R-65） ---------------------------------------

    /// <summary>ドラッグの開始位置。左ボタンが押されたまま一定距離動いたらドラッグとみなす。</summary>
    private Point _dragOrigin;
    private int _dragIndex = -1;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

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

        if (e.Button != MouseButtons.Left || _dragIndex < 0) return;

        var moved = Math.Abs(e.X - _dragOrigin.X) >= SystemInformation.DragSize.Width
                 || Math.Abs(e.Y - _dragOrigin.Y) >= SystemInformation.DragSize.Height;
        if (!moved) return;

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

    /// <summary>R-78: ドラッグ中にカーソルに付ける画像。先頭の項目のアイコンと名前、複数なら件数。</summary>
    private Bitmap DragImage(IReadOnlyList<Entry> targets)
    {
        var first = targets[0];
        var text = targets.Count == 1 ? first.Name : $"{first.Name} ほか {targets.Count - 1} 件";
        var icon = first.Kind == EntryKind.File ? _icons.ForFile(first.FullPath) : _icons.ForFolder();
        return DragImageRenderer.Render(icon, _icons.Size, text, _font, _theme.Foreground, _theme.Background, Gap);
    }

    // R-111-2: 右ボタンの印は効果を決める前に覚える（DropFeedback が右ボタンの効果を返すため）
    protected override void OnDragEnter(DragEventArgs e)
    {
        DropButton.Enter(e);
        UpdateDrop(e);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        DropButton.Over(e);
        UpdateDrop(e);
    }

    // Esc での取り消しも OLE は DragLeave を呼ぶ。枠と自動スクロールはここで片付く
    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        EndDrop();
        DropButton.Leave();
    }

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
        SetAutoScroll(InPanelDragDrop ? _layout.AutoScrollDirection(point.X, point.Y, ClientSize.Width, ViewportHeight) : (0, 0));
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
        var next = FileViewScroll.Next(_layout, _scroll, _autoScrollDirection, _state.Count, ClientSize.Width, ViewportHeight);
        if (next == _scroll) { SetAutoScroll((0, 0)); return; }
        _scroll = next;
        SyncScrollBar();
        Invalidate();   // 枠の位置は次の DragOver で決め直す（OLE はマウスが止まっていても DragOver を呼び続ける）
    }

    protected override void OnDragDrop(DragEventArgs e)
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
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button != MouseButtons.Left) return;
        // 項目の無い余白では何も起こさない。OnMouseDown は余白でカーソルを動かさないので、
        // ここで見ないと「余白を叩いたらカーソル位置の項目が起動した」になる（V-09）
        if (_layout.IndexAt(e.X + ScrollX, e.Y, _state.Count) < 0) return;
        if (_state.Cursor is { } cursor) EntryActivated?.Invoke(this, cursor);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragIndex = -1;

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
            if (_markOnRelease.Release(_state)) Commit(before, marksChanged: true);
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        // スクロールできない間にたまった分が、後でまとめて効かないようにする
        if (!_scrollBar.Visible) { _wheel.Reset(); return; }
        // R-76: 1 ノッチ = 1 列。左端は常に列の境界に揃う
        _scroll = _scroll with { X = _scroll.X - _wheel.Add(e.Delta, SystemInformation.MouseWheelScrollDelta) };
        SyncScrollBar();
        Invalidate();
    }

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
        }
        base.Dispose(disposing);
    }
}
