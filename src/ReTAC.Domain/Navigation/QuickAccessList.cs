namespace ReTAC.Domain.Navigation;

/// <summary>クイックアクセスの登録項目。タイトル（フォルダの別名）＋ フォルダパスの対（16.2 節）。</summary>
public sealed record QuickAccessEntry(string Title, string Path);

/// <summary>
/// クイックアクセス（0x82FC）の登録リストと実行時オプション。
/// 設定・編集（0x82FA）と追加（0x832C）が触る対象でもある。
/// </summary>
public sealed class QuickAccessList
{
    private readonly List<QuickAccessEntry> _items = [];

    public IReadOnlyList<QuickAccessEntry> Items => _items;

    /// <summary>実行時オプション「タイトル（フォルダの別名）を表示する」。現行設定は ON（16.2 節）。</summary>
    public bool ShowTitles { get; set; } = true;

    /// <summary>実行時オプション「フォルダが存在しない時は自動でリストを修正する」。現行設定は OFF。</summary>
    public bool FixMissingAutomatically { get; set; }

    /// <summary>同じフォルダを二重に登録しない。登録できたら true。</summary>
    public bool Add(QuickAccessEntry entry)
    {
        if (IndexOfPath(entry.Path) >= 0) return false;
        _items.Add(entry);
        return true;
    }

    /// <summary>差し替えられたら true。他の項目と同じフォルダになる差し替えはしない。</summary>
    public bool Replace(int index, QuickAccessEntry entry)
    {
        if (!IsValid(index)) return false;
        var found = IndexOfPath(entry.Path);
        if (found >= 0 && found != index) return false;
        _items[index] = entry;
        return true;
    }

    /// <summary>同じフォルダを指す項目の位置。無ければ -1。</summary>
    public int IndexOfPath(string path) => _items.FindIndex(e => PathEquals(e.Path, path));

    public void RemoveAt(int index)
    {
        if (IsValid(index)) _items.RemoveAt(index);
    }

    /// <summary>設定ダイアログの ↑ ↓。端では動かさない。移動後の位置を返す。</summary>
    public int Move(int index, int delta)
    {
        var to = index + delta;
        if (!IsValid(index) || !IsValid(to)) return index;
        (_items[index], _items[to]) = (_items[to], _items[index]);
        return to;
    }

    /// <summary>ポップアップに出す文字列。タイトルを表示しない設定ならパスをそのまま出す。</summary>
    public string LabelOf(QuickAccessEntry entry) =>
        ShowTitles && !string.IsNullOrWhiteSpace(entry.Title) ? entry.Title : entry.Path;

    private bool IsValid(int index) => index >= 0 && index < _items.Count;

    private static bool PathEquals(string a, string b) =>
        string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
