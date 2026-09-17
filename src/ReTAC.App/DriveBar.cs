using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ReTAC.Shell;

namespace ReTAC.App;

/// <summary>
/// ファイルリスト上部のドライブバー（R-32）。フラットスタイル（14.1 節）。
/// 末尾にデスクトップボタンを置く（R-32-2）。ネットワークボタンはスコープ外。
/// R-66-4: 文字・アイコン・高さを DPI に追従させる。
/// </summary>
public sealed class DriveBar : Control
{
    /// <param name="Label">ボタンに描く文字。空ならアイコンだけ（B-15）</param>
    /// <param name="Tooltip">マウスを置いたときの説明（B-15 / P-02）</param>
    private sealed record Button(string Label, string Path, Rectangle Bounds, char? DriveLetter, string Tooltip);

    private const string DesktopLabel = "デスクトップ";

    /// <summary>
    /// ツールチップが出るまでの待ち。利用者の指定は「数秒置いたとき」。
    /// ToolTip の既定（約 0.5 秒）では通りすがりに出てしまう。実機で調整する
    /// </summary>
    private const int TooltipDelay = 1000;

    private List<Button> _buttons = [];
    /// <summary>ボタンのパス → アイコン。ドライブもデスクトップも同じ経路で取る。</summary>
    private Dictionary<string, Bitmap>? _icons;
    /// <summary>デスクトップのアイコンが取れなかった。文字のボタンに戻す（B-15）。</summary>
    private bool _desktopIconMissing;
    private readonly ToolTip _tooltip = new() { InitialDelay = TooltipDelay };
    private int _hoverIndex = -1;
    private int _focusIndex = -1;
    private int _currentIndex = -1;
    private IReadOnlySet<char> _hiddenDrives = new HashSet<char>();
    private bool _showDesktop = true;

    public DriveBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = false;   // §6.1: Tab によるフォーカス移動は持たない。L で入って Esc で戻る
        AllowDrop = true;  // R-65 ③: ドライブアイコンへのドロップでコピー・移動
        Dock = DockStyle.Top;
        BackColor = SystemColors.Control;
        Rebuild();
    }

    /// <summary>ドライブまたはデスクトップが選ばれた（14.1 節: シングルクリック）。</summary>
    public event EventHandler<string>? PathSelected;

    /// <summary>キーボードによる選択が Esc で中止された（R-18）。</summary>
    public event EventHandler? Cancelled;

    /// <summary>ドライブのボタンにファイルが落とされた（T8-3）。転送は呼び出し側が行う。</summary>
    public event EventHandler<(string Path, string[] Files)>? FilesDropped;

    /// <summary>
    /// ドライブのボタンが右クリックされた。エクスプローラーと同じく Windows 標準のメニューを出す。
    /// 実際に出すのは呼び出し側（リストの右クリックと同じ経路を通す）。移動はしない。
    /// </summary>
    public event EventHandler<(string Path, Point ScreenPoint)>? RightClicked;

    /// <summary>16.7 節: 表示するドライブの設定。</summary>
    public void SetVisibility(IReadOnlySet<char> hiddenDrives, bool showDesktop)
    {
        _hiddenDrives = hiddenDrives;
        _showDesktop = showDesktop;
        Rebuild();
    }

    /// <summary>
    /// 今いるドライブを知らせる。そのボタンをへこませて示す（卓駆と同じ）。
    /// パスを読まなくても、どのドライブにいるかがアイコンで分かる。
    /// </summary>
    public void SetCurrentPath(string currentFolder)
    {
        var index = IndexOfDrive(currentFolder);
        if (index == _currentIndex) return;
        _currentIndex = index;
        Invalidate();
    }

    private int IndexOfDrive(string folder)
    {
        var letter = folder.Length > 0 ? char.ToUpperInvariant(folder[0]) : (char)0;
        return _buttons.FindIndex(b => b.DriveLetter == letter);
    }

    /// <summary>
    /// `L`（ドライブの選択・0x82F3）。卓駆のキーボード操作に合わせ、
    /// ドライブバーにフォーカスを移して ←→ で移動し Enter で確定させる。
    /// </summary>
    public bool EnterKeyboardSelection(string currentFolder)
    {
        var index = IndexOfDrive(currentFolder);
        _focusIndex = index >= 0 ? index : 0;
        Focus();
        Invalidate();
        return true;
    }

    private int Scaled(int logical) => logical * DeviceDpi / 96;
    private int IconSize => Scaled(16);

    /// <summary>ボタンを並べるのに要る幅。枠なしのモーダル（R-77）の大きさを決めるのに使う。</summary>
    public int PreferredWidth => _buttons.Count == 0 ? 0 : _buttons[^1].Bounds.Right + Scaled(2);

    private void Rebuild()
    {
        var iconSize = IconSize;
        var padding = Scaled(6);
        var gap = Scaled(2);

        using var measure = CreateGraphics();
        var textHeight = TextRenderer.MeasureText(measure, "Mg", Font).Height;
        var buttonHeight = Math.Max(iconSize, textHeight) + padding;
        Height = buttonHeight + gap * 2;

        var buttons = new List<Button>();
        var x = gap;

        // DriveInfo.GetDrives 自体は接続を確認しないので UI スレッドから呼んでよい。
        // IsReady や容量の取得は応答しないドライブで待たされるため、ここでは触らない（N-05）
        foreach (var drive in DriveInfo.GetDrives())
        {
            var letter = drive.Name[0];
            if (_hiddenDrives.Contains(char.ToUpperInvariant(letter))) continue;
            var width = iconSize + gap + TextRenderer.MeasureText(measure, letter + ":", Font).Width + padding;
            // P-02: 文言はドライブ名だけで組む。ボリュームラベルは応答しないドライブで待たされる（N-05）
            buttons.Add(new Button(letter + ":", drive.Name, new Rectangle(x, gap, width, buttonHeight), letter,
                $"{char.ToUpperInvariant(letter)}: ドライブへ移動"));
            x += width + gap;
        }

        // R-32-2: 末尾にデスクトップボタン。使用頻度が高い
        if (_showDesktop)
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            // B-15: アイコンだけで示す。取れなかったときだけ従来の文字に戻す（押せないボタンにしない）
            var label = _desktopIconMissing ? DesktopLabel : "";
            var desktopWidth = _desktopIconMissing
                ? iconSize + gap + TextRenderer.MeasureText(measure, DesktopLabel, Font).Width + padding
                : iconSize + padding;
            buttons.Add(new Button(label, desktop, new Rectangle(x + gap * 2, gap, desktopWidth, buttonHeight), null,
                "デスクトップフォルダへ移動"));
        }

        _buttons = buttons;
        _currentIndex = -1;   // 添字が変わるので付け直しを待つ
        LoadIconsInBackground();
        Invalidate();
    }

    /// <summary>応答しないドライブでも UI を固まらせないため、アイコンは背景で取る（N-05）。</summary>
    private void LoadIconsInBackground()
    {
        var size = IconSize;
        // M-4: デスクトップを先に読む。応答しないネットワークドライブがあっても
        // デスクトップボタンが空欄のまま待たされない
        var paths = _buttons.OrderBy(b => b.DriveLetter is null ? 0 : 1).Select(b => b.Path).ToList();

        Task.Run(() =>
        {
            using var icons = new ShellIcons(size);
            var loaded = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
                if (icons.ForPath(path) is { } bitmap) loaded[path] = new Bitmap(bitmap);
            return loaded;
        }).ContinueWith(task =>
        {
            if (!task.IsCompletedSuccessfully) return;
            if (IsDisposed) { DisposeAll(task.Result); return; }
            // 参照の差し替えは UI スレッドで行う（描画中の辞書を書き換えない）
            BeginInvoke(() =>
            {
                var previous = _icons;
                _icons = task.Result;
                // R-11: 古い Bitmap は GDI ハンドルごと残る。DPI・フォント・表示ドライブを
                // 変えるたびに読み直すので、捨てないと積み上がる
                DisposeAll(previous);

                // B-15: デスクトップのアイコンが取れなければ、文字の幅で組み直す。
                // 組み直しでもう一度読むが、_desktopIconMissing が立っているので繰り返さない
                if (!_desktopIconMissing
                    && _buttons.FirstOrDefault(b => b.DriveLetter is null) is { } desktop
                    && !_icons.ContainsKey(desktop.Path))
                {
                    _desktopIconMissing = true;
                    Rebuild();
                    return;
                }
                Invalidate();
            });
        });
    }

    private static void DisposeAll(Dictionary<string, Bitmap>? icons)
    {
        if (icons is null) return;
        foreach (var icon in icons.Values) icon.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeAll(_icons);
            _tooltip.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var gap = Scaled(2);

        for (var i = 0; i < _buttons.Count; i++)
        {
            var button = _buttons[i];
            if (i == _focusIndex && Focused)
            {
                // フォーカスのあるドライブは卓駆と同じように凸型の枠で示す
                ControlPaint.DrawButton(e.Graphics, button.Bounds, ButtonState.Normal);
            }
            else if (i == _currentIndex)
            {
                // 今いるドライブは凹ませる。卓駆はこれで現在地を示している
                ControlPaint.DrawButton(e.Graphics, button.Bounds, ButtonState.Pushed);
            }
            else if (i == _hoverIndex)
            {
                // 14.1 節「バーのボタンをフラットスタイルにする」= 常時は枠なし、ホバーで浮かせる
                using var brush = new SolidBrush(SystemColors.ControlLight);
                e.Graphics.FillRectangle(brush, button.Bounds);
                e.Graphics.DrawRectangle(SystemPens.ControlDark, button.Bounds);
            }

            var iconSize = IconSize;
            // 文字の無いボタン（デスクトップ）はアイコンを中央に置く
            var iconX = button.Label.Length == 0
                ? button.Bounds.X + (button.Bounds.Width - iconSize) / 2
                : button.Bounds.X + gap;
            var iconRect = new Rectangle(iconX, button.Bounds.Y + (button.Bounds.Height - iconSize) / 2, iconSize, iconSize);

            if (_icons?.TryGetValue(button.Path, out var icon) == true) e.Graphics.DrawImage(icon, iconRect);

            if (button.Label.Length == 0) continue;
            var textRect = new Rectangle(
                iconRect.Right + gap, button.Bounds.Y,
                button.Bounds.Right - iconRect.Right - gap, button.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, button.Label, Font, textRect, ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        }
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        var point = PointToClient(new Point(e.X, e.Y));
        var index = _buttons.FindIndex(b => b.Bounds.Contains(point));

        // 落とせるボタンの上にいることを、ホバーと同じ見た目で示す
        if (index != _hoverIndex) { _hoverIndex = index; Invalidate(); }

        const int ctrl = 8, shift = 4;
        e.Effect = index < 0 || e.Data?.GetDataPresent(DataFormats.FileDrop) != true
            ? DragDropEffects.None
            : (e.KeyState & ctrl) != 0 ? DragDropEffects.Copy
            : (e.KeyState & shift) != 0 ? DragDropEffects.Move
            : DragDropEffects.Copy | DragDropEffects.Move;
    }

    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        _hoverIndex = -1;
        Invalidate();
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        _hoverIndex = -1;
        Invalidate();

        var point = PointToClient(new Point(e.X, e.Y));
        if (_buttons.FirstOrDefault(b => b.Bounds.Contains(point)) is not { } button) return;
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            FilesDropped?.Invoke(this, (button.Path, files));
    }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) switch
    {
        Keys.Left or Keys.Right or Keys.Home or Keys.End or Keys.Enter or Keys.Escape => true,
        _ => base.IsInputKey(keyData),
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_buttons.Count == 0) return;

        switch (e.KeyCode)
        {
            case Keys.Left: _focusIndex = Math.Max(0, _focusIndex - 1); break;
            case Keys.Right: _focusIndex = Math.Min(_buttons.Count - 1, _focusIndex + 1); break;
            case Keys.Home: _focusIndex = 0; break;
            case Keys.End: _focusIndex = _buttons.Count - 1; break;

            // フォーカスがある時は 1〜9 でも対応するドライブへ変更できる
            case >= Keys.D1 and <= Keys.D9:
            case >= Keys.NumPad1 and <= Keys.NumPad9:
                var number = e.KeyCode >= Keys.NumPad1 ? e.KeyCode - Keys.NumPad0 : e.KeyCode - Keys.D0;
                var letter = (char)('A' + number - 1);
                if (_buttons.FirstOrDefault(b => b.DriveLetter == letter) is { } target)
                    PathSelected?.Invoke(this, target.Path);
                e.Handled = true;
                return;

            // 卓駆と同じく、ドライブ名の文字でそのドライブへ移る（L のあと C で C:）。
            // 無いドライブの文字なら、エラーは出さずにフォーカスだけ返す
            case >= Keys.A and <= Keys.Z:
                if (_buttons.FirstOrDefault(b => b.DriveLetter is { } d && char.ToUpperInvariant(d) == (char)e.KeyCode) is { } picked)
                    PathSelected?.Invoke(this, picked.Path);
                else
                    Cancelled?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;

            case Keys.Enter:
                PathSelected?.Invoke(this, _buttons[Math.Max(0, _focusIndex)].Path);
                e.Handled = true;
                return;

            case Keys.Escape:
                Cancelled?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;

            default:
                return;
        }

        e.Handled = true;
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        _focusIndex = -1;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = _buttons.FindIndex(b => b.Bounds.Contains(e.Location));
        if (index == _hoverIndex) return;
        _hoverIndex = index;
        // B-15 / P-02: DriveBar は 1 つのコントロールにボタンを描いているので、乗っているボタンで文言を差し替える
        _tooltip.SetToolTip(this, index >= 0 ? _buttons[index].Tooltip : "");
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverIndex = -1;
        _tooltip.SetToolTip(this, "");
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        var button = _buttons.FirstOrDefault(b => b.Bounds.Contains(e.Location));
        if (button is null) return;

        if (e.Button == MouseButtons.Right) RightClicked?.Invoke(this, (button.Path, PointToScreen(e.Location)));
        else if (e.Button == MouseButtons.Left) PathSelected?.Invoke(this, button.Path);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Rebuild();   // R-66: 再起動なしで追従する
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Rebuild();
    }
}
