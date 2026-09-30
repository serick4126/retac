using System.Diagnostics;
using System.IO;

namespace ReTAC.App;

/// <summary>
/// R-125: 転送が始まるまでの時間を、区間ごとに測る。環境変数 RETAC_TRANSFER_TIMING があるときだけ働き、
/// 一時フォルダの retac-transfer-timing.log に 1 回の転送を 1 行で追記する。
/// 直す前と後、エクスプローラーとの違いを、利用者の環境（ネットワークドライブ）で比べるためのもの。設定にはしない。
/// </summary>
internal sealed class TransferTiming
{
    public const string Variable = "RETAC_TRANSFER_TIMING";
    public static string LogPath => Path.Combine(Path.GetTempPath(), "retac-transfer-timing.log");

    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private readonly List<string> _parts = [];
    private long _last;

    public static TransferTiming? Start() =>
        Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } ? new TransferTiming() : null;

    /// <summary>前の Mark（無ければ開始）からここまでを、この名前の区間として覚える。</summary>
    public void Mark(string name)
    {
        var now = _watch.ElapsedMilliseconds;
        _parts.Add($"{name}={now - _last}ms");
        _last = now;
    }

    internal string Format(string summary) => $"{summary} {string.Join(' ', _parts)}";

    public void Write(string summary)
    {
        // 測るための記録で転送を止めない
        try { File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {Format(summary)}{Environment.NewLine}"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
