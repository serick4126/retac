using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ReTAC.App;

/// <summary>
/// 「終了後もウィンドウを閉じない」（F-03 / 仕様書 §8 I-2）。<c>ReTAC.exe --hold &lt;実行ファイル&gt; &lt;引数…&gt;</c>。
/// コンソールを作ってその中でツールを動かし、終わったらキーを押すまで結果を残す。
/// 引数は 1 つずつ渡し、シェルに解釈させない（<c>cmd /k</c> に文字列でつなぐと、1 つの値が 1 つの引数にならない）。
/// 作業ディレクトリは起動した側（ReTAC）が渡したものを、そのままツールに引き継ぐ。
/// </summary>
internal static class ConsoleHold
{
    public const string Switch = "--hold";

    /// <summary>ERROR_ELEVATION_REQUIRED。マニフェストで管理者権限を求めるツール</summary>
    private const int ElevationRequired = 740;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    public static int Run(string exe, string[] args)
    {
        AllocConsole();   // ReTAC は GUI の実行ファイルなのでコンソールを持っていない

        int code;
        try
        {
            code = RunAndWait(exe, args, shell: false);   // 作ったコンソールをツールに引き継ぐ
        }
        catch (Win32Exception ex)
        {
            // 740: 管理者権限が要る → UAC に任せる。昇格したツールは自分のコンソールを持つ。
            // それ以外（193 など）: 実行ファイルでないもの（ショートカット・関連付けで開くファイル）→ シェルに任せる
            if (ex.NativeErrorCode != ElevationRequired)
                Console.WriteLine($"（{exe} を直接起動できないため、Windows に任せて開きます）");
            code = TryRun(exe, args, shell: true);
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"{exe} を起動できませんでした。");
            Console.WriteLine(ex.Message);
            code = -1;
        }

        Console.WriteLine();
        // 正常終了なら終了コードは出さない（0 という数字だけを見せても利用者は判断できない）
        Console.Write(code == 0 ? "何かキーを押すと閉じます。" : $"終了コード {code}。何かキーを押すと閉じます。");
        Console.ReadKey(intercept: true);
        return code;
    }

    private static int TryRun(string exe, string[] args, bool shell)
    {
        try
        {
            return RunAndWait(exe, args, shell);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Console.WriteLine($"{exe} を起動できませんでした。");
            Console.WriteLine(ex.Message);
            return -1;
        }
    }

    private static int RunAndWait(string exe, string[] args, bool shell)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = shell };
        foreach (var argument in args) info.ArgumentList.Add(argument);
        using var process = Process.Start(info);
        if (process is null) return 0;
        process.WaitForExit();
        return process.ExitCode;
    }
}
