using System.IO;
using System.Windows.Forms;
using ReTAC.Domain.Entries;

namespace ReTAC.App;

/// <summary>
/// R-91: ブックマークのフォルダは、押してもジャンプせずに中身をメニューで展開する（旧タスクバーのツールバーと同じ）。
/// 先頭の「このフォルダへジャンプ」でジャンプする。中のフォルダはホバーでもクリックでも展開し、クリックでジャンプしない。
/// 中身は開くたびに読み、開いている間は更新しない（押し間違いを防ぐ）。
/// </summary>
public static class FolderExpansion
{
    /// <summary>表示の上限。超えた分は末尾の 1 項目でジャンプへ誘う。</summary>
    private const int MaxItems = 500;

    /// <param name="onMissing">開こうとしたフォルダ自体が無いとき</param>
    public static void Attach(ToolStripDropDownItem item, string folder, BookmarkItems items, Action onMissing)
    {
        var host = items.Host;
        // 開くたびに進める。裏の列挙の結果が、閉じた後や開き直した後に届いたら捨てる
        var generation = 0;
        item.DropDownItems.Add(BookmarkItems.Placeholder("読み込み中…"));   // 項目が無いと ▶ が出ず、開けない

        item.DropDownOpening += (_, _) =>
        {
            var mine = ++generation;
            BookmarkItems.Clear(item.DropDownItems);
            if (!Directory.Exists(folder))
            {
                item.DropDownItems.Add(BookmarkItems.Placeholder("見つかりません"));
                // 開く処理の途中でメッセージを出すとメニューの状態が乱れる。後に回す
                items.Invoker.BeginInvoke(onMissing);
                return;
            }

            var jump = new ToolStripMenuItem("このフォルダへジャンプ(&J)");
            jump.Click += (_, _) => host.JumpTo(folder);
            var loading = BookmarkItems.Placeholder("読み込み中…");
            item.DropDownItems.AddRange([jump, new ToolStripSeparator(), loading]);
            MenuSpacing.Apply(item.DropDownItems, items.Invoker.DeviceDpi);   // R-88

            var enumeration = Task.Run(() => host.Enumerate(folder));
            Task.WhenAny(enumeration, Task.Delay(MainForm.EnumerationTimeout)).ContinueWith(done =>
            {
                // 結果は窓（フォーム）経由で UI スレッドへ戻す。閉じたドロップダウンは窓を持たないことがある
                if (items.Invoker is not { IsDisposed: false } owner) return;
                owner.BeginInvoke(() =>
                {
                    // 古い結果で書き換えない（閉じた・開き直した・捨てられた）
                    if (mine != generation || item.IsDisposed || !item.DropDown.Visible) return;
                    var index = item.DropDownItems.IndexOf(loading);
                    if (index < 0) return;
                    loading.Dispose();

                    if (done.Result != enumeration || !enumeration.IsCompletedSuccessfully)
                    {
                        item.DropDownItems.Insert(index, BookmarkItems.Placeholder("読み込めませんでした"));
                        return;
                    }
                    var added = Fill(item, index, enumeration.Result, folder, items);
                    MenuSpacing.Apply(item.DropDownItems, owner.DeviceDpi);
                    items.LoadIcons(added);
                });
            });
        };
        item.DropDownClosed += (_, _) =>
        {
            generation++;
            // 閉じたら中身を捨てる。次に開いたときに読み直す
            items.Invoker.BeginInvoke(() =>
            {
                if (item.IsDisposed || item.DropDown.Visible) return;
                BookmarkItems.Clear(item.DropDownItems);
                item.DropDownItems.Add(BookmarkItems.Placeholder("読み込み中…"));
            });
        };
    }

    private static List<ToolStripItem> Fill(ToolStripDropDownItem parent, int index, IReadOnlyList<Entry> entries,
                                            string folder, BookmarkItems items)
    {
        var host = items.Host;
        var shown = entries.Where(e => !e.IsParent).ToList();
        var added = new List<ToolStripItem>();
        foreach (var entry in shown.Take(MaxItems))
        {
            var menu = new ToolStripMenuItem(entry.Name.Replace("&", "&&")) { Tag = entry };
            if (entry.Kind == EntryKind.Folder)
            {
                Attach(menu, entry.FullPath, items, onMissing: () => host.PathMissing(entry.FullPath));
            }
            else
            {
                menu.Click += (_, _) =>
                {
                    if (File.Exists(entry.FullPath)) host.OpenFile(entry.FullPath);
                    else host.PathMissing(entry.FullPath);
                };
            }
            added.Add(menu);
        }
        if (shown.Count == 0) added.Add(BookmarkItems.Placeholder("（空）"));
        if (shown.Count > MaxItems)
        {
            var more = new ToolStripMenuItem($"ほか {shown.Count - MaxItems} 件 — このフォルダへジャンプ");
            more.Click += (_, _) => host.JumpTo(folder);
            added.Add(more);
        }
        // 開いているメニューに 1 件ずつ足すたびに並べ直さないよう、まとめて足す
        parent.DropDown.SuspendLayout();
        for (var i = 0; i < added.Count; i++) parent.DropDownItems.Insert(index + i, added[i]);
        parent.DropDown.ResumeLayout();
        return added;
    }
}
