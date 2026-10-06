using ReTAC.Domain.Startup;

namespace ReTAC.App;

/// <summary>
/// R-26-2: 起動引数の存在確認を、待ち時間に上限を付けて背景で行う。
/// 応答しないネットワークのパスで止まらないように、上限を過ぎたら Invalid とみなす。
/// </summary>
internal static class StartupPathProbe
{
    public static readonly TimeSpan DefaultLimit = TimeSpan.FromSeconds(5);

    public static async Task<StartupPathResult> ClassifyAsync(string raw, string? normalized, TimeSpan limit,
        Func<string, bool> directoryExists, Func<string, bool> fileExists)
    {
        var work = Task.Run(() => StartupPath.Classify(raw, normalized, directoryExists, fileExists));
        // ponytail: ファイルシステム I/O は中断できない。待つのをやめるだけで、走っている確認は捨てる
        if (await Task.WhenAny(work, Task.Delay(limit)) == work && work.IsCompletedSuccessfully) return work.Result;
        _ = ErrorLog.IgnoreFileSystemFailure(work);   // 捨てた確認が後で失敗しても、異常終了の記録に載せない（R-126）
        return new(StartupPathKind.Invalid, null, null, raw);
    }
}
