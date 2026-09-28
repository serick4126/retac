using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace ReTAC.Updater.Core;

/// <summary>R-109-4: 置き換えの頼み方。</summary>
public sealed class ReplaceRequest
{
    public ReplaceRequest(string installFolder, Func<StagingFolder, Reason?> fetch)
    {
        InstallFolder = installFolder;
        Fetch = fetch;
    }

    /// <summary>インストール先（正規化する前のパスでよい）。</summary>
    public string InstallFolder { get; }

    /// <summary>
    /// 取得・照合・展開。準備フォルダへ書き出す。失敗の理由を返す（成功なら null）。
    /// INV-UPDATER-VERIFY-IN-WRITER: 置き換えの排他を取った後、このスレッドで呼ぶ。ほかのプロセスが用意したファイルを使わない。
    /// </summary>
    public Func<StagingFolder, Reason?> Fetch { get; }

    /// <summary>置き換えの排他の名前。null なら排他を取らない（同時に書く場合のテストだけ）。</summary>
    public string? LockName { get; set; }

    /// <summary>段と処理中のファイル名を知らせる（画面・progress.txt）。</summary>
    public Action<string, string?>? Progress { get; set; }

    /// <summary>
    /// i 個目（0 始まり）を入れ替える前に呼ぶ。すべて入れ替えた後にも i = 個数で呼ぶ。
    /// テストと開発用のビルドの「止まる」指定に使う（<see cref="SimulatedStop"/> を投げる・待つ）。
    /// </summary>
    public Action<int>? BeforeReplace { get; set; }

    /// <summary>照合・展開の後、入れ替えの前に呼ぶ（開発用のビルドの「照合の後で止まる」指定）。</summary>
    public Action? AfterFetch { get; set; }

    public int Attempts { get; set; } = 5;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
}

/// <summary>R-109-4: 置き換えの結果。</summary>
public sealed class ReplaceReport
{
    public ReplaceReport(ReplaceResult result, Reason reason, int replaced, IReadOnlyList<string> leftovers, string? detail)
    {
        Result = result;
        Reason = reason;
        Replaced = replaced;
        Leftovers = leftovers;
        Detail = detail;
    }

    public ReplaceResult Result { get; }
    public Reason Reason { get; }

    /// <summary>実際に入れ替えたファイルの数。</summary>
    public int Replaced { get; }

    /// <summary>前回までの準備フォルダの名前（消さずに知らせる）。</summary>
    public IReadOnlyList<string> Leftovers { get; }

    /// <summary>技術的な詳細（例外のメッセージ・止まったファイル名）。</summary>
    public string? Detail { get; }
}

/// <summary>テストと開発用のビルドの「止まる」指定だけが投げる。強制終了に見立て、片付けをせずに抜ける。</summary>
public sealed class SimulatedStop : Exception
{
}

/// <summary>
/// R-109-4: 置き換え。ファイルごとに MoveFileEx（MOVEFILE_REPLACE_EXISTING）で、準備フォルダのファイルを正規の名前へ付け替える。
/// 付け替えは 1 回の操作で、失敗すれば置き換え先はそのまま残る。File.Replace（ReplaceFile）は、退避先を渡さないと
/// ERROR_UNABLE_TO_MOVE_REPLACEMENT で置き換え先が元の名前から消えるので使わない。
/// ReTAC.exe を最後にする。旧版へ戻す仕組みは持たない（新旧が混ざっても使える。INV-UPDATER-ANY-MIX）。
/// <b>作業用のスレッドで呼ぶ。</b>置き換えの排他はこのスレッドが取って手放す（INV-UPDATER-WRITER-LOCK）。
/// </summary>
public static class Replacer
{
    public static ReplaceReport Run(ReplaceRequest request)
    {
        NamedLock? held = null;
        if (request.LockName is not null)
        {
            held = NamedLock.TryAcquire(request.LockName);
            if (held is null) return Report(ReplaceResult.Unchanged, Reason.OtherUpdater, 0, Array.Empty<string>(), null);
        }

        var replaced = 0;
        StagingFolder? staging = null;
        IReadOnlyList<string> leftovers = Array.Empty<string>();
        try
        {
            leftovers = StagingFolder.FindLeftovers(request.InstallFolder, null);

            request.Progress?.Invoke("準備", null);
            try { staging = StagingFolder.Create(request.InstallFolder); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Report(ReplaceResult.Unchanged, Reason.WriteFailed, 0, leftovers, ex.Message);
            }

            if (request.Fetch(staging) is { } failed)
            {
                staging.Cleanup();
                return Report(ReplaceResult.Unchanged, failed, 0, leftovers, null);
            }
            request.AfterFetch?.Invoke();

            // U7: 準備フォルダに入ることは無い（Distribution.Check が拒否する）が、念のためここでも名前で除く
            var order = Distribution.ReplaceOrder(
                staging.WrittenNames.Where(n => !string.Equals(n, Protocol.SettingsFile, StringComparison.OrdinalIgnoreCase)));

            for (var i = 0; i < order.Count; i++)
            {
                request.BeforeReplace?.Invoke(i);
                request.Progress?.Invoke("入れ替え", order[i]);
                var source = Path.Combine(staging.FullPath, order[i]);
                var target = Path.Combine(request.InstallFolder, order[i]);
                if (MoveWithRetry(source, target, request) is { } error)
                {
                    staging.Cleanup();
                    var result = replaced == 0 ? ReplaceResult.Unchanged : ReplaceResult.Partial;
                    return Report(result, Reason.WriteFailed, replaced, leftovers, $"{order[i]}: {error}");
                }
                replaced++;
            }
            request.BeforeReplace?.Invoke(order.Count);

            request.Progress?.Invoke("片付け", null);
            staging.Cleanup();
            return Report(ReplaceResult.Completed, Reason.None, replaced, leftovers, null);
        }
        catch (SimulatedStop)
        {
            throw;   // 強制終了に見立てる。片付けをしない
        }
        catch (Exception ex)
        {
            // 想定外の例外も、そのとき処理していた段から結果を決める
            staging?.Cleanup();
            var result = replaced == 0 ? ReplaceResult.Unchanged : ReplaceResult.Partial;
            return Report(result, Reason.Unexpected, replaced, leftovers, ex.Message);
        }
        finally
        {
            held?.Dispose();
        }
    }

    private static ReplaceReport Report(ReplaceResult result, Reason reason, int replaced, IReadOnlyList<string> leftovers, string? detail) =>
        new(result, reason, replaced, leftovers, detail);

    /// <summary>一時的に使用中で失敗したときは、決まった回数だけ間を置いて試す。失敗したら理由の文、成功なら null。</summary>
    private static string? MoveWithRetry(string source, string target, ReplaceRequest request)
    {
        var error = 0;
        for (var attempt = 0; attempt < request.Attempts; attempt++)
        {
            if (attempt > 0) Thread.Sleep(request.RetryDelay);
            if (MoveFileEx(source, target, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) return null;
            error = Marshal.GetLastWin32Error();
        }
        return new Win32Exception(error).Message;
    }

    // 同じドライブの中の付け替えなので中身はコピーされない。MOVEFILE_COPY_ALLOWED は付けない
    // （付けると別のドライブのときにコピーして消す動きになり、途中で止まると欠けたファイルが残りうる）
    private const uint MOVEFILE_REPLACE_EXISTING = 0x1;
    private const uint MOVEFILE_WRITE_THROUGH = 0x8;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "MoveFileExW")]
    private static extern bool MoveFileEx(string existing, string replacement, uint flags);
}
