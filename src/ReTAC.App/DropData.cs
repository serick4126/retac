using System.Windows.Forms;
using ReTAC.Shell;

namespace ReTAC.App;

/// <summary>R-146-1: 受け口が読む、パスを持たない項目（書庫の中など）。ディスク上のパスの一覧（FileDrop）があれば今までどおりそちらを使う。</summary>
internal static class DropData
{
    /// <summary>ドラッグ中の表示用。項目は作らず、形式だけを見る（DragOver のたびに識別子を複製しない）</summary>
    public static bool HasVirtual(IDataObject? data) =>
        data is not null && !data.GetDataPresent(DataFormats.FileDrop) && data.GetDataPresent(VirtualItems.ShellIdListFormat);

    /// <summary>ドロップ・貼り付けの処理の中で呼ぶ（P1-2）。返した VirtualItems は受け取った側が Dispose する</summary>
    public static VirtualItems? CaptureVirtual(IDataObject? data) =>
        HasVirtual(data) ? VirtualItems.FromDataObject(data!) : null;
}
