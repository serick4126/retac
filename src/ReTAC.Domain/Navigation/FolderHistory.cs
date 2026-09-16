namespace ReTAC.Domain.Navigation;

/// <summary>
/// フォルダ履歴（0x82FD）と前後フォルダ（0x832D / 0x832E）。
/// N-02: 移動の履歴とコピー先の履歴は共通のひとつ。宛先入力欄もこの履歴を引く。
/// </summary>
public sealed class FolderHistory
{
    /// <summary>過去 16 回分。</summary>
    public const int Capacity = 16;

    private readonly List<string> _recent = [];
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();

    /// <summary>新しい順。ポップアップ（N-06）と宛先入力欄の ↑ が引く。</summary>
    public IReadOnlyList<string> Recent => _recent;

    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;

    /// <summary>
    /// 移動が起きた。<paramref name="from"/> は移動前のカレントフォルダ（起動直後は null）。
    ///
    /// 積むのは<b>出ていったフォルダ</b>であって、着いたフォルダではない（卓駆と同じ）。
    /// 着いた先を積むと一覧の先頭が常に今いるフォルダになり、「さっきまでどこにいたか」が読めない。
    /// 今いるフォルダも、以前そこを出ていれば一覧に残る（卓駆もそう見えている）。
    /// </summary>
    public void Visit(string? from, string to)
    {
        if (from is null) return;   // 起動直後。まだどこからも出ていない
        Remember(from);
        if (PathEquals(from, to)) return;
        _back.Push(from);
        // 分岐したので進む先は捨てる（ブラウザと同じ作法）
        _forward.Clear();
    }

    /// <summary>移動を伴わない登録。コピー・移動の宛先を確定したときに使う（N-02）。</summary>
    public void Remember(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        _recent.RemoveAll(p => PathEquals(p, path));
        _recent.Insert(0, path);
        if (_recent.Count > Capacity) _recent.RemoveRange(Capacity, _recent.Count - Capacity);
    }

    /// <summary>
    /// 卓駆の「履歴のクリア」。一覧を空にする。
    /// 前後フォルダ（F8 / F9）は別の機能なので触らない。
    /// </summary>
    public void Clear() => _recent.Clear();

    /// <summary>存在しなくなったフォルダを履歴から外す。</summary>
    public void Forget(string path) => _recent.RemoveAll(p => PathEquals(p, path));

    /// <summary>F8。戻り先を返す。戻れないなら null。</summary>
    public string? Back(string current)
    {
        if (_back.Count == 0) return null;
        _forward.Push(current);
        return _back.Pop();
    }

    /// <summary>F9。進み先を返す。進めないなら null。</summary>
    public string? Forward(string current)
    {
        if (_forward.Count == 0) return null;
        _back.Push(current);
        return _forward.Pop();
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
