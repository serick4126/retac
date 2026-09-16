using System.Diagnostics;
using ReTAC.Domain.Tools;
using ReTAC.Shell;

namespace ReTAC.App;

/// <summary>1 回分の起動の仕方を決める（F-03）。直接の起動も外部ツールキューもここを通す。</summary>
internal static class ToolProcess
{
    public static ProcessStartInfo StartInfo(LaunchRequest request)
    {
        // 予定 §1.4 A-1: 名前だけなら PATH と App Paths から探す。見つからなければ書かれたまま Windows に任せる
        var path = ExecutableResolver.Resolve(request.Tool.Path) ?? request.Tool.Path;

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
            // UseShellExecute: 管理者権限を求めるツールも UAC に任せて起動できる（仕様書 §8 I-2）
            info = new ProcessStartInfo(path) { UseShellExecute = true };
        }

        foreach (var argument in request.Arguments) info.ArgumentList.Add(argument);
        // R-37-3: 常にカレントフォルダ。260 超では 8.3 名に落ちる
        info.WorkingDirectory = ToolLauncher.WorkingDirectoryFor(request.WorkingDirectory);
        return info;
    }
}
