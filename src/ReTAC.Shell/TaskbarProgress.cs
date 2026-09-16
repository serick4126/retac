using System.Runtime.InteropServices;

namespace ReTAC.Shell;

/// <summary>
/// タスクバーのボタンに重ねる進行バー（F-05 の補助の入口）。ブラウザのダウンロードやエクスプローラーのコピーと同じ
/// Windows 標準の表示。ウィンドウを増やさずに「まだ動いている」ことを伝える。
/// 常駐でタスクバーから外しているときは見えないので、確実な入口はコマンド「外部ツールキュー」。
/// COM なので UI スレッド（STA）から呼ぶ。失敗しても何もしない（表示が無いだけで困らない）。
/// </summary>
public static class TaskbarProgress
{
    private const int NoProgress = 0x0;   // TBPF_NOPROGRESS
    private const int Normal = 0x2;       // TBPF_NORMAL

    private static ITaskbarList3? _taskbar;

    public static void Set(IntPtr hwnd, int done, int total)
    {
        try
        {
            var taskbar = Instance();
            taskbar.SetProgressState(hwnd, Normal);
            taskbar.SetProgressValue(hwnd, (ulong)Math.Max(done, 0), (ulong)Math.Max(total, 1));
        }
        catch (Exception)
        {
            // m4: 失敗しても何もしない（表示が無いだけで困らない）。原因を型で絞らない
        }
    }

    public static void Clear(IntPtr hwnd)
    {
        try
        {
            Instance().SetProgressState(hwnd, NoProgress);
        }
        catch (Exception)
        {
            // m4: 失敗しても何もしない（表示が無いだけで困らない）。原因を型で絞らない
        }
    }

    private static ITaskbarList3 Instance()
    {
        if (_taskbar is not null) return _taskbar;
        var taskbar = (ITaskbarList3)new TaskbarList();
        taskbar.HrInit();
        return _taskbar = taskbar;
    }

    [ComImport, Guid("56FDF344-FD6D-11D0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
    private class TaskbarList
    {
    }

    /// <summary>ITaskbarList → ITaskbarList2 → ITaskbarList3 の順に並べる（vtable の順）。</summary>
    [ComImport, Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, int flags);
    }
}
