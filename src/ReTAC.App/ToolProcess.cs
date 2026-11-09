using System.Diagnostics;
using ReTAC.Domain.Tools;
using ReTAC.Shell;

namespace ReTAC.App;

/// <summary>1 回分の起動の仕方を決める（F-03）。直接の起動も外部ツールキューもここを通す。</summary>
internal static class ToolProcess
{
    /// <summary>
    /// R-147: <see cref="ExternalTool.Path"/> は設定ファイルの形の式。置き換えて、名前だけなら PATH と App Paths から探す
    /// （絶対パスに限らない）。<paramref name="env"/> は必須（呼び忘れをコンパイルで捕まえる）。
    /// </summary>
    /// <returns>旧設定の値（<see cref="ToolPath.Result.Legacy"/>）は起動しないので null。呼び出し側が <see cref="ToolPath.LegacyNotice"/> を知らせる</returns>
    public static ProcessStartInfo? StartInfo(LaunchRequest request, Func<string, string?> env) =>
        StartInfo(request, ToolPath.Resolve(request.Tool.Path, env));

    private static ProcessStartInfo? StartInfo(LaunchRequest request, ToolPath.Result resolved)
    {
        if (resolved.Legacy) return null;
        // 見つからなければ書かれたまま Windows に任せる
        var path = resolved.LaunchPath;

        ProcessStartInfo info;
        // 見分けられないもの（.bat / .cmd・見つからない）はコンソールとして扱う（仕様書 F-03）
        if (request.Tool.KeepWindowOpen && ExecutableKinds.Of(path) != ExecutableKind.Gui)
        {
            info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            info.ArgumentList.Add(ConsoleHold.Switch);
            info.ArgumentList.Add(path);
        }
        else
        {
            // UseShellExecute: 管理者権限を求めるツールも UAC に任せて起動できる（F-03）
            info = new ProcessStartInfo(path) { UseShellExecute = true };
        }

        foreach (var argument in request.Arguments) info.ArgumentList.Add(argument);
        // R-37-3: 常にカレントフォルダ。260 超では 8.3 名に落ちる
        info.WorkingDirectory = ToolLauncher.WorkingDirectoryFor(request.WorkingDirectory);
        return info;
    }
}
