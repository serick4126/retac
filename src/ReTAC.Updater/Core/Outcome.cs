namespace ReTAC.Updater.Core;

/// <summary>
/// R-109-4: 結果の表示。置き換えの結果 × 起動したか × 起動の成否 から、文を 1 つに決める。
/// 画面の文は短く（U13）。
/// </summary>
public static class Outcome
{
    /// <summary>
    /// R-109-4: 最後に ReTAC.exe を起動するか。ReTAC を 1 つ以上終了させた、または入れ替えを 1 つ以上行ったとき。
    /// ReTAC を使えない状態のまま終わらないため。
    /// </summary>
    public static bool ShouldLaunch(bool stoppedAnyReTac, int replacedCount) => stoppedAnyReTac || replacedCount > 0;

    /// <summary>
    /// R-109-6: 画面を閉じたときに ReTAC を起動するか。作業が動いている間に閉じたときは、入れ替えに入っていれば起動する
    /// （入れ替えの途中でも正規の名前には完全なファイルがある）。作業が終わった後の結果の画面を閉じるときは、ここでは起動しない
    /// （起動するかは結果を出す前に <see cref="ShouldLaunch"/> で決め済み）。
    /// </summary>
    /// <param name="workRunning">置き換えの作業（自分の作業用のスレッド・昇格したプロセス）が動いている</param>
    /// <param name="finished">結果を出し終えている</param>
    /// <param name="stoppedAnyReTac">ReTAC を 1 つ以上終了させた</param>
    /// <param name="replacingStarted">入れ替えの段に入った（昇格したプロセスは親から見えないので、動いていれば入ったとみなす）</param>
    public static bool LaunchOnClose(bool workRunning, bool finished, bool stoppedAnyReTac, bool replacingStarted)
    {
        if (finished) return false;
        return stoppedAnyReTac || (workRunning && replacingStarted);
    }

    /// <summary>結果の文。</summary>
    /// <param name="launched">ReTAC を起動したか（<see cref="ShouldLaunch"/>）</param>
    /// <param name="launchSucceeded">起動に成功したか。起動していなければ見ない</param>
    /// <param name="version">入れようとした版（例: v2.8.0）</param>
    /// <param name="retryAt">レート制限のとき、やり直せる時刻</param>
    public static string Message(ReplaceResult result, Reason reason, bool launched, bool launchSucceeded, string version,
                                 string? retryAt = null)
    {
        var launchFailed = launched && !launchSucceeded;
        switch (result)
        {
            case ReplaceResult.Completed:
                return launchFailed
                    ? $"{version} に更新しましたが、ReTAC を起動できませんでした。"
                    : $"{version} に更新しました。";
            case ReplaceResult.Partial:
                return launchFailed
                    ? "一部のファイルを更新できませんでした。ReTAC も起動できませんでした。もう一度更新してください。"
                    : "一部のファイルを更新できませんでした。もう一度更新してください。";
            default:
                var why = ReasonText(reason, retryAt);
                return launchFailed
                    ? $"{why}。ReTAC は変更していませんが、起動できませんでした。"
                    : $"{why}。ReTAC は変更していません。";
        }
    }

    /// <summary>「変更なし」に添える理由。末尾の句点は付けない（<see cref="Message"/> が付ける）。</summary>
    public static string ReasonText(Reason reason, string? retryAt = null) => reason switch
    {
        Reason.CannotConnect => "GitHub に接続できませんでした",
        Reason.RateLimited => retryAt is null
            ? "GitHub への問い合わせが多すぎます。しばらくしてからやり直してください"
            : $"GitHub への問い合わせが多すぎます。{retryAt} 以降にやり直してください",
        Reason.GitHubError => "GitHub から更新の情報を取得できませんでした",
        Reason.Corrupt => "ダウンロードしたファイルが壊れています",
        Reason.UnsupportedFormat => "このアップデータでは更新できない形式です。手動で更新してください",
        Reason.OtherUpdater => "別のアップデータがファイルを入れ替えています。終わらないときは Windows を再起動してください",
        Reason.Cancelled => "更新を中止しました",
        Reason.ElevationDenied => "管理者の許可が得られませんでした",
        Reason.WriteFailed => "ファイルを置けませんでした",
        Reason.TimedOut => "GitHub からの応答が止まりました",
        Reason.AssetMissing => "GitHub に更新用のファイルがありません",
        Reason.Unexpected => "更新の途中で問題が起きました",
        _ => "更新しませんでした",
    };
}
