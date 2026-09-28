using System;
using System.IO;
using System.Linq;
using System.Threading;
using ReTAC.Updater.Core;

namespace ReTAC.Updater;

/// <summary>
/// R-109-5: 昇格したプロセス。インストール先に書き込めないとき、親が runas で起動する。画面を持たない。
/// 親から受け取るのは版番号だけで、ファイルも照合の値も受け取らない。取得・照合・展開も自分で行い、
/// zip は保護された準備フォルダの中に置く（INV-UPDATER-VERIFY-IN-WRITER）。
/// ReTAC は起動しない（昇格したまま起動すると、エクスプローラーからのドロップが UIPI で拒まれる）。
/// </summary>
internal static class ElevatedReplace
{
    /// <summary>引数: --replace &lt;インストール先&gt; &lt;版 X.Y.Z&gt; &lt;作業フォルダ&gt; [開発用の指定]</summary>
    public static int Run(string[] args)
    {
        if (args.Length < 4) return ExitCodes.Encode(ReplaceResult.Unchanged, Reason.Unexpected);
        var install = args[1];
        var version = args[2];
        // 作業フォルダは progress.txt を書く場所で、表示にしか使わない。別の管理者で昇格すると %TEMP% が別になるので引数で受け取る
        var work = args[3];
        var options = Options.Parse(args.Skip(4).ToList());

        var normalized = InstallFolder.Normalize(install);
        if (normalized is null) return ExitCodes.Encode(ReplaceResult.Unchanged, Reason.WriteFailed);

        var progressFile = Path.Combine(work, "progress.txt");
        void Progress(string stage, string? file)
        {
            // 親は結果の判定には使わない（通常の権限から書き換えられるため）。進み具合の表示と、止まっていないかの判断だけ
            try { File.WriteAllText(progressFile, $"{DateTime.UtcNow:o}\t{stage}\t{file}"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        var fetch =
#if DEBUG
            options.LocalZip is not null
                ? Fetchers.FromLocalZip(options.LocalZip, options.LocalZipSha256!, downloadFolder: null, Progress) :
#endif
            Fetchers.FromGitHub(version, downloadFolder: null, Progress, CancellationToken.None);

        var request = new ReplaceRequest(install, fetch)
        {
            LockName = InstallFolder.ReplaceLockName(normalized),
            Progress = Progress,
        };
        options.ApplyDebugHooks(request, work);

        // このプロセスは画面を持たないので、メインのスレッドがそのまま作業用のスレッドになる（排他を取って手放す）
        var report = Replacer.Run(request);
        Progress("終わり", report.Detail);
        return ExitCodes.Encode(report.Result, report.Reason);
    }
}
