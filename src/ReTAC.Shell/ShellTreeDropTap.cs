using System.Runtime.InteropServices;
using System.Text;

namespace ReTAC.Shell;

/// <summary>OLE の IDropTarget（POINTL は 64 ビットの値渡し）。</summary>
[ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IOleDropTarget
{
    [PreserveSig] int DragEnter(IntPtr data, uint keyState, long point, ref uint effect);
    [PreserveSig] int DragOver(uint keyState, long point, ref uint effect);
    [PreserveSig] int DragLeave();
    [PreserveSig] int Drop(IntPtr data, uint keyState, long point, ref uint effect);
}

/// <summary>
/// R-111-2 / T1: NSTC は普通のフォルダの上では OnDragEnter / OnDragOver を呼ばず、OnDrop の KeyState はボタンが離れた後の 0 なので、
/// 外から右ボタンで落とされたかを NSTC の通知からは知れない。NSTC の中のツリーのウィンドウに登録された受け口を包み、
/// DragEnter / DragOver の KeyState だけ読んで、元の受け口へそのまま渡す（実機で確認済み）。
/// 受け口は GetProp の "OleDropTargetInterface"（OLE の内部の名前。文書化されていない）で取る。取れなければ包まない。
/// そのときは外からの右ボタンのドロップを左ボタンと同じに扱う（ダイアログを経るので黙って転送はしない）。
/// </summary>
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
internal sealed class ShellTreeDropTap(IOleDropTarget inner) : IOleDropTarget
{
    private const string DropTargetProp = "OleDropTargetInterface";

    // 包んだ先が失敗を返した・例外を投げたときは、右ボタンの印を消す。失敗の後に DragLeave / Drop が来るとは限らず、
    // 残すと次の左ボタンのドラッグが右ボタンに見える。印は包んだ先を呼ぶ前に立てる（NSTC の通知の中で読まれるため）
    public int DragEnter(IntPtr data, uint keyState, long point, ref uint effect)
    {
        DragButtonState.Enter(keyState);
        try { return ResetIfFailed(inner.DragEnter(data, keyState, point, ref effect)); }
        catch { DragButtonState.Reset(); throw; }
    }

    public int DragOver(uint keyState, long point, ref uint effect)
    {
        DragButtonState.Over(keyState);
        try { return ResetIfFailed(inner.DragOver(keyState, point, ref effect)); }
        catch { DragButtonState.Reset(); throw; }
    }

    private static int ResetIfFailed(int hr)
    {
        if (hr < 0) DragButtonState.Reset();
        return hr;
    }

    public int DragLeave()
    {
        DragButtonState.Reset();
        return inner.DragLeave();
    }

    /// <summary>NSTC の OnDrop（ReTAC のメニューとダイアログ）はこの中で呼ばれる。終わってから印を消す。</summary>
    public int Drop(IntPtr data, uint keyState, long point, ref uint effect)
    {
        try { return inner.Drop(data, keyState, point, ref effect); }
        finally { DragButtonState.Reset(); }
    }

    /// <summary>
    /// <paramref name="container"/> の子孫から、受け口を持つ SysTreeView32 を探して包む。NSTC を作り直したら呼び直す
    /// （新しいウィンドウには元の受け口が登録し直される）。包み済みのウィンドウは包み直さない。
    /// </summary>
    /// <returns>包めたら true</returns>
    internal static bool Install(IntPtr container)
    {
        if (container == IntPtr.Zero) return false;
        var tree = IntPtr.Zero;
        EnumChildWindows(container, (hwnd, _) =>
        {
            var name = new StringBuilder(32);
            GetClassName(hwnd, name, name.Capacity);
            if (name.ToString() != "SysTreeView32") return true;
            tree = hwnd;
            return false;
        }, IntPtr.Zero);
        if (tree == IntPtr.Zero) return false;

        if (Resolve(GetProp(tree, DropTargetProp), Marshal.GetObjectForIUnknown) is not { } current) return false;
        if (current is ShellTreeDropTap) return true;
        var result = Replace(tree, current, RevokeDragDrop, RegisterDragDrop);
        // Lost は RegisterDragDrop が続けて 2 回失敗したとき（資源が尽きたときくらいしか起きない）。ツリーはドロップを受けなくなるが、
        // ほかの操作は使える。NSTC が作り直される（別のドライブへ移って根が変わる）か、再起動するまで戻らない
        if (result == TapResult.Lost) System.Diagnostics.Debug.WriteLine("ShellTreeDropTap: ツリーの受け口を戻せなかった");
        return result == TapResult.Wrapped;
    }

    internal enum TapResult { Wrapped, NotWrapped, Lost }

    /// <summary>
    /// 登録された受け口を取り出す。取れなければ null（包まずに、右ボタンのドロップを左ボタンと同じに扱う）。
    /// 文書化されていないプロパティの値なので、COM のオブジェクトにできずに例外が出ることもあり得る。例外もここで null に落とし、
    /// ツリーを作る処理（SetRoot / SetDesktopRoot）まで抜けないようにする。変換を引数で受けるのは、失敗の経路をテストするため
    /// </summary>
    internal static IOleDropTarget? Resolve(IntPtr pointer, Func<IntPtr, object> toObject)
    {
        if (pointer == IntPtr.Zero) return null;
        try { return toObject(pointer) as IOleDropTarget; }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// 受け口の差し替え。登録済みのウィンドウへは登録できないので、先に外してから包んだものを登録する。
    /// 包めなければ元を登録し直す。元も戻せなければ Lost（ツリーがドロップを受けなくなる）。
    /// ネイティブの呼び出しを引数で受けるのは、失敗の経路をテストするため。
    /// </summary>
    internal static TapResult Replace(IntPtr tree, IOleDropTarget current,
        Func<IntPtr, int> revoke, Func<IntPtr, IOleDropTarget, int> register)
    {
        if (revoke(tree) != 0) return TapResult.NotWrapped;   // 外せなければ元のまま
        if (register(tree, new ShellTreeDropTap(current)) == 0) return TapResult.Wrapped;
        return register(tree, current) == 0 ? TapResult.NotWrapped : TapResult.Lost;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetProp(IntPtr hwnd, string name);
    [DllImport("ole32.dll")] private static extern int RevokeDragDrop(IntPtr hwnd);
    [DllImport("ole32.dll")] private static extern int RegisterDragDrop(IntPtr hwnd, IOleDropTarget target);
}
