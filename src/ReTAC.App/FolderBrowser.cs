using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>
/// R-52: フォルダ参照ツリー。パスを入力するあらゆる場所で `Shift+Enter` から開く（N-07）。
/// 実体は ReTAC.Shell.ShellFolderBrowser（SHBrowseForFolder の新 UI 版）。
/// </summary>
public static class FolderBrowser
{
    /// <param name="initialPath">R-52-2: 初期選択は入力欄の値。無効ならカレントフォルダ。</param>
    /// <returns>選ばれたパス。取り消されたら null。</returns>
    public static string? Select(IWin32Window owner, string? initialPath, string currentFolder) =>
        ReTAC.Shell.ShellFolderBrowser.Select(owner.Handle, initialPath, currentFolder, "フォルダの選択");
}
