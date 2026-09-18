using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Entries;
using ReTAC.Domain.Navigation;
using ReTAC.Shell;

namespace ReTAC.App;

/// <summary>
/// 項目を押したときの処理。MainForm が実装する。バー・ブックマークメニュー・展開表示から MainForm の中を直接呼ばせないための境界
/// （管理ダイアログは BookmarkSet を直接扱い、ここを使わない）。
/// </summary>
public interface IBookmarkHost
{
    void JumpTo(string folder);
    void OpenFile(string path);
    void Execute(CommandTarget target);
    /// <summary>ブックマークそのものの登録先が無い。QuickAccessFixMissing に従って取り除くか知らせる（R-89）。</summary>
    void BookmarkMissing(Bookmark bookmark);
    /// <summary>展開表示の中の項目が、開いた後に消えていた。知らせるだけで、ブックマークには触れない（R-91）。</summary>
    void PathMissing(string path);
    string LabelOf(CommandTarget target);
    /// <summary>ツールチップに出す割り当て済みのキー。無ければ ""。</summary>
    string KeyOf(CommandTarget target);
    /// <summary>外部ツールのアイコンを取るパス（実行ファイル）。組み込みコマンドは null。</summary>
    string? IconPathOf(CommandTarget target);
    /// <summary>
    /// R-91: 展開表示の中身。今のソートと「表示するファイルタイプ」で同期的に列挙するだけ。
    /// 裏のスレッドで呼ぶこと・待つ時間の上限・古い結果を捨てることは FolderExpansion が受け持つ。
    /// </summary>
    IReadOnlyList<Entry> Enumerate(string folder);
}

/// <summary>R-89 / R-90 / R-91: ブックマークを ToolStripItem にする。バーのボタンとメニューの項目で同じ規則を使う。</summary>
public sealed class BookmarkItems(IBookmarkHost host, Control invoker)
{
    public IBookmarkHost Host => host;

    /// <summary>裏で取った結果を UI スレッドへ戻す窓（MainForm）。</summary>
    internal Control Invoker => invoker;

    /// <summary>メニューの中に並べる項目（グループは ▶ で入れ子、フォルダは展開表示）。</summary>
    public ToolStripItem[] MenuItems(IReadOnlyList<Bookmark> bookmarks)
    {
        var items = bookmarks.Select(b => Configure(new ToolStripMenuItem(), b)).ToArray();
        LoadIcons(items);
        return items;
    }

    /// <summary>バーのボタン（フォルダ・グループは押すと開く、ファイル・コマンドは押すと実行）。</summary>
    public ToolStripItem[] BarItems(IReadOnlyList<Bookmark> bookmarks, BookmarkBarStyle style)
    {
        var items = bookmarks.Select(b =>
        {
            ToolStripItem item = b.Kind is BookmarkKind.Folder or BookmarkKind.Group ? new ToolStripDropDownButton() : new ToolStripButton();
            item.DisplayStyle = style switch
            {
                BookmarkBarStyle.TextOnly => ToolStripItemDisplayStyle.Text,
                // アイコンの無い項目（組み込みコマンドなど）は、空のボタンにしないよう名前を出す
                BookmarkBarStyle.IconOnly when IconPath(b) is null => ToolStripItemDisplayStyle.Text,
                BookmarkBarStyle.IconOnly => ToolStripItemDisplayStyle.Image,
                _ => ToolStripItemDisplayStyle.ImageAndText,
            };
            // 隣のボタンとの間を左右 1px ずつ空ける（並ぶと 2px。詰まって見えるという実機指摘）
            item.Margin = new Padding(item.Margin.Left + 1, item.Margin.Top, item.Margin.Right + 1, item.Margin.Bottom);
            return Configure(item, b);
        }).ToArray();
        LoadIcons(items);
        return items;
    }

    private ToolStripItem Configure(ToolStripItem item, Bookmark b)
    {
        var name = BookmarkRules.DisplayName(b, host.LabelOf);
        item.Text = name.Replace("&", "&&");
        item.Tag = b;
        item.ToolTipText = Tooltip(b, name);
        switch (b.Kind)
        {
            case BookmarkKind.Folder:
                FolderExpansion.Attach((ToolStripDropDownItem)item, b.Target, this, onMissing: () => host.BookmarkMissing(b));
                break;
            case BookmarkKind.Group:
                AttachGroup((ToolStripDropDownItem)item, b);
                break;
            case BookmarkKind.File:
                item.Click += (_, _) =>
                {
                    if (File.Exists(b.Target)) host.OpenFile(b.Target);
                    else host.BookmarkMissing(b);
                };
                break;
            case BookmarkKind.Command:
                item.Click += (_, _) => { if (CommandTarget.Parse(b.Target) is { } target) host.Execute(target); };
                break;
        }
        return item;
    }

    /// <summary>R-89: 名前と、その登録先に割り当て済みのキー（キーボード操作への導線）。フォルダ・ファイルはパスも出す。</summary>
    private string Tooltip(Bookmark b, string name)
    {
        var key = b.Kind == BookmarkKind.Command && CommandTarget.Parse(b.Target) is { } target ? host.KeyOf(target) : "";
        var lines = new List<string> { key.Length > 0 ? $"{name}（{key}）" : name };
        if (b.Kind is BookmarkKind.Folder or BookmarkKind.File) lines.Add(b.Target);
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>グループの中身は開くたびに組み直す（入れ子は何段でも）。</summary>
    private void AttachGroup(ToolStripDropDownItem item, Bookmark group)
    {
        item.DropDownItems.Add(Placeholder("（空）"));   // 項目が無いと ▶ が出ず、開けない
        ToolStripExtras.EnableWheel(item.DropDown);
        item.DropDownOpening += (_, _) =>
        {
            Clear(item.DropDownItems);
            var children = group.Children ?? [];
            if (children.Count == 0) item.DropDownItems.Add(Placeholder("（空）"));
            else item.DropDownItems.AddRange(MenuItems(children));
            MenuSpacing.Apply(item.DropDownItems, invoker.DeviceDpi);   // R-88
        };
    }

    /// <summary>
    /// 項目を捨てる。開くたびに作り直すので、Clear だけだと項目とアイコンの Bitmap（GDI ハンドル）が積み上がる。
    /// </summary>
    internal static void Clear(ToolStripItemCollection items)
    {
        foreach (var item in items.Cast<ToolStripItem>().ToList())
        {
            item.Image?.Dispose();
            item.Dispose();   // 親のコレクションからも外れる
        }
        items.Clear();
    }

    internal static ToolStripMenuItem Placeholder(string text) => new(text) { Enabled = false };

    /// <summary>アイコンは応答しないドライブで待たされるので裏で取る（N-05）。取れたら差し替える。</summary>
    internal void LoadIcons(IReadOnlyList<ToolStripItem> items)
    {
        var jobs = items.Select(item => (item, path: IconPath(item.Tag))).Where(j => j.path is not null).ToList();
        if (jobs.Count == 0) return;
        if (!invoker.IsHandleCreated)
        {
            // 起動直後はまだ窓が無く、結果を UI スレッドへ戻せない。窓ができてから読む
            EventHandler? later = null;
            later = (_, _) => { invoker.HandleCreated -= later; LoadIcons(items); };
            invoker.HandleCreated += later;
            return;
        }
        var size = 16 * invoker.DeviceDpi / 96;
        Task.Run(() =>
        {
            using var icons = new ShellIcons(size);
            return jobs.Select(j => (j.item, image: icons.ForPath(j.path!) is { } b ? new Bitmap(b) : null)).ToList();
        }).ContinueWith(task =>
        {
            if (!task.IsCompletedSuccessfully || invoker.IsDisposed) return;
            invoker.BeginInvoke(() =>
            {
                // 1 件ずつ差し替えるたびにメニュー全体を並べ直すと、数百件で固まる。並べ直しを止めてまとめて入れる
                var owners = task.Result.Select(r => r.item.Owner).OfType<ToolStrip>().Distinct().ToList();
                foreach (var owner in owners) owner.SuspendLayout();
                foreach (var (item, image) in task.Result)
                {
                    if (image is null) continue;
                    if (item.IsDisposed) { image.Dispose(); continue; }
                    item.Image = image;
                }
                foreach (var owner in owners) owner.ResumeLayout();
            });
        });
    }

    private string? IconPath(object? tag) => tag switch
    {
        Bookmark { Kind: BookmarkKind.Folder or BookmarkKind.File } b => b.Target,
        Bookmark { Kind: BookmarkKind.Command } b when CommandTarget.Parse(b.Target) is { } t => host.IconPathOf(t),
        Entry entry => entry.FullPath,
        _ => null,
    };
}
