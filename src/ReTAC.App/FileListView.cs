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
    private readonly HScrollBar _hScrollBar = new() { Dock = DockStyle.Bottom, Visible = false };
    private readonly VScrollBar _vScrollBar = new() { Dock = DockStyle.Right, Visible = false };

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
        Controls.Add(_hScrollBar);
        Controls.Add(_vScrollBar);
        _hScrollBar.Scroll += (_, e) => { _scroll = _scroll with { X = e.NewValue }; Invalidate(); };
        _vScrollBar.Scroll += (_, e) => { _scroll = _scroll with { Y = e.NewValue }; Invalidate(); };
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
        var textStart = ColumnPaddingValue + _icons.Size + Gap;
        // R-113: 「自動」はパネルの幅（名前の文字の外側を引いたもの）、「最大文字数」は数字 0 の幅 × 文字数（Q9）
        var cap = NameWidths.TextCap(_views.List.NameWidth, _measure.Width("0"), ClientSize.Width - textStart - ColumnPaddingValue);
        _layout = EntryMetrics.Layout(_state.Entries, _measure, ClientSize.Width, ClientSize.Height, _hScrollBar.Height,
            _icons.Size, Gap, RowPadding, ColumnPaddingValue, cap);
        UpdateScrollBars();
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
        if (_state.Count == 0) return;
        var (ox, oy) = _layout.ScrollOffset(_scroll);
        // R-01-4: 右端で切れる列も描く（切れるのはウィンドウの右端）。IndexesIn は交わる項目を返すのでそのまま入る。
        // カーソルの項目は最後に描く（全部描くときに右隣へ重ねるため）
        var visible = _layout.IndexesIn(ox, oy, ViewportWidth, ViewportHeight, _state.Count);
        foreach (var index in visible.Where(i => i != _state.CursorIndex)) DrawRow(e.Graphics, index);
        if (visible.Contains(_state.CursorIndex)) DrawRow(e.Graphics, _state.CursorIndex);
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
            TextRenderer.DrawText(g, entry.Name, _font, all, foreground, TextMeasure.Flags);
            return;
        }

        var showExtension = ext.Width > 0 && entry.Extension.Length > 0;
        var baseText = showExtension ? entry.BaseName : NameWithoutAlignment(entry);
        var baseRight = showExtension ? ext.X - Gap : name.Right;
        var baseRect = new Rectangle(name.X + Gap, top, Math.Max(0, baseRight - name.X - Gap), _measure.LineHeight());
        // R-01-4: すべて表示では「…」を出さない。実際に収まらないときだけ EndEllipsis を付ける（1px の測り違いで出さない）
        var ellipsis = IsTruncated(index) ? TextFormatFlags.EndEllipsis : 0;
        TextRenderer.DrawText(g, baseText, _font, baseRect, foreground, TextMeasure.Flags | ellipsis);
        if (showExtension)
            TextRenderer.DrawText(g, entry.Extension, _font, ext with { Y = top, Height = _measure.LineHeight() }, foreground,
                TextMeasure.Flags | ellipsis);
    }

    /// <summary>揃えた拡張子を出さないとき（詳細表示で名前に拡張子を出さない設定）の名前。一覧では常に本体（拡張子は揃えて出す）。</summary>
    private string NameWithoutAlignment(Entry entry) => entry.BaseName;

    /// <summary>R-113: 省略して描いているか。本体の実測が本体の領域より広いか、拡張子の実測が拡張子の領域より広いとき。</summary>
    internal bool IsTruncated(int index)
    {
        if (index < 0 || index >= _state.Count) return false;
        var entry = _state.Entries[index];
        var (nx, _, nw, _) = _layout.NameBounds(index);
        var (ex, _, ew, _) = _layout.ExtensionBounds(index);
        var showExtension = ew > 0 && entry.Extension.Length > 0;
        var baseWidth = (showExtension ? ex - Gap : nx + nw) - nx - Gap;
        return _measure.Width(showExtension ? entry.BaseName : NameWithoutAlignment(entry)) > baseWidth
               || showExtension && _measure.Width(entry.Extension) > ew;
    }

    /// <summary>
    /// Q12 / Q27 / R-113: カーソルの項目を省略せず全部描くか。描くのは一覧と小〜特大アイコンだけ（Phase 15 では一覧）。
    /// 詳細表示では、名前を右へ重ねるとその行自身のサイズ・日時が隠れるので描かない。
    /// </summary>
    internal bool DrawsFullName(int index) => index == _state.CursorIndex && _mode == FileViewMode.List && IsTruncated(index);

    /// <summary>カーソルの項目を全部描くときの帯の幅（左の余白・アイコン・間・名前・右の余白）。</summary>
    private int FullNameWidth(Entry entry) => ColumnPaddingValue + _icons.Size + Gap + _measure.Width(entry.Name) + ColumnPaddingValue;

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

        var index = HitAt(e.Location).Index;

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
        if (_pressArea == FileViewArea.Other) { _markOnRelease.Moved(); _dragIndex = -1; return; }

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
        _nameTip.SetToolTip(this, index >= 0 && IsTruncated(index) ? _state.Entries[index].Name : "");
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
        // 項目の無い余白では何も起こさない。OnMouseDown は余白でカーソルを動かさないので、
        // ここで見ないと「余白を叩いたらカーソル位置の項目が起動した」になる（V-09）
        if (HitAt(e.Location).Index < 0) return;
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
            var marksChanged = _markOnRelease.Release(_state);
            // INV-DETAILS-ROW-HIT: 名前以外はマークを変えずにカーソルだけ動く
            if (marksChanged || _state.CursorIndex != before) Commit(before, marksChanged);
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        // スクロールできない間にたまった分が、後でまとめて効かないようにする
        if (!_layout.ScrollBars.Horizontal) { _wheel.Reset(); return; }
        // R-76: 1 ノッチ = 1 列。左端は常に列の境界に揃う
        ScrollColumns(_wheel.Add(e.Delta, SystemInformation.MouseWheelScrollDelta));
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
        base.Dispose(disposing);
    }
}
