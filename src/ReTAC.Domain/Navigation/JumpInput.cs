using System.IO;

namespace ReTAC.Domain.Navigation;

/// <param name="SelectName">フォルダを開いた後にカーソルを合わせる名前。フォルダを指定したときは null</param>
public sealed record JumpTarget(string Folder, string? SelectName);

/// <summary>
/// R-87: ダイレクトジャンプの入力を行き先に変える。ダイアログとアドレスバーの両方がここを通る。
/// 存在の確認は引数で受け取る（Domain はファイルシステムに触れない）。
/// </summary>
public static class JumpInput
{
    public static JumpTarget? Decide(string currentFolder, string input,
                                     Func<string, bool> folderExists, Func<string, bool> fileExists)
    {
        // R-61: 相対パスはカレントフォルダ基準
        if (PathResolver.Resolve(currentFolder, input) is not { } path) return null;
        if (folderExists(path)) return new JumpTarget(path, null);

        // ファイルは開かない。置いてあるフォルダへ移り、カーソルを合わせるだけにする
        if (fileExists(path) && Path.GetDirectoryName(path) is { Length: > 0 } folder)
            return new JumpTarget(folder, Path.GetFileName(path));
        return null;
    }
}
