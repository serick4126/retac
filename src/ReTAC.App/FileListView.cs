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

/// <summary>R-116 / R-117 / R-118: 格子の項目の層（下から）。</summary>
internal enum GridLayer { Fill, Image, Overlay, CheckBox, CursorFrame, DropFrame }

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
        _hScrollBar.Scroll += (_, e) => RaiseScrollBar(vertical: false, e.NewValue);
        _vScrollBar.Scroll += (_, e) => RaiseScrollBar(vertical: true, e.NewValue);
        _autoScroll.Tick += (_, _) => AutoScrollTick();
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
        UpdateImageQueue();   // R-117: ハンドルができたので、ここから背景の取得を始められる
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
    public bool InPanelDragDrop => _mode switch
    {
        FileViewMode.Details => _views.Details.InPanelDragDrop,
        FileViewMode.List => _views.List.InPanelDragDrop,
        _ => _views.Icons.InPanelDragDrop,
    };

    /// <summary>Phase 16 §10: 今のモードの系統で範囲選択（投げ縄）を許すか。オフなら投げ縄を始めない（クリック・Shift+クリックは変わらない）。</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool RangeSelection => _mode switch
    {
        FileViewMode.Details => _views.Details.RangeSelection,
        FileViewMode.List => _views.List.RangeSelection,
        _ => _views.Icons.RangeSelection,
    };

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
        CancelLasso();   // R-120: 押した時点の中身の座標は別の項目を指す
        HotIndex = -1;
        _rightDown = null;
        _markOnRelease.Cancel();
        _dragIndex = -1;
        var scroll = _scroll;
        _state = new ListState(entries);
        _indexOfPath = new(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _state.Count; i++) _indexOfPath[_state.Entries[i].FullPath] = i;
        _content = null;
        _state.MoveCursor(cursorIndex);
        _scroll = keepScroll ? scroll : default;
        if (!keepScroll) _wheel.Reset();
        RecomputeLayout();
        // 見えている位置ならこの中で何も起きない。カーソルが画面外のときだけ動く
        EnsureCursorVisible();
        RefreshHot();            // R-116: 入れ替わった一覧のマウスの下の項目
        NextImageGeneration();   // R-117: 同じ内容の読み直しでも進める（届く前の結果は別の一覧のもの）
        Invalidate();
        CursorMoved?.Invoke(this, EventArgs.Empty);
        MarksChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- サムネイルと OS の印（R-117 / R-118） ----------------------------------

    /// <summary>一覧を入れる（同じ内容の読み直しを含む）・モードや大きさ・サムネイルと印の設定を変える・dpi が変わるたびに進める。古い結果を捨てるため。</summary>
    internal int ImageGeneration { get; private set; }
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal ShellImageWorker? ImageWorker { get; set; }
    internal int InvalidatedItems { get; private set; }
    private (int, int) _imageRange = (-1, -1);

    /// <summary>ponytail: 上限は画素のバイト数の合計 64MB（256px で 256 枚）。足りなければ上げる。</summary>
    private const long ThumbnailBytes = 64L * 1024 * 1024;
    /// <summary>鍵は（パス・大きさ・更新日時）。世代をまたいで使い回す（読み直しても中身が同じなら取り直さない）。</summary>
    private readonly LruCache<(string Path, int Size, DateTime Modified), Bitmap> _thumbnails =
        new(ThumbnailBytes, b => (long)b.Width * b.Height * 4, b => b.Dispose());
    /// <summary>
    /// 作れなかった（最終の結果がサムネイル無し）鍵。世代をまたいで覚え、同じ（パス・大きさ・更新日時）を毎回要求し直さない。
    /// ponytail: 上限 4096 件。満ちたら全部忘れる（また 1 回ずつ問い合わせるだけ）。古い順に捨てる必要が出たら LRU にする。
    /// </summary>
    private readonly HashSet<(string Path, int Size, DateTime Modified)> _noThumbnail = [];
    private const int NoThumbnailCap = 4096;

    /// <summary>今の一覧の印の番号（パス → 番号）。世代が変わったら捨てる（同期状態は読み直しで変わりうる）。</summary>
    private readonly Dictionary<string, int> _overlays = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>今の一覧のパス → 添字。SetEntries で作る（届いた結果のたびに一覧を走査しない）。</summary>
    private Dictionary<string, int> _indexOfPath = new(StringComparer.OrdinalIgnoreCase);

    private bool ThumbnailsOn => _mode switch
    {
        FileViewMode.MediumIcons or FileViewMode.LargeIcons or FileViewMode.ExtraLargeIcons => _views.Icons.Thumbnails,
        _ => false,   // 並べて表示・コンテンツは Phase 17
    };

    /// <summary>R-117: この項目にサムネイルを使うか。フォルダは「フォルダに中身のサムネイルを出す」も見る（要求と描画の両方がここを通る）。</summary>
    private bool WantsThumbnail(Entry entry) =>
        ThumbnailsOn && !entry.IsParent && (entry.Kind == EntryKind.File || _views.Icons.FolderThumbnails);

    /// <summary>今の設定で描くサムネイル。設定でオフになったものは、キャッシュにあっても返さない。</summary>
    internal Bitmap? ThumbnailFor(Entry entry) =>
        WantsThumbnail(entry) && _thumbnails.TryGet((entry.FullPath, IconSizeFor(_mode), entry.LastWriteTime), out var b) ? b : null;

    internal IReadOnlyList<ImageRequest> BuildImageRequests()
    {
        var overlays = _views.Common.ShowOverlays;
        if (!overlays && !ThumbnailsOn || _state.Count == 0) return [];
        var (ox, oy) = _layout.ScrollOffset(_scroll);
        var visible = _layout.IndexesIn(ox, oy, ViewportWidth, ViewportHeight, _state.Count);
        var page = Math.Max(1, _layout.PageItems(ViewportWidth, ViewportHeight));
        var first = visible.Count > 0 ? visible.Min() : 0;
        var last = visible.Count > 0 ? visible.Max() : 0;
        // 見えている項目、その後に後ろの 1 画面分、前の 1 画面分
        var order = visible.Concat(Enumerable.Range(last + 1, Math.Max(0, Math.Min(page, _state.Count - last - 1))))
            .Concat(Enumerable.Range(Math.Max(0, first - page), Math.Min(page, first)).Reverse());
        var size = IconSizeFor(_mode);
        var result = new List<ImageRequest>();
        foreach (var index in order)
        {
            var entry = _state.Entries[index];
            if (entry.IsParent) continue;
            var key = (entry.FullPath, size, entry.LastWriteTime);
            var wantsThumbnail = WantsThumbnail(entry) && !_noThumbnail.Contains(key) && !_thumbnails.TryGet(key, out _);
            var wantsOverlay = overlays && !_overlays.ContainsKey(entry.FullPath);
            if (!wantsThumbnail && !wantsOverlay) continue;
            result.Add(new ImageRequest(ImageGeneration, entry.FullPath, size, wantsThumbnail, wantsOverlay,
                CloudFiles.IsPlaceholder(entry.Attributes), entry.Kind != EntryKind.File, entry.LastWriteTime));
        }
        return result;
    }

    /// <summary>
    /// 見えている範囲・設定・一覧が変わった。待ちの列を丸ごと差し替える。
    /// 要求が空でも、ワーカーがあれば空で差し替えて古い待ちを消す（設定をオフにしたら問い合わせもしない。R-118）。空のためにワーカーは作らない。
    /// </summary>
    internal void UpdateImageQueue()
    {
        if (IsDisposed) return;
        var requests = BuildImageRequests();
        if (requests.Count == 0) { ImageWorker?.Replace([]); return; }
        // 結果は BeginInvoke で受けるので、ハンドルが無いうちは作らない（テストは ImageWorker を差し替えて使う）
        ImageWorker ??= IsHandleCreated ? CreateWorker() : null;
        ImageWorker?.Replace(requests);
    }

    private ShellImageWorker CreateWorker()
    {
        var worker = new ShellImageWorker();
        worker.Completed += result =>
        {
            // 背景のスレッド。破棄済み・ハンドルが無ければ解放して終わる（Phase 15 の種類名の取得と同じ）
            if (IsDisposed || !IsHandleCreated) { result.Thumbnail?.Dispose(); return; }
            try { BeginInvoke(() => DeliverImage(result)); }
            catch (InvalidOperationException) { result.Thumbnail?.Dispose(); }
        };
        return worker;
    }

    /// <summary>UI のスレッドでの反映。古い世代・閉じた後なら解放して捨てる。届いた項目の矩形だけを描き直す（組み直さない・全項目を走査しない）。</summary>
    internal void DeliverImage(ImageResult result)
    {
        var request = result.Request;
        if (IsDisposed || request.Generation != ImageGeneration) { result.Thumbnail?.Dispose(); return; }
        var overlayChanged = false;
        if (request.Overlay)
        {
            overlayChanged = _overlays.GetValueOrDefault(request.FullPath) != result.OverlayIndex;
            _overlays[request.FullPath] = result.OverlayIndex;
        }
        if (result.Thumbnail is { } thumbnail) _thumbnails.Add((request.FullPath, request.Size, request.Modified), thumbnail);
        else if (request.Thumbnail && (!request.Overlay || request.Cloud))
        {
            // 最終の結果だけ覚える。非クラウドの 1 段目（Overlay 付き）の null は、まだ 2 段目で作れるので覚えない
            if (_noThumbnail.Count >= NoThumbnailCap) _noThumbnail.Clear();
            _noThumbnail.Add((request.FullPath, request.Size, request.Modified));
        }
        if (result.Thumbnail is null && !overlayChanged) return;   // 描きが変わらないものは描き直さない
        if (!_indexOfPath.TryGetValue(request.FullPath, out var index)) return;
        InvalidatedItems++;
        Invalidate(ToRectangle(FileViewScroll.VisibleBounds(_layout, _scroll, index)));
    }

    /// <summary>世代を進め、印の番号を捨てて、要求を出し直す。</summary>
    private void NextImageGeneration()
    {
        ImageGeneration++;
        _overlays.Clear();
        UpdateImageQueue();
    }

    // ---- レイアウト -------------------------------------------------------

    private int Scaled(int logical) => logical * DeviceDpi / 96;

    private int Gap => Scaled(4);
    private int RowPadding => Scaled(2);

    /// <summary>格子の間隔・チェックボックスの大きさ・自動スクロールの端は設定に置かず、コードに固定する（96 dpi の値）。</summary>
    private int GridGap => Scaled(4);
    private int CheckBoxSize => Scaled(16);
    private int EdgeBand => Scaled(24);

    internal bool IsIconMode => _mode is FileViewMode.SmallIcons or FileViewMode.MediumIcons or FileViewMode.LargeIcons or FileViewMode.ExtraLargeIcons;

    /// <summary>R-115: 小アイコンは 16px 固定。中・大・特大は設定の値（96 dpi の値を dpi で比例）。</summary>
    internal int IconSizeFor(FileViewMode mode) => Scaled(mode switch
    {
        FileViewMode.MediumIcons => _views.Icons.MediumSize,
        FileViewMode.LargeIcons => _views.Icons.LargeSize,
        FileViewMode.ExtraLargeIcons => _views.Icons.ExtraLargeSize,
        _ => 16,
    });

    internal int ZeroWidth => _measure.Width("0");

    /// <summary>中〜特大のモードの大きさのアイコンの取り口。モードの大きさが変わったら作り直す。</summary>
    private ShellIcons? _bigIcons;
    /// <summary>B-07: 列の先頭と末尾の余白。卓駆と同程度。gap と同値にしてある。
    /// 見た目を詰めたい／広げたいときはここ 1 箇所を変える。</summary>
    private int ColumnPaddingValue => Scaled(4);

    /// <summary>項目を描ける領域（見出しとスクロールバーを除く）。</summary>
    private int ViewportWidth => Math.Max(0, ClientSize.Width - (_layout.ScrollBars.Vertical ? _vScrollBar.Width : 0));
    private int ViewportHeight => Math.Max(0, ClientSize.Height - _layout.HeaderHeight - (_layout.ScrollBars.Horizontal ? _hScrollBar.Height : 0));

    private void RebuildFontResources()
    {
        _content = null;   // フォント・dpi が変わると幅が変わる
        _nameLines.Clear();
        _font?.Dispose();
        _measure?.Dispose();
        _icons?.Dispose();
        _bigIcons?.Dispose();
        _bigIcons = null;
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
        var imagesBefore = ImageSettingsKey();
        (_mode, _views, _columnWidths, _sortOrder) = (mode, views, columnWidths, sortOrder);
        if (modeChanged) { _scroll = default; _wheel.Reset(); }
        RecomputeLayout();
        EnsureCursorVisible();
        // R-117 / R-118: 変わっていなくても、レイアウトが変わって見えている範囲が変わりうるので出し直す
        if (ImageSettingsKey() != imagesBefore) NextImageGeneration(); else UpdateImageQueue();
        Invalidate();
    }

    /// <summary>モードそのものは含めない（一覧と詳細の切り替えで印を捨てて描き直さない）。大きさと、サムネイル・印を求めるかだけで決まる。</summary>
    private (int, bool, bool, bool) ImageSettingsKey() =>
        (IconSizeFor(_mode), ThumbnailsOn, _views.Icons.FolderThumbnails, _views.Common.ShowOverlays);

    private void RecomputeLayout()
    {
        // R-120: 押した点の中身の座標は、新しいレイアウトでは別の項目を指す。離したときに古い矩形で新しい配置を囲まない
        CancelLasso();
        _nameLines.Clear();   // 名前の行は幅・行数・フォントで変わる
        if (IsIconMode)
        {
            if (_mode != FileViewMode.SmallIcons && _bigIcons?.Size != IconSizeFor(_mode))
            {
                _bigIcons?.Dispose();
                _bigIcons = new ShellIcons(IconSizeFor(_mode));
            }
            _layout = ComputeGrid();
            UpdateScrollBars();
            return;
        }
        if (_mode == FileViewMode.Details) { _layout = ComputeDetails(); UpdateScrollBars(); return; }
        var textStart = ColumnPaddingValue + _icons.Size + Gap;
        // R-113: 「自動」はパネルの幅（名前の文字の外側を引いたもの）、「最大文字数」は数字 0 の幅 × 文字数（Q9）
        var cap = NameWidths.TextCap(_views.List.NameWidth, _measure.Width("0"), ClientSize.Width - textStart - ColumnPaddingValue);
        _layout = EntryMetrics.Layout(_state.Entries, _measure, ClientSize.Width, ClientSize.Height, _hScrollBar.Height,
            _icons.Size, Gap, RowPadding, ColumnPaddingValue, cap, AlignExtension, HidesExtension);
        UpdateScrollBars();
    }

    /// <summary>R-119 / R-113 / Q6: 格子のレイアウト。小アイコンは名前の列の方式で項目の幅を決め、中〜特大は max(アイコン, 数字 0 × 9)。</summary>
    private GridLayout ComputeGrid()
    {
        var small = _mode == FileViewMode.SmallIcons;
        var iconSize = IconSizeFor(_mode);
        var zero = _measure.Width("0");
        int textWidth;
        if (small)
        {
            // R-113: 最長の名前は一覧・フォント・dpi・設定が変わったときだけ測る（届いた結果では測り直さない）
            var longest = EnsureContent().MaxName;
            var outside = Scaled(4) * 3 + iconSize + GridGap * 2;
            var cap = NameWidths.TextCap(_views.Icons.SmallIconWidth, zero, ClientSize.Width - outside);
            textWidth = cap is { } c ? Math.Min(longest, c) : longest;
        }
        else textWidth = Math.Max(iconSize, zero * 9);
        return GridLayout.Compute(new GridLayoutInput
        {
            EntryCount = _state.Count, Arrangement = small ? GridArrangement.IconLeft : GridArrangement.IconTop,
            IconSize = iconSize, LineHeight = _measure.LineHeight(), NameLines = small ? 1 : _views.Icons.NameLines,
            TextWidth = textWidth, PaddingX = Scaled(small ? 4 : 6), PaddingY = Scaled(small ? 2 : 4), Gap = GridGap,
            CheckBoxSize = small ? 0 : CheckBoxSize,
            ClientWidth = ClientSize.Width, ClientHeight = ClientSize.Height, VerticalBarWidth = _vScrollBar.Width, EdgeBand = EdgeBand,
        });
    }

    /// <summary>列の最小幅。見出しの文字と左右の余白（ドラッグでもこれより狭くしない）。ソートの印は文字の上に描くので幅に入れない（Q36）。</summary>
    private int MinColumnWidth(DetailsColumn? column) =>
        _measure.Width(DetailsCells.Header(column)) + ColumnPaddingValue * 2;

    /// <summary>R-114 / R-113 / V6: 列ごとに中身の最長と見出しの最小幅を測り、手動の幅（96 dpi の論理値）を dpi で拡大して渡す。</summary>
    private DetailsLayout ComputeDetails()
    {
        var content = EnsureContent();
        var details = _views.Details;
        var pad = ColumnPaddingValue;
        var showExtension = AlignExtension;
        var (maxBase, maxExt) = (content.MaxBase, content.MaxExt);
        var nameTextStart = pad + _icons.Size + Gap;
        var naturalText = maxBase + (maxExt > 0 ? Gap + maxExt : 0);
        // R-113: 詳細表示の「自動」は名前の列だけでパネルの幅を超えない（Q14）
        var cap = NameWidths.TextCap(details.NameWidth, _measure.Width("0"), ClientSize.Width - nameTextStart - pad);
        var text = cap is { } c && naturalText > c ? c : naturalText;
        var baseWidth = text == naturalText ? maxBase : Math.Max(0, text - Gap - maxExt);

        var columns = new List<DetailsColumnInput> { Column(null, nameTextStart + text + pad) };
        foreach (var setting in details.Columns.Where(c => c.Visible))
        {
            columns.Add(Column(setting.Column, content.Auto[setting.Column] + pad * 2));
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

    /// <summary>
    /// R-114: 項目の全走査で測る中身の幅。項目・フォント・dpi・列と拡張子の設定が変わるまで使い回す
    /// （リサイズ・スクロール・列幅のドラッグ・ソートの印では走査しない）。種類名が届いたときは、その 1 件だけを測って差し替える。
    /// </summary>
    private sealed class ContentMeasure
    {
        public required string Signature { get; init; }
        public required int MaxBase { get; init; }
        public required int MaxExt { get; init; }
        /// <summary>小アイコンの項目の幅の元。続けて描く名前（FullNameText）の最長。</summary>
        public required int MaxName { get; init; }
        public required Dictionary<DetailsColumn, int> Auto { get; init; }
        /// <summary>種類の鍵ごとの代表の項目。届いた鍵が今の一覧にあるかを O(1) で引く。</summary>
        public required Dictionary<string, Entry> TypeRepresentatives { get; init; }
    }

    private ContentMeasure? _content;

    /// <summary>テスト用: 項目の全走査（中身の幅の測り直し）をした回数。</summary>
    internal int ContentScanCount { get; private set; }

    /// <summary>テスト用: 種類の列の文字を差し替える（既定は DetailsCells の背景取得つきの答え）。</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<Entry, string> TypeText { get; set; } = e => DetailsCells.Text(e, DetailsColumn.Type);

    private string CellText(Entry entry, DetailsColumn column) => column == DetailsColumn.Type ? TypeText(entry) : DetailsCells.Text(entry, column);

    /// <summary>詳細表示のときだけ列を測る（ほかのモードで種類の列の背景取得を起こさない）。</summary>
    private IEnumerable<DetailsColumn> VisibleColumns() =>
        _mode == FileViewMode.Details ? _views.Details.Columns.Where(c => c.Visible).Select(c => c.Column) : [];

    private string ContentSignature() =>
        string.Join(',', VisibleColumns()) + $"|{AlignExtension}|{_views.Common.HideKnownExtensions}|{_mode == FileViewMode.SmallIcons}";

    private ContentMeasure EnsureContent()
    {
        var signature = ContentSignature();
        if (_content is { } cached && cached.Signature == signature) return cached;
        ContentScanCount++;
        var showExtension = AlignExtension;
        var (maxBase, maxExt, maxName) = (0, 0, 0);
        var visible = VisibleColumns().ToList();
        var auto = visible.ToDictionary(c => c, _ => 0);
        var representatives = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _state.Entries)
        {
            maxBase = Math.Max(maxBase, _measure.Width(NameText(entry)));
            if (_mode == FileViewMode.SmallIcons) maxName = Math.Max(maxName, _measure.Width(FullNameText(entry)));
            if (showExtension && !HidesExtension(entry)) maxExt = Math.Max(maxExt, _measure.Width(entry.Extension));
            foreach (var column in visible) auto[column] = Math.Max(auto[column], _measure.Width(CellText(entry, column)));
            // 親フォルダの行は種類を持たない（空の文字）。拡張子の無いファイルと同じ空の鍵になるので、代表にしない
            if (!entry.IsParent) representatives.TryAdd(ShellFileType.KeyOf(entry.FullPath, entry.Kind == EntryKind.Folder), entry);
        }
        return _content = new ContentMeasure
        {
            Signature = signature, MaxBase = maxBase, MaxExt = maxExt, MaxName = maxName, Auto = auto, TypeRepresentatives = representatives,
        };
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

    /// <summary>R-114: 背景で届いた種類の鍵。UI スレッドで反映するまでためる（作業スレッドから積まれる）。</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _resolvedKeys = new();
    private int _flushScheduled;
    /// <summary>
    /// R-114: 背景で届いた種類（作業スレッドから呼ばれる）。破棄済み・ハンドルが無いなら何もしない。
    /// 反映の予約は 1 つだけにして、その間に届いた分はまとめて 1 回で測り直す（拡張子 U 種類 × 項目 N 件の全走査を通知ごとにしない）。
    /// </summary>
    private void OnTypeResolved(string key)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (!QueueResolvedType(key)) return;
        try { BeginInvoke(FlushResolvedTypes); }
        catch (InvalidOperationException) { Volatile.Write(ref _flushScheduled, 0); }   // 閉じる途中でハンドルが消えた
    }

    /// <summary>鍵をためる。反映の予約がまだ無く、これから予約するなら true。</summary>
    internal bool QueueResolvedType(string key)
    {
        _resolvedKeys[key] = 0;
        return Interlocked.Exchange(ref _flushScheduled, 1) == 0;
    }

    /// <summary>
    /// UI スレッドでの反映。項目は走査しない: ためた鍵のうち今の一覧にあるものだけ、その種類名を 1 回測り、
    /// 種類の列の最長より広いときだけ列幅の合計を組み直す（R-114）。詳細表示でないあいだに届いた分は、次に開いたとき測り直す。
    /// </summary>
    internal void FlushResolvedTypes()
    {
        Volatile.Write(ref _flushScheduled, 0);
        var keys = new List<string>();
        foreach (var key in _resolvedKeys.Keys) if (_resolvedKeys.TryRemove(key, out _)) keys.Add(key);
        if (IsDisposed || keys.Count == 0) return;
        if (_mode != FileViewMode.Details) { _content = null; return; }
        if (_content is not { } content) return;   // 次の RecomputeLayout が全部測る
        var changed = false;
        var present = false;
        foreach (var key in keys)
        {
            if (!content.TypeRepresentatives.TryGetValue(key, out var entry)) continue;
            present = true;
            if (!content.Auto.TryGetValue(DetailsColumn.Type, out var current)) continue;   // 種類の列が非表示
            var width = _measure.Width(TypeText(entry));
            if (width > current) { content.Auto[DetailsColumn.Type] = width; changed = true; }
        }
        if (changed)
        {
            _layout = ComputeDetails();
            UpdateScrollBars();
            EnsureCursorVisible();   // 横のバーが出て表示の高さが 1 行減ると、最下行のカーソルが隠れる
        }
        if (present) Invalidate();
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
        // 部分の描き直し（届いた項目 1 つ・ホバー）では、クリップに交わる項目だけ描く。カーソルの項目は右へ帯を広げうるので毎回描く
        foreach (var index in visible.Where(i => i != _state.CursorIndex && IntersectsClip(e.ClipRectangle, i))) DrawRow(e.Graphics, index);
        if (visible.Contains(_state.CursorIndex)) DrawRow(e.Graphics, _state.CursorIndex);
        if (_lasso.Active is not null)   // R-120: 項目の後に、枠と半透明の塗りを重ねる
        {
            var r = ToRectangle(FileViewScroll.ToVisible(_layout, _scroll, LassoRect));
            var (stroke, fill) = ItemFrames.LassoColors(_theme);
            using (var brush = new SolidBrush(fill)) e.Graphics.FillRectangle(brush, r);
            using var pen = new Pen(stroke);
            e.Graphics.DrawRectangle(pen, r.X, r.Y, Math.Max(0, r.Width - 1), Math.Max(0, r.Height - 1));
        }
        e.Graphics.ResetClip();
        // R-117: 見えている範囲が変わったら要求を出し直す（スクロール・リサイズの入口をここ 1 か所にする）
        var range = visible.Count > 0 ? (visible.Min(), visible.Max()) : (-1, -1);
        if (range != _imageRange) { _imageRange = range; UpdateImageQueue(); }
    }

    private bool IntersectsClip(Rectangle clip, int index) => ToRectangle(FileViewScroll.VisibleBounds(_layout, _scroll, index)).IntersectsWith(clip);

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
        var isMarked = IsMarkedForDisplay(index);
        var rect = ToRectangle(FileViewScroll.VisibleBounds(_layout, _scroll, index));
        if (IsIconMode) { DrawGridItem(g, index); return; }

        var (background, foreground) = RowColors.Of(_theme, AttributeColorRule.Classify(entry.Attributes), isCursor, isMarked);
        // Q12 / R-113: 一覧のカーソルの項目が省略されていたら、帯を名前の終わりまで右へ広げて右隣の上に重ねる
        var full = DrawsFullName(index);
        var band = full ? rect with { Width = Math.Max(rect.Width, FullNameWidth(entry)) } : rect;
        using (var brush = new SolidBrush(background)) g.FillRectangle(brush, band);

        var iconRect = ToRectangle(FileViewScroll.ToVisible(_layout, _scroll, _layout.IconBounds(index)));
        var imageRect = iconRect with { Y = rect.Y + (rect.Height - _icons.Size) / 2, Width = _icons.Size, Height = _icons.Size };
        DrawItemImage(g, entry, imageRect);
        DrawOverlay(g, entry, imageRect);
        if (isMarked) MarkStar.Draw(g, iconRect, _theme.MarkStarColor);   // R-11-5

        DrawName(g, index, entry, foreground, full, band);

        if (_mode == FileViewMode.Details)   // R-114: 名前以外のセル
        {
            // INV-LAYOUT-GEOMETRY-SINGLE-SOURCE: セルの矩形はレイアウトに聞く
            foreach (var cell in _layout.Header)
                if (cell.Column is { } column && _layout.CellBounds(index, column) is { } bounds)
                    DrawCell(g, DetailsCells.Text(entry, column), ToRectangle(FileViewScroll.ToVisible(_layout, _scroll, bounds)),
                        foreground, DetailsCells.RightAligned(column));
        }

        if (index == _dropTarget)   // R-110-2
        {
            using var pen = new Pen(RowColors.Frame(background, foreground), Scaled(2)) { Alignment = PenAlignment.Inset };
            g.DrawRectangle(pen, DropFrameBounds(index));
        }
    }

    /// <summary>R-116 / R-117 / R-118: 格子の項目の描く順（下から）。仕様の重ね順で、テストで固定する。</summary>
    internal static readonly IReadOnlyList<GridLayer> GridLayers =
        [GridLayer.Fill, GridLayer.Image, GridLayer.Overlay, GridLayer.CheckBox, GridLayer.CursorFrame, GridLayer.DropFrame];

    /// <summary>
    /// INV-LAYOUT-GEOMETRY-SINGLE-SOURCE: 落とす先の枠は項目の矩形（ItemBounds）。
    /// カーソルの枠は、中〜特大で名前を項目の下へはみ出して描くときだけ、はみ出した帯まで囲む
    /// （項目の矩形に描くと、枠の下辺が帯を横切って線に見えた）。塗りと枠と描き直しの範囲は、この 1 か所の答えを使う。
    /// </summary>
    internal Rectangle CursorFrameBounds(int index)
    {
        var item = DropFrameBounds(index);
        if (_layout is not GridLayout { Arrangement: GridArrangement.IconTop }) return item;
        var nameRect = ToRectangle(FileViewScroll.ToVisible(_layout, _scroll, _layout.NameBounds(index)));
        return NameOverflowBand(item, nameRect, NameLinesFor(index).Lines.Count, _measure.LineHeight(), Scaled(4));
    }

    internal Rectangle DropFrameBounds(int index) => ToRectangle(FileViewScroll.VisibleBounds(_layout, _scroll, index));

    /// <summary>R-119: 名前の行が名前の領域に収まらないとき、項目と（はみ出した名前の）矩形の和。収まるなら項目のまま。</summary>
    internal static Rectangle NameOverflowBand(Rectangle item, Rectangle nameRect, int lineCount, int lineHeight, int padding) =>
        lineCount * lineHeight > nameRect.Height
            ? Rectangle.Union(item, nameRect with { Height = lineCount * lineHeight + padding })
            : item;

    /// <summary>R-119 / R-113: 小アイコンで描く 1 行。カーソルの項目で省略されていれば全部（一覧と同じく右へ帯を広げる）。</summary>
    internal string SmallIconNameText(int index) =>
        DrawsFullName(index) ? FullNameText(_state.Entries[index]) : NameLinesFor(index).Lines.FirstOrDefault() ?? "";

    /// <summary>R-120: 投げ縄の間は、囲んだ項目を仮のマークで描く（マークの集合は変えない）。</summary>
    internal bool IsMarkedForDisplay(int index)
    {
        var marked = _state.Marks.Contains(index);
        if (_lasso.Active is not { } lasso || _state.Entries[index].IsParent) return marked;   // 「..」は仮のマークにもしない
        _lassoCovered ??= lasso.Covered(_layout, _state.Count).ToHashSet();
        return _lassoCovered.Contains(index) ? !lasso.Remove : marked;
    }

    // ---- 投げ縄（R-120） -----------------------------------------------------

    private readonly LassoGesture _lasso = new();
    private Point _lassoMouse;             // 自動スクロールの間も最後のマウスの位置で矩形を伸ばす
    private HashSet<int>? _lassoCovered;   // 仮のマークの対象。矩形・スクロールが変わったら作り直す

    internal bool LassoActive => _lasso.Active is not null;
    internal (int X, int Y, int Width, int Height) LassoRect => _lasso.Active?.Rect ?? default;

    private (int X, int Y) ToContent(Point p)
    {
        var (ox, oy) = _layout.ScrollOffset(_scroll);
        return (p.X + ox, p.Y - _layout.HeaderHeight + oy);
    }

    /// <summary>R-120: 項目の無い所（詳細表示では名前以外も。INV-DETAILS-ROW-HIT）で押したら、閾値を超えたときに投げ縄を始める。</summary>
    internal void LassoPress(Point location, bool ctrl)
    {
        var (index, area) = HitAt(location);
        // Phase 16 §10: 範囲選択がオフの系統では始めない。押した所が項目の余白・名前以外なら、動かしたときに保留のカーソル移動も捨てる（OnMouseMove）
        var starts = RangeSelection && location.Y >= _layout.HeaderHeight && (index < 0 || area == FileViewArea.Other);
        var (x, y) = ToContent(location);
        _lasso.Press(location.X, location.Y, x, y, starts, ctrl);
        _lassoCovered = null;
    }

    internal void LassoMove(Point location)
    {
        var wasActive = LassoActive;
        var (x, y) = ToContent(location);
        if (!_lasso.Move(location.X, location.Y, x, y, SystemInformation.DragSize.Width, SystemInformation.DragSize.Height)) return;
        if (!wasActive) _markOnRelease.Moved();   // 詳細表示の名前以外で押していたら、保留したカーソルの移動を捨てる
        _lassoMouse = location;
        _lassoCovered = null;
        var direction = _layout.AutoScrollDirection(location.X, location.Y - _layout.HeaderHeight, ViewportWidth, ViewportHeight);
        SetAutoScroll(direction, LassoScrollInterval(direction));
        Invalidate();
    }

    internal void LassoRelease()
    {
        var active = LassoActive;
        var before = _state.CursorIndex;
        var changed = _lasso.Release(_state, _layout);
        EndLassoCommon();
        if (active) Commit(before, changed);
    }

    /// <summary>R-120: Esc・右ボタン・フォーカスの喪失・キャプチャの喪失・一覧の入れ替わり。マークは変えない。</summary>
    private void CancelLasso()
    {
        if (!LassoActive) { _lasso.Cancel(); return; }
        _lasso.Cancel();
        EndLassoCommon();
    }

    private void EndLassoCommon()
    {
        _lassoCovered = null;
        SetAutoScroll((0, 0));
        if (IsHandleCreated && Capture) Capture = false;   // 状態を消してから放す（CaptureChanged が来ても二重に動かない）
        Invalidate();
    }

    internal void LoseFocus() => CancelLasso();
    internal void CaptureLost() => CancelLasso();

    internal void RaiseMouseDown(MouseEventArgs e) => OnMouseDown(e);
    internal void RaiseMouseMove(MouseEventArgs e) => OnMouseMove(e);
    internal void RaiseMouseUp(MouseEventArgs e) => OnMouseUp(e);
    internal void RaiseLostFocus() => OnLostFocus(EventArgs.Empty);
    internal void RaiseCaptureChanged() => OnMouseCaptureChanged(EventArgs.Empty);

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        LoseFocus();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture) CaptureLost();
    }

    /// <summary>
    /// R-116 / R-119: 格子の項目。GridLayers の順に描く。矩形はすべてレイアウトに聞く（INV-LAYOUT-GEOMETRY-SINGLE-SOURCE）。
    /// 塗り・名前・カーソルの枠は帯（カーソルの項目の名前を全部描くときは項目の外へ広がる）に、落とす先の枠は項目の矩形に描く。
    /// </summary>
    private void DrawGridItem(Graphics g, int index)
    {
        var entry = _state.Entries[index];
        var isCursor = index == _state.CursorIndex;
        var isMarked = IsMarkedForDisplay(index);   // 投げ縄の仮のマークを含む
        var small = _mode == FileViewMode.SmallIcons;
        var item = DropFrameBounds(index);
        var (background, foreground) = RowColors.Of(_theme, AttributeColorRule.Classify(entry.Attributes), isCursor, isMarked);
        var nameRect = ToRectangle(FileViewScroll.ToVisible(_layout, _scroll, _layout.NameBounds(index)));
        var iconRect = ToRectangle(FileViewScroll.ToVisible(_layout, _scroll, _layout.IconBounds(index)));
        var lineHeight = _measure.LineHeight();
        // R-119: 中〜特大のカーソルの項目は、名前の全部の行を項目の下へはみ出して描く。小アイコンは一覧と同じく右へ広げる
        IReadOnlyList<string> names = small ? [SmallIconNameText(index)] : NameLinesFor(index).Lines;
        var band = small
            ? DrawsFullName(index) ? item with { Width = Math.Max(item.Width, FullNameWidth(entry)) } : item
            : NameOverflowBand(item, nameRect, names.Count, lineHeight, Scaled(4));

        foreach (var layer in GridLayers)
            switch (layer)
            {
                case GridLayer.Fill:
                    using (var brush = new SolidBrush(background)) g.FillRectangle(brush, band);
                    var y = nameRect.Y + (small ? (nameRect.Height - lineHeight) / 2 : 0);
                    var textWidth = small ? band.Right - nameRect.X : nameRect.Width;
                    var flags = TextMeasure.Flags | (small ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter);
                    foreach (var line in names)
                    {
                        TextRenderer.DrawText(g, line, _font, new Rectangle(nameRect.X, y, textWidth, lineHeight), foreground, flags);
                        y += lineHeight;
                    }
                    break;
                case GridLayer.Image:
                    DrawItemImage(g, entry, iconRect);
                    if (small && isMarked) MarkStar.Draw(g, iconRect, _theme.MarkStarColor);   // R-11-5
                    break;
                case GridLayer.Overlay:
                    DrawOverlay(g, entry, iconRect);
                    break;
                case GridLayer.CheckBox:
                    if (ShowsCheckBox(index) && _layout.CheckBoxBounds(index) is { } box)
                        ItemFrames.DrawCheckBox(g, ToRectangle(FileViewScroll.ToVisible(_layout, _scroll, box)), background, foreground,
                            isChecked: isMarked, stroke: Math.Max(1, Scaled(1)));
                    break;
                case GridLayer.CursorFrame:
                    if (isCursor && !small) ItemFrames.DrawCursorFrame(g, band, background, foreground, Math.Max(1, Scaled(1)));
                    break;
                case GridLayer.DropFrame:
                    if (index == _dropTarget)   // R-110-2
                    {
                        using var pen = new Pen(RowColors.Frame(background, foreground), Scaled(2)) { Alignment = PenAlignment.Inset };
                        g.DrawRectangle(pen, DropFrameBounds(index));
                    }
                    break;
            }
    }

    /// <summary>
    /// R-117 / R-118: ② サムネイル（今の設定で使うものがあれば。無ければアイコン）。サムネイルは縦横比を保って矩形の中央に縮める。
    /// 一覧・詳細の行頭のアイコンも通る（サムネイルは中〜特大だけ。ThumbnailFor が null を返す）。
    /// </summary>
    private void DrawItemImage(Graphics g, Entry entry, Rectangle iconRect)
    {
        var size = iconRect.Width;
        if (ThumbnailFor(entry) is { } thumbnail)
        {
            var scale = Math.Min((float)size / thumbnail.Width, (float)size / thumbnail.Height);
            var (w, h) = ((int)(thumbnail.Width * scale), (int)(thumbnail.Height * scale));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(thumbnail, iconRect.X + (size - w) / 2, iconRect.Y + (size - h) / 2, w, h);
            return;
        }
        // 中〜特大の大きさの取り口は、そのモードのときだけ使う（一覧・詳細へ切り替えても _bigIcons は残る）
        var icons = IsIconMode && _mode != FileViewMode.SmallIcons && _bigIcons is not null ? _bigIcons : _icons;
        var icon = entry.Kind == EntryKind.File ? icons.ForFile(entry.FullPath) : icons.ForFolder();
        if (icon is not null) g.DrawImage(icon, iconRect);
    }

    /// <summary>R-118: ③ OS の印。印の位置はシェルの絵のとおり（絵の中の左下）なので、アイコンの矩形にそのまま重ねる。GridLayers の順（アイコンの後・チェックボックスの前）で呼ぶ。</summary>
    private void DrawOverlay(Graphics g, Entry entry, Rectangle iconRect)
    {
        var size = iconRect.Width;
        if (_views.Common.ShowOverlays && _overlays.TryGetValue(entry.FullPath, out var overlay) && ShellOverlays.Image(overlay, size) is { } mark)
            g.DrawImage(mark, iconRect with { Width = size, Height = size });
    }

    /// <summary>
    /// R-119: 格子の名前の行。小アイコン・カーソルの項目以外の中〜特大は設定の行数まで。カーソルの項目は全部。拡張子は揃えず続けて描き（Q35）、
    /// 隠した拡張子（R-01-7）は出さない。NameWrap は長い名前で何百回も測るので、項目ごとに覚える
    /// （一覧・フォント・dpi・レイアウト・設定が変わったときに捨てる。カーソルの項目は行数が違う別の鍵）。
    /// </summary>
    internal NameWrap.Result NameLinesFor(int index)
    {
        var small = _mode == FileViewMode.SmallIcons;
        var lines = index == _state.CursorIndex && !small ? int.MaxValue : small ? 1 : _views.Icons.NameLines;
        var width = _layout.NameBounds(index).Width;
        var entry = _state.Entries[index];
        var hides = HidesExtension(entry);
        var key = (index, width, lines, hides);
        if (_nameLines.TryGetValue(key, out var cached)) return cached;
        var (body, tail) = hides || entry.Extension.Length == 0 ? (FullNameText(entry), "") : (entry.BaseName, entry.Extension);
        return _nameLines[key] = NameWrap.Lines(body, tail, width, lines, _measure.Width);
    }

    private readonly Dictionary<(int Index, int Width, int Lines, bool Hidden), NameWrap.Result> _nameLines = [];

    /// <summary>R-116: ホバー中の項目（MouseMove で更新。項目の外・離れたら -1）。</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal int HotIndex { get; set; } = -1;

    /// <summary>R-116 / Q1: 「ホバー中とマーク済み」なら、マウスが乗っている項目とマーク済みの項目だけ。「常に表示」なら全項目。</summary>
    internal bool ShowsCheckBox(int index) =>
        _layout.CheckBoxBounds(index) is not null
        && !_state.Entries[index].IsParent   // 「..」はマークできないので、チェックボックスも出さない
        && (_views.Icons.CheckBoxes == CheckBoxMode.Always || index == HotIndex || _state.Marks.Contains(index));

    /// <summary>今のマウスの位置でホバーを付け直す。ハンドルが無ければ位置が分からないので外す。</summary>
    internal void RefreshHot() =>
        RefreshHot(IsHandleCreated && PointToClient(Cursor.Position) is var p && ClientRectangle.Contains(p) ? p : new Point(-1, -1));

    /// <summary>スクロール・一覧の入れ替えのあとに、クライアント座標の点の下の項目をホバーにする。</summary>
    internal void RefreshHot(Point clientPoint) => SetHot(clientPoint.X < 0 ? -1 : HitAt(clientPoint).Index);

    /// <summary>ホバーの項目を変え、古い項目と新しい項目の見えている矩形だけ描き直す。</summary>
    private void SetHot(int index)
    {
        if (index == HotIndex) return;
        var old = HotIndex;
        HotIndex = index;
        if (_layout.CheckBoxBounds(0) is null) return;   // チェックボックスの無いモードは描き直さない
        foreach (var i in new[] { old, index })
            if (i >= 0 && i < _state.Count) Invalidate(CursorFrameBounds(i));
    }

    internal void PressKey(Keys key) => OnKeyDown(new KeyEventArgs(key));

    /// <summary>
    /// R-01-4 / R-01-6 / R-113: 本体は名前の領域の左から、拡張子は揃えた位置から。収まらなければ本体の末尾を「…」で省略し、
    /// 拡張子は残す（拡張子そのものが入らなければ拡張子も「…」）。全部描く（full）ときは揃えから外して続けて描く（R-01-6 の例外）。
    /// ponytail: 続けて描く名前以外の省略は GDI の EndEllipsis 任せ（切る位置は GDI が決める）。崩れる例が見つかったら、CutAtGrapheme で自前に切る形に広げる。
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
    private bool AlignExtension => _mode switch
    {
        FileViewMode.Details => _views.Details.AlignExtension,
        FileViewMode.List => _views.List.AlignExtension,
        _ => false,   // Q35: 格子は揃えない
    };

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
        return CutAtGrapheme(entry.BaseName, tail, width, _measure.Width);
    }

    /// <summary>
    /// R-113: 省略は文字の途中で切らない（サロゲートペア・異体字セレクタ・結合文字を書記素の単位で残す）。
    /// UTF-16 の長さで 1 つずつ削ると、対の片方だけが残って壊れた文字になる。
    /// </summary>
    internal static string CutAtGrapheme(string text, string tail, int width, Func<string, int> measure)
    {
        var starts = System.Globalization.StringInfo.ParseCombiningCharacters(text);
        var count = starts.Length;
        while (count > 0 && measure(text[..(count < starts.Length ? starts[count] : text.Length)] + tail) > width) count--;
        return text[..(count < starts.Length ? starts[count] : text.Length)] + tail;
    }

    /// <summary>R-113: 省略して描いているか。本体の実測が本体の領域より広いか、拡張子の実測が拡張子の領域より広いとき。</summary>
    internal bool IsTruncated(int index)
    {
        if (index < 0 || index >= _state.Count) return false;
        var entry = _state.Entries[index];
        // R-119: 格子は名前の折り返しの行数で決める（中〜特大はカーソルかどうかに依らず設定の行数、小アイコンは 1 行）
        if (IsIconMode)
        {
            if (_mode == FileViewMode.SmallIcons || index != _state.CursorIndex) return NameLinesFor(index).Truncated;   // 覚えた答えと同じ行数
            var (body, tail) = HidesExtension(entry) || entry.Extension.Length == 0 ? (FullNameText(entry), "") : (entry.BaseName, entry.Extension);
            return NameWrap.Lines(body, tail, _layout.NameBounds(index).Width, _views.Icons.NameLines, _measure.Width).Truncated;
        }
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
    internal bool DrawsFullName(int index) =>
        index == _state.CursorIndex && _mode is FileViewMode.List or FileViewMode.SmallIcons && IsTruncated(index);

    /// <summary>カーソルの項目を全部描くときの帯の幅（左の余白・アイコン・間・名前・右の余白）。</summary>
    private int FullNameWidth(Entry entry) => ColumnPaddingValue + _icons.Size + Gap + _measure.Width(FullNameText(entry)) + ColumnPaddingValue;

    private static Rectangle ToRectangle((int X, int Y, int Width, int Height) r) => new(r.X, r.Y, r.Width, r.Height);

    /// <summary>見えている座標の点の、項目と押した所の種類。見出しの上なら (-1, None)。</summary>
    private (int Index, FileViewArea Area) HitAt(Point point)
    {
        if (point.Y < _layout.HeaderHeight) return (-1, FileViewArea.None);
        var offset = _layout.ScrollOffset(_scroll);
        var (x, y) = (point.X + offset.X, point.Y - _layout.HeaderHeight + offset.Y);
        var hit = _layout.HitTest(x, y, _state.Count);
        // R-120: 中〜特大の項目は、アイコン・描いている名前の文字・チェックボックスに当たらない所を「項目の余白」（Other）にする。
        // 詳細表示の名前以外と同じ扱い（押しただけでは動かず、離すとカーソル、動かすと投げ縄）。文字の幅はここで測る（レイアウトは数値だけ受ける）
        if (hit.Area == FileViewArea.Name && _layout is GridLayout { Arrangement: GridArrangement.IconTop } grid
            && !Contains(grid.IconBounds(hit.Index), x, y)
            && !grid.NameTextBounds(hit.Index, NameLinesFor(hit.Index).Lines.Select(_measure.Width).ToList(), _measure.LineHeight())
                .Any(line => Contains(line, x, y)))
            return (hit.Index, FileViewArea.Other);
        return hit;
    }

    private static bool Contains((int X, int Y, int Width, int Height) r, int x, int y) =>
        x >= r.X && x < r.X + r.Width && y >= r.Y && y < r.Y + r.Height;

    // ---- 固定キー -----------------------------------------------------------

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) switch
    {
        Keys.Escape when LassoActive => true,   // R-120: フォームの既定の処理に取られない
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
        if (e.KeyCode == Keys.Escape && LassoActive) { CancelLasso(); e.Handled = true; return; }   // R-120
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
            var border = _layout.HeaderBorderAt(x, Scaled(4));
            _rightDown = null;
            _headerRightDown = e.Button == MouseButtons.Right;
            if (e.Button != MouseButtons.Left) return;
            if (border >= 0) _headerDrag = (border, e.X, _layout.Header[border].Width);   // 幅のドラッグを始める
            else { _headerPress = _layout.HeaderCellAt(x); InvalidateHeader(); }   // 離した時点でソート
            return;
        }

        var index = HitAt(e.Location).Index;
        _headerRightDown = false;

        if (e.Button == MouseButtons.Right)
        {
            PressRight(e.Location);
            return;
        }
        if (e.Button == MouseButtons.Left) LassoPress(e.Location, ModifierKeys.HasFlag(Keys.Control));
        if (e.Button != MouseButtons.Left || index < 0) return;

        PressLeft(e.Location, ModifierKeys.HasFlag(Keys.Shift));
    }

    /// <summary>右ボタンを押した処理。投げ縄の途中なら取り消す（R-120）。</summary>
    internal void PressRight(Point location)
    {
        CancelLasso();
        _rightDown = (HitAt(location).Index, location, ModifierKeys.HasFlag(Keys.Shift));
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
                     && _layout.HeaderBorderAt(e.X + _layout.ScrollOffset(_scroll).X, Scaled(4)) < 0
            ? _layout.HeaderCellAt(e.X + _layout.ScrollOffset(_scroll).X) : -1);

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
        if (e.Button == MouseButtons.Left)
        {
            LassoMove(e.Location);   // R-120: 投げ縄の間は D&D・ツールチップに進まない
            if (LassoActive) return;
        }
        if (e.Button == MouseButtons.None && _layout.HeaderHeight > 0)
        {
            var onBorder = e.Y < _layout.HeaderHeight
                           && _layout.HeaderBorderAt(e.X + _layout.ScrollOffset(_scroll).X, Scaled(4)) >= 0;
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

        if (e.Button == MouseButtons.None)
        {
            SetHot(HitAt(e.Location).Index);   // R-116
            UpdateNameTip(e.Location);
        }
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
        SetHot(-1);
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

    /// <summary>
    /// D&amp;D は一定（R-110-3）。投げ縄は端からの深さで間隔を変える（Phase 16 §10）ので、始めるときに interval を渡す。
    /// 向きが同じ間は Interval に触れない（動かすたびに触ると、Timer が数えなおして永久に鳴らない）。途中の変更は Tick の終わりで行う。
    /// </summary>
    internal void SetAutoScroll((int X, int Y) direction, int interval = AutoScrollInterval)
    {
        if (direction == _autoScrollDirection) return;
        _autoScrollDirection = direction;
        _autoScroll.Stop();
        if (direction == (0, 0)) return;
        _autoScroll.Interval = interval;
        _autoScroll.Start();
    }

    internal int AutoScrollTimerInterval => _autoScroll.Interval;

    /// <summary>Phase 16 §10: 最後のマウスの位置での投げ縄の自動スクロールの間隔。帯の幅はレイアウトが答える。</summary>
    private int LassoScrollInterval((int X, int Y) direction) => FileViewScroll.LassoInterval(
        _layout.AutoScrollBand, direction, _lassoMouse.X, _lassoMouse.Y - _layout.HeaderHeight, ViewportWidth, ViewportHeight);

    /// <summary>R-110-3: 1 段。向きも段の量もレイアウトが決める（今の一覧では横に 1 列）。スクロールできる端まで来たら止める。</summary>
    internal void AutoScrollTick()
    {
        var next = FileViewScroll.Next(_layout, _scroll, _autoScrollDirection, _state.Count, ViewportWidth, ViewportHeight);
        if (next == _scroll) { SetAutoScroll((0, 0)); return; }
        _scroll = next;
        UpdateScrollBars();
        AfterScroll();   // 枠の位置は次の DragOver で決め直す（OLE はマウスが止まっていても DragOver を呼び続ける）
        if (LassoActive) _autoScroll.Interval = LassoScrollInterval(_autoScrollDirection);   // 動いたマウスの深さを次の 1 段から反映する
    }

    /// <summary>
    /// スクロールしたあとの共通の後始末。R-120: 中身がずれたぶん、最後のマウスの位置から投げ縄の矩形を伸ばす
    /// （ホイール・スクロールバー・自動スクロールのどれでも。マウスは動かないので MouseMove は来ない）。R-116: 下にある項目も変わる。
    /// </summary>
    private void AfterScroll()
    {
        if (LassoActive)
        {
            var (cx, cy) = ToContent(_lassoMouse);
            _lasso.Scrolled(cx, cy);
            _lassoCovered = null;
        }
        RefreshHot();
        Invalidate();
    }

    /// <summary>スクロールバーのつまみを動かした処理。ハンドル無しで試せるように Scroll イベントから切り出した。</summary>
    internal void RaiseScrollBar(bool vertical, int value)
    {
        _scroll = vertical ? _scroll with { Y = value } : _scroll with { X = value };
        AfterScroll();
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
            var border = _layout.HeaderBorderAt(e.X + _layout.ScrollOffset(_scroll).X, Scaled(4));
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
                     && _layout.HeaderCellAt(e.X + _layout.ScrollOffset(_scroll).X) == press)
                HeaderClicked?.Invoke(this, _layout.Header[press].Column);
            return;
        }
        if (e.Button == MouseButtons.Right && _headerRightDown && e.Y < _layout.HeaderHeight)
        {
            _headerRightDown = false;
            // 見出しの上では項目のメニューではなく列のメニュー
            DetailsHeaderMenu(_layout.HeaderCellAt(e.X + _layout.ScrollOffset(_scroll).X)).Show(this, e.Location);
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
            if (LassoActive) { LassoRelease(); return; }
            _lasso.Cancel();   // 押しただけの保留を消す
            ReleaseLeft();
        }
    }

    /// <summary>左ボタンを離した処理（R-11-2）。テストから呼べるように OnMouseUp から切り出した（挙動は変えない）。</summary>
    internal void ReleaseLeft()
    {
        // R-11-2: Shift の範囲マークは、離した時点でカーソルも押した項目へ動く（MarkOnRelease.Release の中で）。
        // 動いたかどうかは離す前のカーソル位置と比べる必要があるので、Release より前に取っておく
        var before = _state.CursorIndex;
        var marksChanged = _markOnRelease.Release(_state);
        // INV-DETAILS-ROW-HIT: 名前以外はマークを変えずにカーソルだけ動く
        if (marksChanged || _state.CursorIndex != before) Commit(before, marksChanged);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        // V9: Ctrl+ホイールは表示モードの段（奥へ回すと上の段）。スクロールはしない
        if (ModifierKeys.HasFlag(Keys.Control))
        {
            ModeWheel(e.Delta);
            return;
        }
        // Ctrl を離したあとに、たまった分が次の Ctrl+ホイールへ持ち越されないようにする
        _modeWheel.Reset();
        var (horizontal, vertical) = _layout.ScrollBars;
        // スクロールできない間にたまった分が、後でまとめて効かないようにする
        if (!horizontal && !vertical) { _wheel.Reset(); return; }
        // R-76: 一覧は 1 ノッチ = 1 列（左端は常に列の境界に揃う）。詳細は縦のバーがあれば縦に MouseWheelScrollLines 行、無ければ横に 1 段
        ScrollWheel(_wheel.Add(e.Delta, SystemInformation.MouseWheelScrollDelta));
    }

    /// <summary>Ctrl+ホイールの処理。Ctrl は ModifierKeys（実際のキーボード）なので、テストが届けられるように切り出した。</summary>
    internal void ModeWheel(int delta)
    {
        var notches = _modeWheel.Add(delta, SystemInformation.MouseWheelScrollDelta);
        if (notches != 0) ViewModeWheel?.Invoke(this, notches);
    }

    /// <summary>ホイールのノッチ数（正で上・左へ）ぶん進める。1 ノッチの段数はレイアウトが答える（格子は 1 段が大きいので 1 行。INV-LAYOUT-GEOMETRY-SINGLE-SOURCE）。</summary>
    internal void ScrollWheel(int notches)
    {
        if (_layout.ScrollBars.Vertical)
            ScrollBy(0, -notches * _layout.WheelSteps(Math.Max(1, SystemInformation.MouseWheelScrollLines), ViewportWidth, ViewportHeight));
        else ScrollBy(-notches, 0);
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
        AfterScroll();
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
        NextImageGeneration();    // R-117: 大きさが変わるので取り直す
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _font?.Dispose();
            _measure?.Dispose();
            _icons?.Dispose();
            _bigIcons?.Dispose();
            ImageWorker?.Dispose();   // 待たない
            _thumbnails.Clear();
            _noThumbnail.Clear();
            _overlays.Clear();
            _autoScroll.Dispose();
            _nameTip.Dispose();
        }
        ShellFileType.Resolved -= OnTypeResolved;
        base.Dispose(disposing);
    }
}
