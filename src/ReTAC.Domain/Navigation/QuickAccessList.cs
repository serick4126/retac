namespace ReTAC.Domain.Navigation;

/// <summary>
/// クイックアクセスの登録項目。タイトル（フォルダの別名）＋ 登録先（16.2 節）。
/// R-92: Kind を足した。Kind の無い古い項目は Folder として読む（既定値）。Path は Folder / File ならパス、
/// Command なら CommandTarget.Serialize() の文字列（キー割り当ての保存と同じ形）。Group は使わない（読み込み時に落とす）。
/// </summary>
public sealed record QuickAccessEntry(string Title, string Path, BookmarkKind Kind = BookmarkKind.Folder);

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

    /// <summary>同じ登録先を二重に登録しない（種類ごと。R-92）。登録できたら true。</summary>
    public bool Add(QuickAccessEntry entry)
    {
        if (IndexOf(entry) >= 0) return false;
        _items.Add(entry);
        return true;
    }

    /// <summary>
    /// R-102-3: 中身をまるごと差し替える。INV-QUICKACCESS-SHARED によりこの実体（参照）は全ウィンドウで共有するので、
    /// 設定画面の確定では新しい QuickAccessList を作って差し替えるのではなく、この実体の中身だけを入れ替える。
    /// Add と同じ重複規則を通すので、渡した並びに重複が混ざっていても後勝ちで弾かれる。
    /// </summary>
    public void ReplaceAll(IEnumerable<QuickAccessEntry> entries)
    {
        _items.Clear();
        foreach (var entry in entries) Add(entry);
    }

    /// <summary>差し替えられたら true。他の項目と同じフォルダになる差し替えはしない。</summary>
    public bool Replace(int index, QuickAccessEntry entry)
    {
        if (!IsValid(index)) return false;
        var found = IndexOf(entry);
        if (found >= 0 && found != index) return false;
        _items[index] = entry;
        return true;
    }

    /// <summary>同じフォルダを指す項目の位置。無ければ -1。</summary>
    public int IndexOfPath(string path) => IndexOf(new QuickAccessEntry("", path));

    /// <summary>
    /// R-92: 同じ登録先を指す項目の位置。種類が同じもの同士だけ比べる（同じパスのフォルダとファイルは別物）。
    /// フォルダ・ファイルは末尾の \ と大文字小文字を無視し、コマンドは文字列が同じなら同じ。無ければ -1。
    /// </summary>
    public int IndexOf(QuickAccessEntry entry) => _items.FindIndex(e => e.Kind == entry.Kind
        && (entry.Kind == BookmarkKind.Command ? e.Path == entry.Path : PathEquals(e.Path, entry.Path)));

    /// <summary>
    /// Q4 / Q10: Group の項目と、消した外部ツール・読めないコマンドを指す項目を落とす。変わったら true。
    /// </summary>
    public bool DropUnknownTools(IEnumerable<int> existingToolIds)
    {
        var ids = existingToolIds.ToHashSet();
        return _items.RemoveAll(e => e.Kind == BookmarkKind.Group
            || (e.Kind == BookmarkKind.Command && !BookmarkRules.IsKnown(e.Path, ids))) > 0;
    }

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
