using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>
/// R-131: ファイルの参照。WinForms の OpenFileDialog は ShowDialog で同じスレッドの全ウィンドウを無効にするので、
/// 呼び出したウィンドウだけを止めるために、実体は ReTAC.Shell.ShellFileBrowser（Win32 を持ち主のハンドルで直接呼ぶ）。
/// </summary>
public static class FileBrowser
{
    /// <returns>選ばれたパス。取り消されたら null。</returns>
    public static string? Select(IWin32Window owner, string initialDirectory, string fileName) =>
        ReTAC.Shell.ShellFileBrowser.Select(owner.Handle, initialDirectory, fileName, "ファイルの選択");
}
