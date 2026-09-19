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
    /// <summary>表示の上限。超えた分は末尾の 1 項目でジャンプへ誘う。パンくずの ▸ の一覧（R-94）も同じ。</summary>
    internal const int MaxItems = 500;

    internal enum LoadOutcome { Loaded, Missing, Failed }

    /// <summary>
    /// フォルダの中身を裏で読み、待つ上限までに読めた結果を UI スレッドで渡す。展開表示とパンくずの ▸ の一覧（R-94）で共通の<b>機構だけ</b>。
    /// 何を出すか（ファイルも出すか、太字にするか）は呼び出し側が決める。
    /// 上限を過ぎたら Failed を 1 回だけ渡し、後から読み終えた結果は捨てる（閉じたメニューを書き換えない）。
    /// </summary>
    /// <param name="stillWanted">UI スレッドで結果を渡す直前に確かめる。閉じた・開き直した・捨てられたなら false</param>
    internal static void Load(Control invoker, string folder, Func<string, IReadOnlyList<Entry>> enumerate,
                              Func<bool> stillWanted, Action<LoadOutcome, IReadOnlyList<Entry>> apply)
    {
        var enumeration = Task.Run(() => List(folder, enumerate));
        Task.WhenAny(enumeration, Task.Delay(MainForm.EnumerationTimeout)).ContinueWith(done =>
        {
            // 結果は窓（フォーム）経由で UI スレッドへ戻す。閉じたドロップダウンは窓を持たないことがある
            if (invoker.IsDisposed || !invoker.IsHandleCreated) return;
            try
            {
                invoker.BeginInvoke(() =>
                {
                    if (!stillWanted()) return;
                    if (done.Result != enumeration || !enumeration.IsCompletedSuccessfully) apply(LoadOutcome.Failed, []);
                    else if (enumeration.Result is not { } entries) apply(LoadOutcome.Missing, []);
                    else apply(LoadOutcome.Loaded, entries);
                });
            }
            catch (InvalidOperationException) { }   // 確かめた直後に窓が閉じた
        });
    }

    /// <param name="onMissing">開こうとしたフォルダ自体が無いとき</param>
    public static void Attach(ToolStripDropDownItem item, string folder, BookmarkItems items, Action onMissing)
    {
        var host = items.Host;
        // 開くたびに進める。裏の列挙の結果が、閉じた後や開き直した後に届いたら捨てる
        var generation = 0;
        item.DropDownItems.Add(BookmarkItems.Placeholder("読み込み中…"));   // 項目が無いと ▶ が出ず、開けない
        ToolStripExtras.EnableWheel(item.DropDown);   // 数百件を ▲▼ だけで送らせない（実機指摘）
        ExpansionDropZone.Attach(item.DropDown, folder, host);   // R-93: 中へファイルを落として転送する

        item.DropDownOpening += (_, _) =>
        {
            var mine = ++generation;
            BookmarkItems.Clear(item.DropDownItems);
            // 存在の確認も裏で行う。止まった HDD や届かないネットワークでは、確認だけで UI が止まる（R-91）
            var jump = new ToolStripMenuItem("このフォルダへジャンプ(&J)");
            jump.Click += (_, _) => host.JumpTo(folder);
            var loading = BookmarkItems.Placeholder("読み込み中…");
            item.DropDownItems.AddRange([jump, new ToolStripSeparator(), loading]);
            MenuSpacing.Apply(item.DropDownItems, items.Invoker.DeviceDpi);   // R-88

            Load(items.Invoker, folder, host.Enumerate,
                // 古い結果で書き換えない（閉じた・開き直した・捨てられた）
                stillWanted: () => mine == generation && !item.IsDisposed && item.DropDown.Visible,
                apply: (outcome, entries) =>
                {
                    var index = item.DropDownItems.IndexOf(loading);
                    if (index < 0) return;
                    loading.Dispose();
                    switch (outcome)
                    {
                        case LoadOutcome.Failed:
                            // 読めなかっただけでは消えたと見なさない。ブックマークは残す
                            item.DropDownItems.Insert(index, BookmarkItems.Placeholder("読み込めませんでした"));
                            break;
                        case LoadOutcome.Missing:
                            item.DropDownItems.Insert(index, BookmarkItems.Placeholder("見つかりません"));
                            // 開いているメニューの処理中にメッセージを出すとメニューの状態が乱れる。後に回す
                            items.Invoker.BeginInvoke(onMissing);
                            break;
                        default:
                            items.LoadIcons(Fill(item, index, entries, folder, items));
                            break;
                    }
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

    /// <returns>中身。フォルダが消えていれば null。届かない・読めないときは例外（消えたとは見なさない）</returns>
    private static IReadOnlyList<Entry>? List(string folder, Func<string, IReadOnlyList<Entry>> enumerate)
    {
        if (Directory.Exists(folder)) return enumerate(folder);
        // 届かないネットワークや外したドライブでも Exists は false を返す。ルートが見えるときだけ「消えた」とする
        if (Path.GetPathRoot(folder) is { Length: > 0 } root && Directory.Exists(root)) return null;
        throw new DirectoryNotFoundException(folder);
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
            items.AttachContextMenu(menu);
            if (entry.Kind == EntryKind.Folder)
            {
                Attach(menu, entry.FullPath, items, onMissing: () => host.PathMissing(entry.FullPath));
            }
            else
            {
                menu.Click += (_, e) =>
                {
                    if (BookmarkItems.IsRightClick(e)) return;
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
        // 開いているメニューに 1 件ずつ足すたびに並べ直さないよう、余白は入れる前に掛け、まとめて足す
        MenuSpacing.Apply(added, items.Invoker.DeviceDpi);   // R-88
        parent.DropDown.SuspendLayout();
        for (var i = 0; i < added.Count; i++) parent.DropDownItems.Insert(index + i, added[i]);
        parent.DropDown.ResumeLayout();
        return added;
    }
}
