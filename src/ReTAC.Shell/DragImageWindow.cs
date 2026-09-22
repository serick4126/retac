using System.Runtime.InteropServices;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace ReTAC.Shell;

/// <summary>
/// R-78 / R-97-3: ドラッグ中にカーソルへ付く画像のウィンドウ（OS の画像管理が作る）。
/// 落とす先が説明（「◯◯へ移動」）を書き換えても、ウィンドウは自分では描き直さない。知らせるのはドラッグ元の役目で、
/// エクスプローラーは知らせるが、WinForms は効果（移動・コピー）が変わったときしか知らせない。
/// そのままだと、名前空間ツリーで別のフォルダを指しても、説明のフォルダ名が最初のまま残る（実機 NG）。
/// </summary>
public static class DragImageWindow
{
    /// <summary>説明が前回と変わっていたら、画像のウィンドウに描き直させる。GiveFeedback から毎回呼ぶ。</summary>
    /// <param name="last">前回読んだ説明。呼び出し側がドラッグの間だけ持つ</param>
    /// <returns>説明が出ているか。出ていなければ、カーソル（禁止マークなど）はドラッグ元が出す</returns>
    public static bool RedrawIfDescriptionChanged(ComTypes.IDataObject data, ref byte[]? last)
    {
        var description = Read(data, s_dropDescription);
        if (description is not { Length: >= 4 }) return false;
        var shown = BitConverter.ToInt32(description) != DROPIMAGE_INVALID;
        if (last is not null && description.AsSpan().SequenceEqual(last)) return shown;
        last = description;
        var window = Read(data, s_dragWindow);
        if (window is { Length: >= 4 }) PostMessage((IntPtr)BitConverter.ToInt32(window), DDWM_UPDATEWINDOW, IntPtr.Zero, IntPtr.Zero);
        return shown;
    }

    /// <returns>形式の中身。無ければ（または読めなければ）null。ドラッグを止めないよう、失敗しても投げない</returns>
    private static byte[]? Read(ComTypes.IDataObject data, short format)
    {
        var request = new ComTypes.FORMATETC
        {
            cfFormat = format,
            dwAspect = ComTypes.DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = ComTypes.TYMED.TYMED_HGLOBAL,
        };
        try
        {
            if (data.QueryGetData(ref request) != 0) return null;
            data.GetData(ref request, out var medium);
            try
            {
                var size = (int)GlobalSize(medium.unionmember);
                var pointer = GlobalLock(medium.unionmember);
                if (pointer == IntPtr.Zero) return null;
                try
                {
                    var bytes = new byte[size];
                    Marshal.Copy(pointer, bytes, 0, size);
                    return bytes;
                }
                finally { GlobalUnlock(medium.unionmember); }
            }
            finally { ReleaseStgMedium(ref medium); }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { return null; }
    }

    private const int DROPIMAGE_INVALID = -1;
    private const uint DDWM_UPDATEWINDOW = 0x0400 + 3;   // WM_USER + 3
    private static readonly short s_dropDescription = (short)RegisterClipboardFormat("DropDescription");
    private static readonly short s_dragWindow = (short)RegisterClipboardFormat("DragWindow");

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string format);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref ComTypes.STGMEDIUM medium);
}
