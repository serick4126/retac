namespace ReTAC.Updater.Core;

/// <summary>
/// R-109: ReTAC とアップデータの間の約束（版をまたぐ約束）。<b>版をまたいで変えない。</b>
/// ReTAC 側（ReTAC.App.UpdateProtocol）にも同じ値の定数があり、テストで両方がこの値と一致することを確かめる。
/// </summary>
public static class Protocol
{
    /// <summary>終了依頼のメッセージ名（RegisterWindowMessage）。</summary>
    public const string MessageName = "ReTAC.QuitForUpdate";

    /// <summary>送り先の目印のプロパティ名（SetProp）。</summary>
    public const string WindowPropName = "ReTAC.MainWindow";

    /// <summary>終了依頼を受け付けた。</summary>
    public const int Accepted = 1;

    /// <summary>終了依頼を断った（ダイアログを開いている・ファイル操作の最中）。</summary>
    public const int Refused = 2;

    public const string ReTacExe = "ReTAC.exe";
    public const string UpdaterExe = "ReTAC.Updater.exe";

    /// <summary>U7: 絶対に上書きしない。</summary>
    public const string SettingsFile = "retac.settings.json";

    /// <summary>R-109-5: 昇格したプロセスに置き換えだけを行わせる引数の先頭。</summary>
    public const string ReplaceSwitch = "--replace";
}

/// <summary>R-109-4: 置き換えの結果。昇格したプロセスでは終了コードの下位 4 ビットに載せる。</summary>
public enum ReplaceResult
{
    /// <summary>すべてのファイルを入れ替えた。</summary>
    Completed = 0,

    /// <summary>インストール先の正規の名前のファイルは変わっていない。</summary>
    Unchanged = 1,

    /// <summary>いくつかのファイルは新しく、ReTAC.exe を含む残りは旧版のまま。</summary>
    Partial = 2,
}

/// <summary>R-109: 結果に添える理由。昇格したプロセスでは終了コードの上位に載せる。番号は同じ exe の中の約束。</summary>
public enum Reason
{
    None = 0,
    /// <summary>GitHub に接続できない・名前を解決できない。</summary>
    CannotConnect = 1,
    /// <summary>GitHub API の問い合わせの上限。</summary>
    RateLimited = 2,
    /// <summary>GitHub がエラーを返した。</summary>
    GitHubError = 3,
    /// <summary>照合が合わない・zip として読めない・項目が準備フォルダの外を指す。</summary>
    Corrupt = 4,
    /// <summary>配布物の名前の外のファイル・フォルダ・重複・exe の欠け。</summary>
    UnsupportedFormat = 5,
    /// <summary>別のアップデータが置き換えの排他を持っている。</summary>
    OtherUpdater = 6,
    /// <summary>利用者がやめた。</summary>
    Cancelled = 7,
    /// <summary>UAC で許可されなかった。</summary>
    ElevationDenied = 8,
    /// <summary>ファイルを置けなかった・入れ替えられなかった。</summary>
    WriteFailed = 9,
    /// <summary>ダウンロードが 30 秒止まった・問い合わせが 15 秒で返らない。</summary>
    TimedOut = 10,
    /// <summary>リリースに目的の zip や照合の値が無い。</summary>
    AssetMissing = 11,
    /// <summary>想定外の例外。</summary>
    Unexpected = 12,
}

/// <summary>R-109-2: 取得・照合・展開の失敗。理由に、表示に添える詳細（HTTP の状態コードなど）と、レート制限のやり直せる時刻を付ける。</summary>
public sealed class FetchFailure
{
    public FetchFailure(Reason reason, string? detail = null, string? retryAt = null)
    {
        Reason = reason;
        Detail = detail;
        RetryAt = retryAt;
    }

    public Reason Reason { get; }

    /// <summary>技術的な詳細。結果の文の下に小さく添える。</summary>
    public string? Detail { get; }

    /// <summary>レート制限のとき、やり直せる時刻（HH:mm）。分からなければ null。</summary>
    public string? RetryAt { get; }
}

/// <summary>R-109-5: 昇格したプロセスの終了コード。下位 4 ビットが結果、その上が理由。</summary>
public static class ExitCodes
{
    public static int Encode(ReplaceResult result, Reason reason) => ((int)reason << 4) | (int)result;

    /// <summary>決まった形でなければ null（異常終了など）。そのときは親がインストール先の実物で判定し直す。</summary>
    public static (ReplaceResult Result, Reason Reason)? Decode(int code)
    {
        if (code < 0) return null;
        var result = code & 0xF;
        var reason = code >> 4;
        if (result > (int)ReplaceResult.Partial || reason > (int)Reason.Unexpected) return null;
        return ((ReplaceResult)result, (Reason)reason);
    }
}
