using System.Drawing;
using ReTAC.App.Rendering;
using System.IO;
using System.Windows.Forms;
using ReTAC.Domain.Entries;
using ReTAC.Domain.Formatting;
using ReTAC.Domain.Selection;
using ReTAC.Shell;

namespace ReTAC.App;

/// <summary>
/// R-34: 4 区画（① ドライブ容量 ② フォルダ数 ③ ファイル数と合計サイズ ④ カーソル位置の情報）。
/// R-34-2 / R-34-3: ②③ はマーク分に切り替わり、そのときアイコンが赤い★になる。
/// 切り替わる条件は区画ごとに独立している（ListSummary の説明を参照。実機確認済み）。
/// R-66-4: 文字・アイコン・高さを DPI に追従させる。
/// </summary>
public sealed class StatusBar : Control
{
    private string _capacity = "";
    private ListSummary _summary;
    private string _cursorName = "";
    private string _cursorDate = "";
    private string _cursorSize = "";
    private string _cursorType = "";
    /// <summary>サイズ欄の幅。フォント・DPI が変わったら -1 に戻して測り直す（R-34）</summary>
    private int _sizeColumn = -1;
    private string _queueText = "";
    /// <summary>キューの区画の左端。クリックの判定に使う。出していなければ int.MaxValue</summary>
    private int _queueLeft = int.MaxValue;

    /// <summary>
    /// F-05: 外部ツールキューの状況。空なら出さない。
    /// 既存の 4 区画（R-34）を上書きせず右端に置く。狭いときは ④ のほうが切れる。
    /// 後から区画が増えても、左から詰める区画の並びは変わらない。
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string QueueText
    {
        get => _queueText;
        set
        {
            if (_queueText == value) return;
            _queueText = value;
            Invalidate();
        }
    }

    /// <summary>キューの区画がクリックされた（進行状況のウィンドウを開く入口）。</summary>
    public event EventHandler? QueueClicked;

    public StatusBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Bottom;
        BackColor = SystemColors.Control;
        UpdateHeight();
    }

    private int Scaled(int logical) => logical * DeviceDpi / 96;
    private int IconSize => Scaled(12);

    private void UpdateHeight()
    {
        using var g = CreateGraphics();
        Height = Math.Max(TextRenderer.MeasureText(g, "Mg", Font).Height, IconSize) + Scaled(6);
    }

    /// <summary>④ の区画に代わりに出す知らせ。次の Update で消える（R-87 の「見つかりません」など）。</summary>
    private string _message = "";
    private long _messageHeldUntil;

    /// <param name="holdMilliseconds">この間は Update で消さない。転送の完了（R-129）は直後の再表示で消えると読めない</param>
    public void ShowMessage(string text, int holdMilliseconds = 0)
    {
        _message = text;
        _messageHeldUntil = Environment.TickCount64 + holdMilliseconds;
        Invalidate();
    }

    public void Update(ListState state, string currentFolder)
    {
        if (Environment.TickCount64 >= _messageHeldUntil) _message = "";
        _summary = ListSummary.Of(state);
        (_cursorDate, _cursorSize, _cursorType, _cursorName) = DescribeCursor(state.Cursor);
        Invalidate();
        LoadCapacityInBackground(currentFolder);
    }

    /// <summary>
    /// R-34 ④: 更新日時・サイズ・種別・名前の順。フォルダはサイズが空、親フォルダは全部空。
    /// 日時とサイズは固定幅の欄に置き、カーソルを動かしても位置が動かないようにする。幅の変わる名前は末尾に置く。
    /// </summary>
    internal static (string Date, string Size, string Type, string Name) DescribeCursor(Entry? entry)
    {
        if (entry is null || entry.IsParent) return ("", "", "", "");
        var isFolder = entry.Kind == EntryKind.Folder;
        return (Display.Timestamp(entry.LastWriteTime), isFolder ? "" : Display.Size(entry.Size),
            ShellFileType.TypeName(entry.FullPath, isFolder), entry.Name);
    }

    /// <summary>
    /// R-34 ④ / R-69-2: サイズ欄の幅。整数部が最大になる見本のうち、いちばん広いものに合わせる。
    /// 描画から切り離すのは、フォントの代わりに文字数などで測る関数を渡してテストするため。
    /// </summary>
    internal static int SizeColumnWidth(Func<string, int> measure) =>
        new[] { "1023.9KB", "1023.9MB", "1023.9GB", "1023.9TB", "1023.9PB", "8.0EB" }.Max(measure);

    /// <summary>
    /// R-34 ④: サイズ欄の右端・種別の始まり・名前の始まり。区切りは gap（空白 2 つ分）。
    /// サイズが空のフォルダでも欄の幅は取るので、種別の始まりはファイルと同じになる。
    /// </summary>
    internal static (int SizeRight, int TypeX, int NameX) CursorLayout(int x, int dateWidth, int sizeColumn, int gap, int typeWidth)
    {
        var sizeRight = x + dateWidth + gap + sizeColumn;
        var typeX = sizeRight + gap;
        return (sizeRight, typeX, typeX + typeWidth + gap);
    }

    private string _capacityRoot = "";
    private DateTime _capacityTakenAt;

    /// <summary>容量を取り直す間隔。転送でどれだけ減ったかが分かる程度でよい。</summary>
    private static readonly TimeSpan CapacityLifetime = TimeSpan.FromSeconds(30);

    /// <summary>容量の取得は応答しないドライブで待たされるため背景で行う（N-05）。</summary>
    private void LoadCapacityInBackground(string folder)
    {
        var root = Path.GetPathRoot(folder);
        if (string.IsNullOrEmpty(root)) { _capacity = ""; _capacityRoot = ""; return; }

        // R-14: カーソルが動くたびに呼ばれる。↓ を押しっぱなしにすると
        // DriveInfo の I/O を抱えたタスクが積み上がり、完了順も保証されない
        if (string.Equals(root, _capacityRoot, StringComparison.OrdinalIgnoreCase)
            && DateTime.UtcNow - _capacityTakenAt < CapacityLifetime) return;

        _capacityRoot = root;
        _capacityTakenAt = DateTime.UtcNow;

        Task.Run(() =>
        {
            try
            {
                var drive = new DriveInfo(root);
                return drive.IsReady
                    ? Display.DriveCapacity(root[0].ToString(), drive.TotalSize, drive.AvailableFreeSpace)
                    : $"{root[0]}:";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return $"{root[0]}:";
            }
        }).ContinueWith(task =>
        {
            if (!task.IsCompletedSuccessfully || IsDisposed) return;
            try { BeginInvoke(() => { _capacity = task.Result; Invalidate(); }); }
            catch (InvalidOperationException) { }   // 窓ができる前・確かめた直後に閉じた。捨てられたタスクの例外として記録に載せない（R-126）
        });
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var pad = Scaled(6);

        var right = Width;
        _queueLeft = int.MaxValue;
        if (_queueText.Length > 0)
        {
            var size = TextRenderer.MeasureText(e.Graphics, _queueText, Font);
            _queueLeft = Math.Max(0, Width - size.Width - pad * 2);
            e.Graphics.DrawLine(SystemPens.ControlDark, _queueLeft, Scaled(3), _queueLeft, Height - Scaled(3));
            TextRenderer.DrawText(e.Graphics, _queueText, Font, new Rectangle(_queueLeft + pad, 0, size.Width, Height), ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            right = _queueLeft;
        }

        // 既存の区画はキューの区画の手前で切る
        e.Graphics.SetClip(new Rectangle(0, 0, right, Height));
        var x = pad;
        x = DrawSection(e.Graphics, x, _capacity, icon: null, marked: false);
        // R-34-3: マーク件数を表示している区画は、アイコンの上に赤い★を重ねる（実機確認）。
        // ★に差し替えるのではなく、フォルダ／ファイルのアイコンは残したまま重ねる
        x = DrawSection(e.Graphics, x, $"{_summary.FolderCount}個", Icon.Folder, _summary.FolderFromMarks);
        x = DrawSection(e.Graphics, x, $"{_summary.FileCount}個 {Display.Size(_summary.TotalSize)}",
            Icon.File, _summary.FileFromMarks);
        if (_message.Length > 0) DrawSection(e.Graphics, x, _message, icon: null, marked: false);
        else DrawCursorSection(e.Graphics, x);
        e.Graphics.ResetClip();
    }

    private enum Icon { Folder, File }

    /// <summary>
    /// R-34 ④: 日時・サイズ・種別を先に描き、残りの幅に名前を置く。サイズは固定幅の欄の中で右寄せ。
    /// 日時・サイズ・種別も入らない幅では名前を出さず、それらは区画の右端（OnPaint のクリップ）で切れる。
    /// </summary>
    private void DrawCursorSection(Graphics g, int x)
    {
        if (_cursorName.Length == 0) return;
        var pad = Scaled(6);
        g.DrawLine(SystemPens.ControlDark, x - pad / 2, Scaled(3), x - pad / 2, Height - Scaled(3));
        var measure = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        int Measure(string s) => TextRenderer.MeasureText(g, s, Font, Size.Empty, measure).Width;
        if (_sizeColumn < 0) _sizeColumn = SizeColumnWidth(Measure);
        var gap = Measure("  ");
        var dateWidth = Measure(_cursorDate);
        var typeWidth = Measure(_cursorType);
        var (sizeRight, typeX, nameX) = CursorLayout(x, dateWidth, _sizeColumn, gap, typeWidth);
        var nameWidth = CursorNameWidth(nameX, Measure(_cursorName), Width, _queueLeft, pad);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping;
        TextRenderer.DrawText(g, _cursorDate, Font, new Rectangle(x, 0, dateWidth, Height), ForeColor, flags);
        TextRenderer.DrawText(g, _cursorSize, Font, new Rectangle(sizeRight - _sizeColumn, 0, _sizeColumn, Height), ForeColor,
            (flags & ~TextFormatFlags.Left) | TextFormatFlags.Right);
        TextRenderer.DrawText(g, _cursorType, Font, new Rectangle(typeX, 0, typeWidth, Height), ForeColor, flags);
        TextRenderer.DrawText(g, _cursorName, Font, new Rectangle(nameX, 0, nameWidth, Height), ForeColor, flags | TextFormatFlags.EndEllipsis);
    }

    /// <summary>
    /// R-34 ④: 名前に使える幅。区画の右端はキューの区画を出している間はその手前（OnPaint と同じ）。
    /// 描画から切り離すのは、コントロールのハンドルを作らずに幅ごとの扱いをテストするため。
    /// </summary>
    internal static int CursorNameWidth(int nameX, int nameWidth, int controlWidth, int queueLeft, int pad) =>
        Math.Max(0, Math.Min(nameWidth, Math.Min(controlWidth, queueLeft) - nameX - pad));

    private int DrawSection(Graphics g, int x, string text, Icon? icon, bool marked)
    {
        var pad = Scaled(6);
        if (x > pad)
        {
            g.DrawLine(SystemPens.ControlDark, x - pad / 2, Scaled(3), x - pad / 2, Height - Scaled(3));
        }

        if (icon is { } kind)
        {
            var rect = new Rectangle(x, (Height - IconSize) / 2, IconSize, IconSize);
            DrawIcon(g, rect, kind, marked);
            x += IconSize + Scaled(3);
        }

        var size = TextRenderer.MeasureText(g, text, Font);
        TextRenderer.DrawText(g, text, Font, new Rectangle(x, 0, size.Width, Height), ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping);
        return x + size.Width + pad * 2;
    }

    private static void DrawIcon(Graphics g, Rectangle rect, Icon kind, bool marked)
    {
        // フォルダ／ファイルは輪郭だけの簡素な図形にする（卓駆のビットマップは流用しない・N-07）
        using (var pen = new Pen(SystemColors.ControlDarkDark))
        {
            if (kind == Icon.Folder)
            {
                var tab = new Rectangle(rect.X, rect.Y + rect.Height / 4, rect.Width, rect.Height / 2);
                g.DrawRectangle(pen, tab);
                g.DrawLine(pen, rect.X, tab.Y, rect.X + rect.Width / 3, rect.Y + rect.Height / 8);
            }
            else
            {
                g.DrawRectangle(pen, rect.X + rect.Width / 6, rect.Y, rect.Width * 2 / 3, rect.Height - 1);
            }
        }

        if (!marked) return;

        // ファイルリストの行と同じ作法で、アイコンの上に赤い★を重ねる（R-11-5 / R-34-3）。
        // 形の定義は Rendering.MarkStar に 1 つだけ置く（V-12）。
        // R-108-4: ダークではダーク用の推奨の★と同じ色、ハイコントラストでは OS の文字色（色で区別しない）
        var star = Program.StartupOs.HighContrast ? Program.StartupOs.WindowText
            : Program.StartupOs.Dark ? Color.FromArgb(0xFF, 0x6B, 0x6B)
            : Color.Red;
        MarkStar.Draw(g, rect, star);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        UpdateHeight();   // R-66
        _sizeColumn = -1;
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _sizeColumn = -1;
        UpdateHeight();
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left && e.X >= _queueLeft) QueueClicked?.Invoke(this, EventArgs.Empty);
    }
}
