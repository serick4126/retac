using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>
/// 呼んだウィンドウだけを無効にしてダイアログを出し、閉じるのを待つ（F-03 / R-23）。
/// ShowDialog は同じ UI スレッドのトップレベルウィンドウをすべて無効にするので、
/// 外部ツールの入力を待つ間、他の ReTAC ウィンドウまで操作できなくなる。
/// </summary>
internal static class OwnerModal
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool enable);

    /// <returns>閉じたときの DialogResult。× で閉じたら Cancel</returns>
    public static Task<DialogResult> ShowAsync(Form owner, Form dialog)
    {
        var closed = new TaskCompletionSource<DialogResult>();

        // モードレスでは DialogResult を持つボタンを押しても閉じない。モーダルと同じく閉じるようにする
        foreach (var button in dialog.Controls.OfType<Button>().Where(b => b.DialogResult != DialogResult.None))
            button.Click += (_, _) => dialog.Close();

        // ダイアログ自身の FormClosing（検査で差し戻す等）より後に登録することで、
        // e.Cancel で閉じずに終わったときは DialogResult を None に戻す。
        // ShowDialog と違い、モードレスは閉じずに終わっても DialogResult を自動で戻さないため、
        // 次に別の経路（× など）で閉じたときに前回の OK が化けて残ってしまう（実機指摘）
        dialog.FormClosing += (_, e) =>
        {
            if (e.Cancel) dialog.DialogResult = DialogResult.None;
        };

        dialog.FormClosed += (_, _) =>
        {
            EnableWindow(owner.Handle, true);
            owner.Activate();
            closed.TrySetResult(dialog.DialogResult == DialogResult.None ? DialogResult.Cancel : dialog.DialogResult);
        };

        // CenterParent はモードレスでは効かないので自分で真ん中に置く
        dialog.StartPosition = FormStartPosition.Manual;
        dialog.Location = new Point(owner.Left + (owner.Width - dialog.Width) / 2, owner.Top + (owner.Height - dialog.Height) / 2);

        EnableWindow(owner.Handle, false);
        dialog.Show(owner);
        return closed.Task;
    }
}
