using System.Runtime.InteropServices;
using System.Windows.Forms;
using ReTAC.Shell;

namespace ReTAC.App;

/// <summary>R-78 / R-97-3: ReTAC から始めるファイルのドラッグ。</summary>
internal static class ShellDrag
{
    /// <summary>
    /// シェルが作るデータ（Shell IDList と FileDrop の両方を持つ）を WinForms の DataObject で包み、小さい画像を付けて渡す。
    /// FileDrop だけだと、名前空間ツリー（左パネル）はシェル項目に変換できず、ドラッグ中の通知もドロップも受けられない。
    /// </summary>
    public static void Start(Control source, IReadOnlyList<string> paths, Bitmap image, Point cursorOffset)
    {
        var shell = ShellDataObject.For(paths);
        DataObject data;
        if (shell is not null) data = new DataObject(shell);
        else
        {
            data = new DataObject();
            var list = new System.Collections.Specialized.StringCollection();
            list.AddRange(paths.ToArray());
            data.SetFileDropList(list);
        }
        byte[]? lastDescription = null;
        var defaultCursors = false;
        GiveFeedbackEventHandler redraw = (_, e) =>
        {
            // 名前空間ツリーは、落とせない所（PC など）で説明を消す。画像付きの WinForms は既定のカーソルを使わないので、
            // そのままでは禁止マークがどこにも出ない
            if (!DragImageWindow.RedrawIfDescriptionChanged(data, ref lastDescription) && e.Effect == DragDropEffects.None)
            {
                e.UseDefaultCursors = defaultCursors = true;
            }
            else if (defaultCursors)
            {
                // OS が付けた禁止のカーソルは、既定のカーソルをやめても残る。説明が出る所へ戻ったら矢印に戻す
                Cursor.Current = Cursors.Default;
                defaultCursors = false;
            }
        };
        source.GiveFeedback += redraw;
        try
        {
            source.DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link,
                image, cursorOffset, useDefaultDragImage: false);
        }
        finally
        {
            source.GiveFeedback -= redraw;
            // シェルの資源を GC まで握らない。DoDragDrop は同期なので、戻れば使い終わっている
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
        }
    }
}
