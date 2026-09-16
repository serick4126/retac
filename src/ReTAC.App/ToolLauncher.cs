using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using ReTAC.Domain.Tools;

namespace ReTAC.App;

/// <summary>
/// 外部ツールの起動（F-03）。
/// R-37-3 / R-54-2: 作業ディレクトリは常にカレントフォルダ。ラッパーバッチを不要にするための要。
/// </summary>
public static class ToolLauncher
{
    /// <summary>
    /// 1 回分の起動（F-03）。対象の決定とマクロの展開は済んでいる（<see cref="LaunchPlanner"/>）。
    /// 起動の準備と起動は裏のスレッドで行う（ネットワーク上のパスで UI を止めない・R-23 / N-05）。
    /// 6 章 / V-03: 起動できなくてもエラーを出すだけ。ReTAC 本体は動き続ける。
    /// </summary>
    public static async Task StartAsync(IWin32Window owner, LaunchRequest request)
    {
        try
        {
            // パスの解決（File.Exists・レジストリ）と種類の判定（ファイルを読む）も裏のスレッドで行う（R-23 / N-05）
            await Task.Run(() => Process.Start(ToolProcess.StartInfo(request))?.Dispose());
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            MessageBox.Show(owner, $"{request.Tool.Path} を起動できませんでした。{Environment.NewLine}{ex.Message}",
                "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// CreateProcess の作業ディレクトリは longPathAware を立てても MAX_PATH までしか通らない。
    /// 260 を超えるフォルダでは「ディレクトリ名が無効です」で起動に失敗するので、8.3 名へ落とす。
    /// それも取れなければ空にする。作業ディレクトリを保つことより、起動できることを優先する。
    /// </summary>
    public static string WorkingDirectoryFor(string folder) => ShortIfLong(folder) ?? "";

    /// <summary>
    /// 起動対象・引数として渡すパス。
    /// <b>落とせなければ元のパスをそのまま返す。</b>作業ディレクトリと違い、
    /// ここを空にすると <c>ProcessStartInfo("")</c> になって起動そのものが失敗する
    /// （8.3 名を無効にしたボリュームで起きる。V-08）。
    /// 長いまま渡せば、外部アプリが開けないだけで済む。
    /// </summary>
    public static string TargetPath(string path) => ShortIfLong(path) ?? path;

    /// <summary>
    /// 260 を超えるパスを 8.3 名へ落とす。外部のアプリ（秀丸など）は長いパスを
    /// 扱えないことが多く、そのまま渡しても開けない。
    /// </summary>
    /// <returns>8.3 名。260 未満ならそのまま。落とせなければ null（扱いは呼び出し側が決める）</returns>
    private static string? ShortIfLong(string path)
    {
        const int maxPath = 260;
        const string longPathPrefix = @"\\?\";
        if (path.Length < maxPath) return path;

        var buffer = new StringBuilder(maxPath);
        // 260 超のパスを渡すので、GetShortPathName 側も長いパスの接頭辞が要る
        var length = GetShortPathName(longPathPrefix + path, buffer, buffer.Capacity);
        if (length == 0 || length >= maxPath) return null;

        var shortened = buffer.ToString();
        return shortened.StartsWith(longPathPrefix, StringComparison.Ordinal)
            ? shortened[longPathPrefix.Length..]
            : shortened;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetShortPathName(string longPath, StringBuilder shortPath, int bufferSize);
}
