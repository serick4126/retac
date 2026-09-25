using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ReTAC.Domain.Entries;
using ReTAC.Domain.Navigation;
using Timer = System.Windows.Forms.Timer;

namespace ReTAC.App;

/// <summary>
/// R-89: バーとグループのメニューの上のドラッグ。バーの項目の並べ替えと、ファイル・フォルダの追加を受ける。
/// ToolStrip.AllowItemReorder は Alt を押しながらでないと働かず、挿入位置も出ないので自前で扱う。
/// </summary>
internal sealed class BookmarkDropZone
{
    /// <summary>バーの項目のドラッグの形式。中身には何も載せない（他のアプリへ渡さない。ReTAC の外へは落とせない）。</summary>
    internal const string Format = "ReTAC.Bookmark";

    /// <summary>ドラッグ中のバーの項目。窓をまたいでも同じプロセスの中なので、ここで受け渡す（R-98: ブックマークビューとも）。</summary>
    private static Bookmark? s_dragging;

    internal static Bookmark? Dragging => s_dragging;

    private readonly ToolStrip _strip;
    private readonly List<Bookmark> _list;
    private readonly IBookmarkHost _host;
    private readonly bool _vertical;
    /// <summary>グループの上で止まったら開く（R-89）。</summary>
    private readonly Timer _hold = new() { Interval = 1000 };
    private ToolStripDropDownItem? _holding;
    private DropSpot? _spot;

    private BookmarkDropZone(ToolStrip strip, List<Bookmark> list, IBookmarkHost host, bool vertical)
    {
        _strip = strip;
        _list = list;
        _host = host;
        _vertical = vertical;
    }

    /// <param name="list">この並びへ入れる（バーなら BookmarkSet.Bar、グループのメニューならその Children）</param>
    /// <param name="vertical">メニュー（縦）なら true</param>
    public static void Attach(ToolStrip strip, List<Bookmark> list, IBookmarkHost host, bool vertical)
    {
        var zone = new BookmarkDropZone(strip, list, host, vertical);
        strip.AllowDrop = true;
        // 効果を決めてからドラッグ画像の後始末へ知らせる（DropTargetHelper）
        strip.DragEnter += (s, e) => zone.Guard(e, () => { zone.OnDragOver(s, e); DropTargetHelper.Enter(strip, e); });
        strip.DragOver += (s, e) => zone.Guard(e, () => { zone.OnDragOver(s, e); DropTargetHelper.Over(e); });
        strip.DragLeave += (_, _) => zone.Guard(null, () => { zone.Reset(); DropTargetHelper.Leave(); });
        strip.DragDrop += (s, e) => zone.Guard(e, () => { DropTargetHelper.Drop(e); zone.OnDragDrop(s, e); });
        strip.Paint += zone.OnPaint;
        zone._hold.Tick += (_, _) =>
        {
            zone._hold.Stop();
            if (zone._holding is { IsDisposed: false } item) item.ShowDropDown();
        };
        strip.Disposed += (_, _) => zone._hold.Dispose();
    }

    /// <summary>
    /// 受け口から例外を漏らさない。漏れると OS がドロップ自体を断り、禁止のカーソルになる。
    /// 失敗したら、枠・線・ホールドのタイマー・ステータス・ドラッグ画像を片付けて、落とせない扱いにする。
    /// </summary>
    private void Guard(DragEventArgs? e, Action action) => Guarded(e, action, Reset);

    /// <summary><see cref="Guard"/> の中身。展開したメニューの受け口（<see cref="ExpansionDropZone"/>）も同じ守り方をする。</summary>
    internal static void Guarded(DragEventArgs? e, Action action, Action reset)
    {
        try { action(); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            if (e is not null) e.Effect = DragDropEffects.None;
            try { reset(); }
            catch (Exception inner) { System.Diagnostics.Debug.WriteLine(inner); }
            DropTargetHelper.Leave();
        }
    }

    /// <summary>
    /// バー・グループのメニューの項目を、左ボタンで押して動かしたらドラッグを始める。
    /// 並べ替え・グループへの出し入れの条件は、どこから始めても同じ（BookmarkRules.Move が決める）。
    /// ToolStrip は項目の上のマウスを項目へ回し、自分の MouseDown / MouseMove イベントを出さないので、項目のイベントで拾う。
    /// </summary>
    public static void EnableDrag(ToolStripItem item, Bookmark bookmark)
    {
        Point? origin = null;
        item.MouseDown += (_, e) => origin = e.Button == MouseButtons.Left ? e.Location : null;
        // 閉じたフォルダ・グループのボタンは、押しても開かないよう base.OnMouseDown を呼ばず、MouseDown イベントが出ない
        if (item is BarDropDownButton button) button.LeftPressedClosed += (_, e) => origin = e.Location;
        item.MouseUp += (_, _) => origin = null;
        item.MouseMove += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || origin is not { } o || item.Owner is not { } strip) return;
            if (Math.Abs(e.X - o.X) < SystemInformation.DragSize.Width
                && Math.Abs(e.Y - o.Y) < SystemInformation.DragSize.Height) return;
            origin = null;
            (item as BarDropDownButton)?.CancelOpen();
            // R-91-2: ドラッグを始めたら、覚えていた押下の情報を次のクリックに使わせない
            // （バーのボタンは CancelOpen が自分のトラッカーを直接捨てるので、ここでは主にホバー展開の項目向け）
            DoubleClickTrackers.Forget(item);
            DragFrom(strip, bookmark);
        };
    }

    private static void DragFrom(ToolStrip strip, Bookmark bookmark)
    {
        if (DragBookmark(strip, bookmark) != DragDropEffects.None) CloseMenus(strip);
    }

    /// <summary>ブックマークの項目のドラッグを始める。終わるまで戻らない（R-98: ブックマークビューからも使う）。</summary>
    internal static DragDropEffects DragBookmark(Control source, Bookmark bookmark)
    {
        s_dragging = bookmark;
        try { return source.DoDragDrop(new DataObject(Format, ""), DragDropEffects.Move); }
        finally { s_dragging = null; }
    }

    /// <summary>
    /// 並びを変えたら、開いているメニューを閉じる。メニューは開くたびに組み直すので、開いたままだと古い並びが残る
    /// （バーから開いたメニューは、バーの作り直しで閉じる。ブックマークメニューから開いたものは閉じない）。
    /// </summary>
    internal static void CloseMenus(ToolStrip strip)
    {
        if (strip is not ToolStripDropDown { IsDisposed: false } dropDown) return;
        while (dropDown.OwnerItem?.Owner is ToolStripDropDown parent) dropDown = parent;
        dropDown.Close(ToolStripDropDownCloseReason.CloseCalled);
    }

    /// <summary>並んでいるブックマークの項目。バーで入りきらない項目は » の中なので、見えている先頭の分だけ。</summary>
    private List<ToolStripItem> Slots() =>
        _strip.Items.Cast<ToolStripItem>().Where(i => i.Tag is Bookmark && i.Placement == ToolStripItemPlacement.Main).ToList();

    // どの項目も中央 1/3 は「項目の上」。何が起こるかは BookmarkDrop.Onto が種類で決める（R-93: ファイル・コマンドの上は落とせない）
    private DropSpot Hit(List<ToolStripItem> slots, Point client) =>
        BookmarkDrop.Hit(slots.Select(i => _vertical
            ? (i.Bounds.Top, i.Bounds.Bottom, true)
            : (i.Bounds.Left, i.Bounds.Right, true)).ToList(), _vertical ? client.Y : client.X);

    /// <summary>空の並びの「（空）」の行。空のグループ・「ブックマークバー」サブメニューへは、この行の上に落とす。</summary>
    public static readonly object EmptySlot = new();

    /// <summary>
    /// 縦のメニューでは、ブックマークの項目が並ぶ範囲（空なら「（空）」の行）だけを受け口にする。
    /// ブックマークメニューには管理・追加などの操作の行や区切りも並ぶので、その上に落としても受けない。
    /// バーは空いた所へ落としても末尾に足す（R-89）。
    /// </summary>
    private bool Accepts(List<ToolStripItem> slots, Point client)
    {
        if (!_vertical) return true;
        if (slots.Count == 0)
            return _strip.Items.Cast<ToolStripItem>().Any(i => ReferenceEquals(i.Tag, EmptySlot) && i.Bounds.Contains(client));
        return client.Y >= slots[0].Bounds.Top && client.Y < slots[^1].Bounds.Bottom;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var slots = Slots();
        var client = _strip.PointToClient(new Point(e.X, e.Y));
        var spot = Hit(slots, client);
        var onto = spot.Onto ? slots[spot.Index] : null;
        var accepts = Accepts(slots, client);
        var reorder = s_dragging is not null && e.Data?.GetDataPresent(Format) == true;
        // R3: 外からは FileDrop だけ。URL・テキスト・仮想ファイルは受けない
        var files = !reorder && e.Data?.GetDataPresent(DataFormats.FileDrop) == true;

        var effect = Effect(reorder, e.AllowedEffect);
        string message;
        if (!accepts || !reorder && !files)
        {
            e.Effect = DragDropEffects.None;
            message = "";
        }
        else if (onto?.Tag is Bookmark { Kind: BookmarkKind.Folder } folder)
        {
            // R-93: フォルダの中央はそのフォルダへの転送（枠）。判定はファイルリストへのドロップと同じ（先頭の項目・修飾キー・元が許す効果）。
            // 並べ替えは落とせない（ブックマークはファイルではない）。フォルダの有無はここで確かめない（R-91）
            if (reorder) e.Effect = DragDropEffects.None;
            else DropFeedback.Apply(e, folder.Target, DropFeedback.FolderLabel(folder.Target));
            message = e.Effect switch
            {
                DragDropEffects.Copy => $"{DropFeedback.FolderLabel(folder.Target)} へコピー",
                DragDropEffects.Move => $"{DropFeedback.FolderLabel(folder.Target)} へ移動",
                _ => "",
            };
        }
        else if (onto?.Tag is Bookmark target && (BookmarkDrop.Onto(target.Kind, reorder) != OntoAction.IntoGroup || target.Children is null)
                 || reorder && ReferenceEquals(onto?.Tag, s_dragging))
        {
            e.Effect = DragDropEffects.None;
            message = "";
        }
        else if (effect == DragDropEffects.None)
        {
            e.Effect = DragDropEffects.None;   // ドラッグ元が許していない効果は返さない
            message = "";
        }
        else
        {
            e.Effect = effect;
            var group = onto is null ? "" : $"「{onto.Text?.Replace("&&", "&")}」に";
            message = reorder ? (onto is null ? "並べ替え" : $"{group}移す") : $"{group}ブックマークに追加";
        }
        _host.ShowStatus(message);

        if (!ReferenceEquals(onto, _holding))
        {
            _hold.Stop();
            _holding = onto as ToolStripDropDownItem;
            // グループは中へ入れるため、フォルダはファイルを中のサブフォルダへ落とすため（R-93）に開く
            if (_holding is { Tag: Bookmark { Kind: BookmarkKind.Group } } || files && _holding is { Tag: Bookmark { Kind: BookmarkKind.Folder } })
                _hold.Start();
        }
        var shown = e.Effect == DragDropEffects.None ? (DropSpot?)null : spot;
        if (shown != _spot)
        {
            _spot = shown;
            _strip.Invalidate();
        }
    }

    /// <summary>
    /// ドラッグ元が許す中から選ぶ。ファイルは Move にしない。移動と受け取った元（エクスプローラー）が、元のファイルを消すことがある。
    /// </summary>
    internal static DragDropEffects Effect(bool reorder, DragDropEffects allowed) =>
        reorder ? allowed & DragDropEffects.Move
        : allowed.HasFlag(DragDropEffects.Link) ? DragDropEffects.Link
        : allowed & DragDropEffects.Copy;

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        var spot = _spot;
        var slots = Slots();
        Reset();
        if (spot is not { } s) return;

        var onto = s.Onto ? slots[s.Index].Tag as Bookmark : null;
        if (onto is { Kind: BookmarkKind.Folder })
        {
            // R-93: 転送。_spot は効果が None でないときだけ立つので、表示が禁止だった所へは来ない
            if (s_dragging is null && e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            {
                var (ctrl, shift) = DropFeedback.Modifiers(e);   // 後に回すとキーは離されている
                (slots[s.Index] as ToolStripDropDownItem)?.HideDropDown();   // ホールドで開いていたら閉じる
                CloseMenus(_strip);
                _host.TransferDropped(files, onto.Target, e.AllowedEffect, ctrl, shift);
            }
            return;
        }
        var dest = onto?.Children ?? _list;
        var index = onto is null ? s.Index : dest.Count;   // グループの上なら、その末尾へ
        var changed = false;
        if (s_dragging is { } dragging)
        {
            changed = BookmarkRules.Move(_host.Bookmarks, dragging, dest, index);
        }
        else if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
        {
            changed = InsertFiles(dest, index, paths);
        }
        if (!changed) return;
        _host.BookmarksChanged();
        CloseMenus(_strip);
    }

    /// <summary>R-89: 落とされたファイル・フォルダを dest の index の前へ登録する。足したら true（R-98: ブックマークビューと共有）。</summary>
    internal static bool InsertFiles(List<Bookmark> dest, int index, string[] paths)
    {
        var changed = false;
        // Q5: ドロップ元の順に全件。重複は畳まない。見つからないパスは飛ばす
        foreach (var path in paths)
        {
            BookmarkKind? kind = Directory.Exists(path) ? BookmarkKind.Folder : File.Exists(path) ? BookmarkKind.File : null;
            if (kind is not { } k) continue;
            dest.Insert(index++, new Bookmark("", k, path));
            changed = true;
        }
        return changed;
    }

    private void Reset()
    {
        _hold.Stop();
        _holding = null;
        _host.ShowStatus("");
        if (_spot is null) return;
        _spot = null;
        _strip.Invalidate();
    }

    /// <summary>挿入位置の線（項目の間）か、項目の枠（グループの上）を描く。ToolStrip は標準では何も出さない。</summary>
    private void OnPaint(object? sender, PaintEventArgs e)
    {
        if (_spot is not { } spot) return;
        var slots = Slots();
        var width = Math.Max(2, 2 * _strip.DeviceDpi / 96);
        using var brush = new SolidBrush(SystemColors.Highlight);
        if (spot.Onto)
        {
            using var pen = new Pen(SystemColors.Highlight, width);
            var bounds = slots[spot.Index].Bounds;
            e.Graphics.DrawRectangle(pen, bounds.X + 1, bounds.Y + 1, bounds.Width - 2, bounds.Height - 2);
            return;
        }
        // 線は、入れる位置の後ろの項目の手前（末尾なら最後の項目の後ろ）
        int At(Rectangle r, bool before) => _vertical ? (before ? r.Top : r.Bottom) : (before ? r.Left : r.Right);
        var edge = spot.Index < slots.Count ? At(slots[spot.Index].Bounds, before: true)
            : slots.Count > 0 ? At(slots[^1].Bounds, before: false)
            : _vertical ? _strip.Padding.Top : _strip.Padding.Left;
        var line = _vertical
            ? new Rectangle(4, edge - width / 2, _strip.ClientSize.Width - 8, width)
            : new Rectangle(edge - width / 2, 3, width, _strip.ClientSize.Height - 6);
        e.Graphics.FillRectangle(brush, line);
    }
}

/// <summary>
/// R-93: 展開したメニュー（ブックマークのフォルダの中身）の上のドロップ。ファイルを転送する。
/// サブフォルダの項目の上ならそのサブフォルダへ（項目を枠で囲む。止めると開く）、それ以外ならそのメニューのフォルダへ（メニュー全体を枠で囲む）。
/// 挿入線は無い（ここはブックマークの並びではなく、ファイルシステムの中身）。
/// </summary>
internal sealed class ExpansionDropZone
{
    private readonly ToolStripDropDown _menu;
    private readonly string _folder;
    private readonly IBookmarkHost _host;
    private readonly Timer _hold = new() { Interval = 1000 };   // BookmarkDropZone と同じ「ホールドで展開」
    private ToolStripDropDownItem? _holding;
    /// <summary>枠で囲むもの。サブフォルダの項目か、メニュー全体（null の項目）。落とせないときは None。</summary>
    private (bool Shown, ToolStripItem? Item) _frame;

    private ExpansionDropZone(ToolStripDropDown menu, string folder, IBookmarkHost host)
    {
        _menu = menu;
        _folder = folder;
        _host = host;
    }

    public static void Attach(ToolStripDropDown menu, string folder, IBookmarkHost host)
    {
        var zone = new ExpansionDropZone(menu, folder, host);
        menu.AllowDrop = true;
        menu.DragEnter += (_, e) => zone.Guard(e, () => { zone.OnDragOver(e); DropTargetHelper.Enter(menu, e); });
        menu.DragOver += (_, e) => zone.Guard(e, () => { zone.OnDragOver(e); DropTargetHelper.Over(e); });
        menu.DragLeave += (_, _) => zone.Guard(null, () => { zone.Reset(); DropTargetHelper.Leave(); });
        menu.DragDrop += (_, e) => zone.Guard(e, () => { DropTargetHelper.Drop(e); zone.OnDragDrop(e); });
        menu.Paint += zone.OnPaint;
        zone._hold.Tick += (_, _) =>
        {
            zone._hold.Stop();
            if (zone._holding is { IsDisposed: false } item) item.ShowDropDown();
        };
        menu.Disposed += (_, _) => zone._hold.Dispose();
    }

    private void Guard(DragEventArgs? e, Action action) => BookmarkDropZone.Guarded(e, action, Reset);

    /// <summary>落とす先のフォルダと、枠で囲む項目（null ならメニュー全体）。</summary>
    private (string Folder, ToolStripItem? Item) Target(DragEventArgs e)
    {
        var item = _menu.GetItemAt(_menu.PointToClient(new Point(e.X, e.Y)));
        // フォルダの有無はここで確かめない（R-91）。Entry は開いたときに読んだもの
        return item?.Tag is Entry { Kind: EntryKind.Folder } sub ? (sub.FullPath, item) : (_folder, null);
    }

    private void OnDragOver(DragEventArgs e)
    {
        var (folder, item) = Target(e);
        // 外からは FileDrop だけ（R3）。ブックマークの並べ替えは FileDrop を持たないので、ここで自然に None になる
        DropFeedback.Apply(e, folder, DropFeedback.FolderLabel(folder));
        var label = DropFeedback.FolderLabel(folder);
        _host.ShowStatus(e.Effect switch
        {
            DragDropEffects.Copy => $"{label} へコピー",
            DragDropEffects.Move => $"{label} へ移動",
            _ => "",
        });

        if (!ReferenceEquals(item, _holding))
        {
            _hold.Stop();
            _holding = item as ToolStripDropDownItem;
            if (_holding is not null && e.Effect != DragDropEffects.None) _hold.Start();
        }
        var frame = (e.Effect != DragDropEffects.None, item);
        if (frame != _frame)
        {
            _frame = frame;
            _menu.Invalidate();
        }
    }

    private void OnDragDrop(DragEventArgs e)
    {
        var (folder, _) = Target(e);
        var shown = _frame.Shown;
        Reset();
        if (!shown || e.Data?.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files) return;
        var (ctrl, shift) = DropFeedback.Modifiers(e);   // 後に回すとキーは離されている
        BookmarkDropZone.CloseMenus(_menu);
        _host.TransferDropped(files, folder, e.AllowedEffect, ctrl, shift);
    }

    private void Reset()
    {
        _hold.Stop();
        _holding = null;
        _host.ShowStatus("");
        if (!_frame.Shown) return;
        _frame = default;
        _menu.Invalidate();
    }

    private void OnPaint(object? sender, PaintEventArgs e)
    {
        if (!_frame.Shown) return;
        var width = Math.Max(2, 2 * _menu.DeviceDpi / 96);
        using var pen = new Pen(SystemColors.Highlight, width);
        var bounds = _frame.Item?.Bounds ?? _menu.ClientRectangle;
        e.Graphics.DrawRectangle(pen, bounds.X + 1, bounds.Y + 1, bounds.Width - 2, bounds.Height - 2);
    }
}

/// <summary>
/// R-107: バーのボタンから開いたドロップダウンの、選べる項目の先頭・末尾を選ぶ。
/// ToolStrip.SelectNextToolStripItem・CanKeyboardSelect は internal でアセンブリの外から呼べないため、
/// 区切り線を除く・有効な項目という近い条件（このバーの中身は自分で組み立てているので実害は無い）で代える。
/// </summary>
internal static class BarKeyboardNav
{
    internal static void SelectEdge(ToolStripItemCollection items, bool first)
    {
        var candidates = items.Cast<ToolStripItem>().Where(i => i.Enabled && i is not ToolStripSeparator);
        (first ? candidates.FirstOrDefault() : candidates.LastOrDefault())?.Select();
    }

    /// <summary>
    /// 「»」の一覧に回っている、キーボードで選べる項目（並び順）。「»」の DropDownItems は常に空を返す
    /// （ToolStripOverflow.Items は空の読み取り専用の集まりで、中身は ToolStrip の internal な OverflowItems）ので、
    /// バー本体の Items から IsOnOverflow で拾う。OverflowItems も Items の順に積まれるので並びは同じ。
    /// </summary>
    internal static List<ToolStripItem> OverflowSelectable(ToolStrip bar) =>
        bar.Items.Cast<ToolStripItem>().Where(i => i.IsOnOverflow && i.Available && i.Enabled && i is not ToolStripSeparator).ToList();

    /// <summary>
    /// R-107: 1 段目の一覧を閉じて、開いたボタンを選んだ状態へ戻す（Esc と同じ効果）。
    /// ToolStripDropDown.SelectPreviousToolStrip（Esc の実装）は internal で呼べないので、公開 API だけで
    /// 閉じる・バーへ焦点を戻す・ボタンを選ぶ、を組み立てる。
    /// </summary>
    internal static void ReturnToOwner(ToolStripDropDown dropDown, ToolStripItem ownerItem, ToolStrip bar)
    {
        dropDown.Visible = false;
        bar.Focus();
        ownerItem.Select();
    }

    /// <summary>
    /// R-107: 「»」の一覧の中の項目（BarButton・BarDropDownButton）へ来たキー。処理したら true。
    /// 標準では「»」の中の項目はバー直下と同じ扱いで（ToolStripDropDownItem.ProcessDialogKey の isTopLevel）、
    /// ↑/↓ でフォルダを開いてしまい、←/→ は横に詰めた並びの中を移り、端から「»」へは Esc でしか戻れない。
    /// 利用者の決定で「»」の一覧も 1 段目として扱う: ↑/↓ は並び順で移り端で「»」へ戻る、← は何もしない、
    /// → は開けるものだけ開く（open）。Enter・Esc は標準のまま。
    /// </summary>
    internal static bool ProcessOverflowKey(ToolStripItem item, Keys keyData, bool canOpen, Action? open)
    {
        if (!item.IsOnOverflow || item.Owner is not { } bar) return false;
        switch (keyData)
        {
            case Keys.Left or Keys.Right:
                if (!BookmarkRules.SwallowArrowKey(level: 1, canOpen, forward: keyData == Keys.Right)) open?.Invoke();
                return true;
            case Keys.Up or Keys.Down:
                var list = OverflowSelectable(bar);
                if (BookmarkRules.OverflowListStep(list.IndexOf(item), list.Count, down: keyData == Keys.Down) is { } next)
                    list[next].Select();
                else if (item.GetCurrentParent() is ToolStripDropDown overflow)
                    ReturnToOwner(overflow, bar.OverflowButton, bar);
                return true;
            default:
                return false;
        }
    }
}

/// <summary>
/// バーのフォルダ・グループのボタン。押したときではなく離したときに開く（押した時点で開くと、ドラッグを始められない）。
/// 開いているときに押したら、今までどおり閉じる。ただしフォルダは、その 2 回目の押下が直前に開いた時刻・位置の
/// ダブルクリックの範囲内なら「閉じてジャンプ」（R-91-2）にする。標準の DoubleClick はここには届かない
/// （2 回目の押下は開いたメニューを閉じるのに使われ、Click イベントの対まで進まない）。
/// </summary>
internal sealed class BarDropDownButton : ToolStripDropDownButton
{
    private bool _openOnUp;
    private readonly DoubleClickTracker _doubleClick = new();

    /// <summary>閉じているときに左ボタンで押された。このときは MouseDown イベントが出ないので、ドラッグの始点はここで知らせる。</summary>
    public event MouseEventHandler? LeftPressedClosed;

    /// <summary>フォルダのダブルクリック（R-91-2）。グループには繋がない（呼び出し側の判断）。</summary>
    public event EventHandler? DoubleClicked;

    /// <summary>
    /// R-107: フォルダは中身を非同期に読むため、開いた直後（DropDownOpening が返った時点）ではまだ本当の
    /// 末尾が分からない（「このフォルダへジャンプ」しか無い）。FolderExpansion.Attach がここへ「読み込み終わり後に
    /// 末尾を選び直す」処理を登録する（グループは同期的に子が分かるので登録されない。null のままなら何もしない）。
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Action? RequestSelectLastOnReady { get; set; }

    /// <summary>
    /// R-107: ↓ で開いて先頭を選ぶのは素の ToolStripDropDownItem の動きのままだが、↑ でも同じく先頭を選んでしまう
    /// （ToolStripDropDownItem.ProcessDialogKey は Up も Down も forward: true で開く）。↑ のときだけ末尾を選び直す
    /// （判定は BookmarkRules.BarVerticalKey。ここは開けるボタンなので結果は常に OpenSelectLast）。
    /// </summary>
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (IsOnOverflow)
            return BarKeyboardNav.ProcessOverflowKey(this, keyData, canOpen: Enabled && HasDropDownItems, open: () =>
            {
                ShowDropDown();
                BarKeyboardNav.SelectEdge(DropDownItems, first: true);
            }) || base.ProcessDialogKey(keyData);

        if (Enabled && keyData == Keys.Up && HasDropDownItems
            && BookmarkRules.BarVerticalKey(canOpen: true, down: false) == BookmarkRules.BarVerticalKeyAction.OpenSelectLast)
        {
            RequestSelectLastOnReady?.Invoke();   // フォルダなら、読み込み後に選び直してもらう
            ShowDropDown();   // DropDownOpening が同期的に中身を組み立てる（Configure/AttachGroup/FolderExpansion）
            BarKeyboardNav.SelectEdge(DropDownItems, first: false);
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && !DropDown.Visible)
        {
            _openOnUp = true;
            LeftPressedClosed?.Invoke(this, e);
            return;
        }
        if (e.Button == MouseButtons.Left && DropDown.Visible
            && _doubleClick.Decide(DateTime.UtcNow, e.Location, e.Button, TimeSpan.FromMilliseconds(SystemInformation.DoubleClickTime), SystemInformation.DoubleClickSize))
        {
            HideDropDown();
            DoubleClicked?.Invoke(this, EventArgs.Empty);
            return;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && _openOnUp)
        {
            _openOnUp = false;
            ShowDropDown();
            // 開いた時刻・位置を覚える。次の左の押下（開いている間）がこの範囲内ならダブルクリック（R-91-2）
            _doubleClick.Remember(DateTime.UtcNow, e.Location);
            return;
        }
        base.OnMouseUp(e);
    }

    /// <summary>ドラッグが始まったので、離しても開かない。古い開いた時刻・位置も次のクリックに使わせない。</summary>
    public void CancelOpen()
    {
        _openOnUp = false;
        _doubleClick.Forget();
    }
}

/// <summary>
/// R-107: バーのファイル・コマンドのボタン。素の ToolStripItem は Enter では Click を呼ぶが
/// （ToolStripItem.ProcessDialogKey。フォーカスを渡す前のコントロールへ戻す処理も一緒に行う）、
/// Space はその対象にならない（SupportsSpaceKey が既定 false で、外から立てられない内部プロパティ）。
/// Space のときだけここで同じ Click を呼ぶ。Enter は素の実装に任せる（二重に呼ばれることはない）。
/// 利用者の実機確認により、Enter は一覧へ戻り、Space は続けて押せるようバーに留まる、という違いにした。
/// </summary>
internal sealed class BarButton : ToolStripButton
{
    /// <summary>
    /// 今の Click が Space キー経由かどうか。OnClick の中でしか意味を持たない値なので、UI スレッドだけで
    /// 同期的に読み書きする（WinForms はシングルスレッドなので、static でも競合しない）。
    /// MainForm 側はこれを見て、Space のときだけファイル一覧への焦点の戻しを省く。
    /// </summary>
    public static bool IsSpaceActivation { get; private set; }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        // 「»」の一覧の中は 1 段目の一覧として扱う。Space も開いた一覧の中と同じく何もしない（標準の ToolStripButton のまま）
        if (IsOnOverflow)
            return BarKeyboardNav.ProcessOverflowKey(this, keyData, canOpen: false, open: null) || base.ProcessDialogKey(keyData);

        // ↑/↓ はここで呑み込む。横並びの ToolStrip は ↑/↓ を自分では使わず（ProcessArrowKey は縦並びか
        // ドロップダウンのときだけ動く）、Control.ProcessDialogKey で親へ流す。行き着いたフォームの
        // ContainerControl.ProcessDialogKey が矢印キーとして SelectNextControl で別のコントロールへ焦点を移し、
        // バーの選択が消える（B で入った直後に起きた）。フォルダ・グループのボタンは ToolStripDropDownItem が
        // ↑/↓ を自分で処理する（開けないときも呑み込む）ので、漏れていたのはこのボタンだけ
        if (keyData is Keys.Up or Keys.Down
            && BookmarkRules.BarVerticalKey(canOpen: false, down: keyData == Keys.Down) == BookmarkRules.BarVerticalKeyAction.None)
            return true;

        if (Enabled && keyData == Keys.Space)
        {
            IsSpaceActivation = true;
            try { OnClick(EventArgs.Empty); }
            finally { IsSpaceActivation = false; }
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }
}

/// <summary>
/// R-107: ブックマークバーのボタンから開いたドロップダウンの中の項目。←/→ をバーのボタン間の移動へ
/// 漏らさない（今までは、フォルダを何段も掘っている途中で →/← を押すと、開いたドロップダウンごと閉じて
/// 隣のバーのボタンへ移ってしまっていた）。判定そのものは BookmarkRules.SwallowArrowKey（純粋関数）に置く。
/// バー直下のボタン自身（BarDropDownButton）はこの対象に入れない（ボタン間の移動は標準のまま）。
/// 「ブックマーク」メニュー・ブックマークビューの項目も対象に入れない（バーと同じ機能だが別経路であり、
/// そちらは標準の Windows の挙動のままにする）。
/// </summary>
internal sealed class ConfinedMenuItem(int level) : ToolStripMenuItem
{
    protected override bool ProcessDialogKey(Keys keyData)
    {
        var forward = keyData == Keys.Right;
        if ((forward || keyData == Keys.Left) && BookmarkRules.SwallowArrowKey(level, HasDropDownItems, forward))
            return true;   // 呑み込むだけ。バー本体の ProcessDialogKey（ボタン間の移動）まで渡さない

        // R-107: 1 段目の端（先頭で ↑・末尾で ↓）は、Esc と同じくバーのボタンへ戻す。
        // 「»」の一覧から開いたときは、ボタン（ownerItem）は「»」の一覧の中の項目で、そこへ選択が戻る。
        if (keyData is Keys.Up or Keys.Down
            && Owner is ToolStripDropDown dropDown && dropDown.OwnerItem is { } ownerItem && ownerItem.Owner is { } bar)
        {
            var selectable = dropDown.Items.Cast<ToolStripItem>().Where(i => i.Enabled && i is not ToolStripSeparator).ToList();
            var isFirst = selectable.Count > 0 && ReferenceEquals(selectable[0], this);
            var isLast = selectable.Count > 0 && ReferenceEquals(selectable[^1], this);
            if (BookmarkRules.ReturnsToBarButton(level, isFirst, isLast, down: keyData == Keys.Down))
            {
                BarKeyboardNav.ReturnToOwner(dropDown, ownerItem, bar);
                return true;
            }
        }

        return base.ProcessDialogKey(keyData);
    }
}
