using ReTAC.Shell;

namespace ReTAC.Domain.Tests;

/// <summary>R-118: 実際に印が付く項目で番号が取れる（SHGFI_ICON を付け忘れると常に 0 になる）。</summary>
public class ShellOverlaysTests
{
    [Fact]
    public void ショートカットには印が付く()
    {
        // スタートメニューに頼らず、一時フォルダにショートカットを作る（どの環境でも必ず確かめる。見つからずに素通りしない）
        var dir = Directory.CreateTempSubdirectory("retac-overlay-");
        try
        {
            var target = Path.Combine(dir.FullName, "target.txt");
            File.WriteAllText(target, "x");
            var link = Path.Combine(dir.FullName, "target.lnk");
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            try
            {
                dynamic shortcut = shell.CreateShortcut(link);
                try
                {
                    shortcut.TargetPath = target;
                    shortcut.Save();
                }
                finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut); }   // 先にショートカット、次に本体
            }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }

            var index = ShellOverlays.IndexOf(link);
            Assert.NotEqual(0, index);                       // SHGFI_ICON を付け忘れると 0 になる
            Assert.NotNull(ShellOverlays.Image(index, 16));
            // 普通のファイルに印が無いことは確かめない（同期ソフト・セキュリティ製品・バージョン管理のシェル拡張が印を付けうる）
        }
        finally { dir.Delete(recursive: true); }
    }

    [Theory]
    [InlineData(0, 16)]      // 印なし
    [InlineData(99, 16)]     // 無効な番号（GetOverlayImage が失敗する）
    [InlineData(1, 0)]       // 大きさが 0
    public void 取れない印の絵は例外ではなくnull(int index, int size)
    {
        Assert.Null(ShellOverlays.Image(index, size));
        Assert.Null(ShellOverlays.Image(index, size));   // 2 回目も（失敗を覚えて壊れていない）
    }
}
