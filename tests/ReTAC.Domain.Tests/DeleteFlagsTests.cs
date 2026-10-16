using ReTAC.Shell;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-19: 削除はごみ箱へ送る。ごみ箱が使えない場所で Windows が完全削除の前に確認できるよう、
/// 削除に使うフラグに確認や画面を省くものを入れない（後から誰かが足したらここで止める）。
/// 確認が実際に出ることは、ごみ箱が使えない場所での実機確認で確かめる。
/// </summary>
public class DeleteFlagsTests
{
    private const uint FOF_SILENT = 0x0004;
    private const uint FOF_NOCONFIRMATION = 0x0010;
    private const uint FOF_ALLOWUNDO = 0x0040;
    private const uint FOF_NOERRORUI = 0x0400;

    [Fact]
    public void 削除に使うフラグはごみ箱へ送り_確認と画面を省かない()
    {
        // MainForm.DeleteTargets は RunOperation(silentOverwrite: false) で作る（noUi はテスト用だけ）
        var flags = ShellFileOperation.OperationFlags(silentOverwrite: false, noUi: false);

        Assert.Equal(FOF_ALLOWUNDO, flags & FOF_ALLOWUNDO);
        Assert.Equal(0u, flags & FOF_NOCONFIRMATION);
        Assert.Equal(0u, flags & FOF_SILENT);
        Assert.Equal(0u, flags & FOF_NOERRORUI);
    }
}
