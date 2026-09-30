using System.IO;
using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>
/// R-126: 想定外の例外の詳細を、設定ファイルと同じフォルダの retac.error.log に追記する。
/// 公開後に「落ちた」「おかしなメッセージが出た」と報告を受けたときの手がかりにする。
/// 書くのは日時・経路・版・OS と、例外の ToString()（種類・メッセージ・スタック・内側の例外）。ファイルの中身は書かない。
/// <b>同期で書き切る</b>: 画面以外のスレッドの例外では、ここから戻るとプロセスが終わる。
/// 書けなくても例外を出さない（記録の失敗で本来の処理を止めない）。
/// </summary>
internal static class ErrorLog
{
    public const string FileName = "retac.error.log", OldFileName = "retac.error.old.log";

    /// <summary>これを超えていたら、今のファイルを古い方へ置き換えてから書く（2 世代だけ持つ）。</summary>
    public const long MaxBytes = 1024 * 1024;

    /// <summary>1 回の起動で、1 回だけの記録に載せる例外の数の上限（種類・メッセージの違うものを合わせて）。</summary>
    internal const int OnceLimit = 20;

    private static readonly object Gate = new();

    private static readonly HashSet<string> Seen = [];

    /// <summary>テストと、わざと例外を起こす起動の引数が差し替える。</summary>
    internal static string? FolderOverride { get; set; }

    /// <summary>設定ファイルを実際に読み書きしている場所（書けない実行ディレクトリから AppData へ逃げていれば、そちら。R-55-3）。</summary>
    private static string Folder =>
        FolderOverride ?? Path.GetDirectoryName(AppSettings.ActualPath ?? AppSettings.PrimaryPath)!;

    /// <returns>記録したファイルのパス。書けなければ null</returns>
    public static string? Write(Exception exception, string source) => Write(exception, source, Folder, DateTime.Now, null);

    /// <param name="wait">テストでの差し替え用（既定は Thread.Sleep）</param>
    internal static string? Write(Exception exception, string source, string folder, DateTime now, Action<int>? wait)
    {
        try
        {
            var path = Path.Combine(folder, FileName);
            var text = Format(exception, source, now);
            lock (Gate)
            {
                // R-55-3: 直前に書いたファイルをウイルス対策などが開いている間の一時的な失敗は、設定ファイルと同じやり方でやり直す
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        var file = new FileInfo(path);
                        if (file.Exists && file.Length > MaxBytes) File.Move(path, Path.Combine(folder, OldFileName), overwrite: true);
                        File.AppendAllText(path, text);
                        return path;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        if (attempt >= AppSettings.WriteAttempts) return null;
                        (wait ?? Thread.Sleep)(AppSettings.WriteRetryDelay);
                    }
                }
            }
        }
        catch (Exception) { return null; }   // 記録の失敗で本来の処理を止めない
    }

    /// <summary>
    /// 同じ経路・同じ種類・同じメッセージの例外は、1 回の起動につき 1 回だけ記録する。合わせて <see cref="OnceLimit"/> 件まで。
    /// 項目ごとに同じ例外が出る経路（サムネイルの取得）が、項目の数だけ記録しないようにする。
    /// </summary>
    /// <returns>記録したファイルのパス。記録しなかった（既に記録した・上限・書けない）なら null</returns>
    public static string? WriteOnce(Exception exception, string source) => WriteOnce(exception, source, Folder, DateTime.Now);

    internal static string? WriteOnce(Exception exception, string source, string folder, DateTime now)
    {
        lock (Seen)
        {
            if (Seen.Count >= OnceLimit || !Seen.Add($"{source}|{exception.GetType().FullName}|{exception.Message}")) return null;
        }
        return Write(exception, source, folder, now, null);
    }

    /// <summary>
    /// 結果を待たずに裏で走らせたタスクのうち、読めない・切断といった普通の失敗（IOException・UnauthorizedAccessException）だけを、
    /// ここで受け止めて何も出さない。受け止めないと、捨てられたタスクの例外として記録に載る（R-126）。
    /// 想定外の型が混ざっていれば、1 回だけ記録する。
    /// </summary>
    internal static Task IgnoreFileSystemFailure(Task task) =>
        task.ContinueWith(t =>
        {
            if (!t.Exception!.InnerExceptions.All(e => e is IOException or UnauthorizedAccessException))
                WriteOnce(t.Exception, "task");
        }, TaskContinuationOptions.OnlyOnFaulted);

    /// <summary>テスト用: 1 回だけの記録の覚えを消す。</summary>
    internal static void ResetOnce()
    {
        lock (Seen) Seen.Clear();
    }

    internal static string Format(Exception exception, string source, DateTime now)
    {
        var nl = Environment.NewLine;
        return $"==== {now:yyyy-MM-dd HH:mm:ss} {source}{nl}"
             + $"ReTAC {Application.ProductVersion} / {Environment.OSVersion} / .NET {Environment.Version}{nl}"
             + $"{exception}{nl}{nl}";
    }

    /// <summary>画面に出す文。今までどおり例外のメッセージ 1 行で、記録できたときだけ場所を 1 行添える（無いファイルを案内しない）。</summary>
    public static string Message(Exception exception, string? logPath) =>
        logPath is null ? exception.Message : $"{exception.Message}{Environment.NewLine}記録: {logPath}";
}
