using System.Runtime.InteropServices;

namespace ReTAC.Shell;

/// <summary>R-89: シェルのメニューで何が起きたか。Shell の項目を実行したのか、取り消したのかを呼び出し側が区別できるようにする。</summary>
public enum ContextMenuOutcome { Cancelled, ShellInvoked, AppItem }

/// <param name="AppItem">Outcome が AppItem のとき、選ばれた ReTAC の項目の添字。それ以外は -1</param>
public readonly record struct ContextMenuResult(ContextMenuOutcome Outcome, int AppItem = -1)
{
    public static readonly ContextMenuResult Cancelled = new(ContextMenuOutcome.Cancelled);
}

/// <summary>
/// シェルのコンテキストメニュー（0x8328）。
/// 書庫機能をスコープ外にできている前提そのもの — 圧縮・解凍は WinRAR の項目へ委譲する。
/// </summary>
public static class ShellContextMenu
{
    /// <param name="ownerHandle">メニューの所有者。実際の追跡はメッセージ転送用の隠しウィンドウで行う</param>
    /// <param name="paths">対象。同一フォルダ内の項目であること（シェルの仕様）</param>
    /// <param name="screenX">スクリーン座標</param>
    public static void Show(IntPtr ownerHandle, IReadOnlyList<string> paths, int screenX, int screenY) =>
        ShowWithItems(ownerHandle, paths, screenX, screenY, []);

    /// <summary>
    /// R-89: シェルのメニューの先頭に ReTAC の項目を差し込んで出す。ReTAC の項目はシェルへ渡さず、選ばれた添字を返す。
    /// Shell の項目は今までどおりここで実行する。
    /// </summary>
    /// <param name="items">先頭に並べる ReTAC の項目の文言（"" は区切り線）。空なら今までの Show と同じ。
    /// パスをシェルが扱えない（消えた・届かない）ときは、ReTAC の項目だけで出す</param>
    /// <param name="isChecked">R-106-1: items と同じ添字でチェック状態を持たせたいとき（例: 「アイコンだけ表示」）。無ければ全部チェック無し</param>
    public static ContextMenuResult ShowWithItems(IntPtr ownerHandle, IReadOnlyList<string> paths, int screenX, int screenY,
                                                 IReadOnlyList<string> items, IReadOnlyList<bool>? isChecked = null)
    {
        if (paths.Count == 0) return ContextMenuResult.Cancelled;

        var pidls = new List<IntPtr>();
        var childPidls = new List<IntPtr>();
        IShellFolder? parent = null;
        object? contextMenu = null;

        try
        {
            foreach (var path in paths)
            {
                if (SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _) != 0) continue;
                pidls.Add(pidl);

                var folderGuid = IID_IShellFolder;
                if (SHBindToParent(pidl, ref folderGuid, out var folder, out var child) != 0) continue;
                // R-13: 親は 1 つあれば足りる。2 つめ以降の RCW は放置すると参照が残る
                if (parent is null) parent = folder;
                else if (!ReferenceEquals(parent, folder)) Marshal.ReleaseComObject(folder);
                childPidls.Add(child);
            }
            if (parent is null || childPidls.Count == 0) return ShowItems(ownerHandle, screenX, screenY, items, isChecked);

            var contextGuid = IID_IContextMenu;
            var children = childPidls.ToArray();
            var uiHr = parent.GetUIObjectOf(ownerHandle, (uint)children.Length, children, ref contextGuid, IntPtr.Zero, out var unknown);
            if (uiHr != 0 || unknown == IntPtr.Zero) return ShowItems(ownerHandle, screenX, screenY, items, isChecked);

            contextMenu = Marshal.GetObjectForIUnknown(unknown);
            Marshal.Release(unknown);
            if (contextMenu is not IContextMenu shellMenu) return ContextMenuResult.Cancelled;

            return TrackAndInvoke(ownerHandle, contextMenu, shellMenu, screenX, screenY, directory: null, items, isChecked);
        }
        finally
        {
            if (contextMenu is not null) Marshal.ReleaseComObject(contextMenu);
            if (parent is not null) Marshal.ReleaseComObject(parent);
            // childPidls は親 pidl の内部を指すだけなので解放しない
            foreach (var pidl in pidls) Marshal.FreeCoTaskMem(pidl);
        }
    }

    /// <summary>R-89: ReTAC の項目だけのメニュー（コマンド・グループのブックマーク、バーの空いた所）。選ばれた添字を返す。</summary>
    /// <param name="items">項目の文言（"" は区切り線）</param>
    /// <param name="isChecked">R-106-1: items と同じ添字でチェック状態を持たせたいとき。無ければ全部チェック無し</param>
    public static ContextMenuResult ShowItems(IntPtr ownerHandle, int screenX, int screenY, IReadOnlyList<string> items,
                                             IReadOnlyList<bool>? isChecked = null)
    {
        if (items.Count == 0) return ContextMenuResult.Cancelled;
        var menu = CreatePopupMenu();
        try
        {
            InsertItems(menu, items, isChecked);
            var command = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_LEFTALIGN | TPM_RIGHTBUTTON, screenX, screenY, ownerHandle, IntPtr.Zero);
            return command >= AppIdFirst ? new ContextMenuResult(ContextMenuOutcome.AppItem, (int)(command - AppIdFirst)) : ContextMenuResult.Cancelled;
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    /// <summary>ReTAC の項目を先頭に差し込む。番号は Shell に渡す範囲（IdCmdFirst〜IdCmdLast）の外。</summary>
    private static void InsertItems(IntPtr menu, IReadOnlyList<string> items, IReadOnlyList<bool>? isChecked)
    {
        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (items[i].Length == 0) { InsertMenu(menu, 0, MF_BYPOSITION | MF_SEPARATOR, UIntPtr.Zero, null); continue; }
            var flags = MF_BYPOSITION | MF_STRING | (isChecked is { } c && i < c.Count && c[i] ? MF_CHECKED : 0);
            InsertMenu(menu, 0, flags, (UIntPtr)(AppIdFirst + (uint)i), items[i]);
        }
    }

    /// <summary>
    /// R-81: フォルダの背景のメニュー（「新規作成」「貼り付け」「プロパティ」など）。
    /// IShellFolder.CreateViewObject が返すメニューなので、エクスプローラーの画面（DefView）が足す
    /// 「表示」「並べ替え」は出ない。ReTAC は並べ替えを自前で持つので足さない。
    /// </summary>
    public static void ShowFolderBackground(IntPtr ownerHandle, string folderPath, int screenX, int screenY)
    {
        var pidl = IntPtr.Zero;
        IShellFolder? desktop = null;
        IShellFolder? folder = null;
        object? contextMenu = null;
        try
        {
            if (SHParseDisplayName(folderPath, IntPtr.Zero, out pidl, 0, out _) != 0) return;
            if (SHGetDesktopFolder(out desktop) != 0) return;

            var folderGuid = IID_IShellFolder;
            if (desktop.BindToObject(pidl, IntPtr.Zero, ref folderGuid, out var folderUnknown) != 0
                || folderUnknown == IntPtr.Zero) return;
            folder = (IShellFolder)Marshal.GetObjectForIUnknown(folderUnknown);
            Marshal.Release(folderUnknown);

            var contextGuid = IID_IContextMenu;
            if (folder.CreateViewObject(ownerHandle, ref contextGuid, out var menuUnknown) != 0
                || menuUnknown == IntPtr.Zero) return;
            contextMenu = Marshal.GetObjectForIUnknown(menuUnknown);
            Marshal.Release(menuUnknown);

            if (contextMenu is IContextMenu shellMenu)
                TrackAndInvoke(ownerHandle, contextMenu, shellMenu, screenX, screenY, folderPath, [], null);
        }
        finally
        {
            if (contextMenu is not null) Marshal.ReleaseComObject(contextMenu);
            if (folder is not null) Marshal.ReleaseComObject(folder);
            if (desktop is not null) Marshal.ReleaseComObject(desktop);
            if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
        }
    }

    /// <summary>メニューを出し、選ばれた項目を実行する。項目のメニューと背景のメニューで共通。</summary>
    /// <param name="directory">作業フォルダを使う項目（「ターミナルで開く」など）に渡すフォルダ。項目のメニューでは null</param>
    private static ContextMenuResult TrackAndInvoke(IntPtr ownerHandle, object contextMenu, IContextMenu shellMenu,
                                                    int screenX, int screenY, string? directory, IReadOnlyList<string> items,
                                                    IReadOnlyList<bool>? isChecked)
    {
        var menu = CreatePopupMenu();
        try
        {
            // CMF_EXPLORE: エクスプローラーと同じ既定の並び。拡張（WinRAR など）もこの経路で入る
            if (shellMenu.QueryContextMenu(menu, 0, IdCmdFirst, IdCmdLast, CMF_NORMAL | CMF_EXPLORE) < 0) return ContextMenuResult.Cancelled;

            // R-89: ReTAC の項目は Shell の項目の前に並べる
            InsertItems(menu, items, isChecked);
            if (items.Count > 0) InsertMenu(menu, (uint)items.Count, MF_BYPOSITION | MF_SEPARATOR, UIntPtr.Zero, null);

            // 拡張の項目はオーナードローのことがあり、メニュー用のメッセージを
            // IContextMenu2/3 へ転送しないと中身が出ない（「新規作成」のサブメニューも同じ）
            using var hook = new MenuMessageHook(contextMenu);
            var command = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_LEFTALIGN | TPM_RIGHTBUTTON,
                screenX, screenY, hook.Handle, IntPtr.Zero);
            if (command >= AppIdFirst) return new ContextMenuResult(ContextMenuOutcome.AppItem, (int)(command - AppIdFirst));
            if (command < IdCmdFirst) return ContextMenuResult.Cancelled;

            var invoke = new CMINVOKECOMMANDINFOEX
            {
                cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                fMask = CMIC_MASK_UNICODE,
                hwnd = ownerHandle,
                lpVerb = (IntPtr)(command - IdCmdFirst),
                lpVerbW = (IntPtr)(command - IdCmdFirst),
                lpDirectoryW = directory,
                nShow = SW_SHOWNORMAL,
            };
            shellMenu.InvokeCommand(ref invoke);
            return new ContextMenuResult(ContextMenuOutcome.ShellInvoked);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetDesktopFolder([MarshalAs(UnmanagedType.Interface)] out IShellFolder folder);

    /// <summary>メニュー表示中のメッセージを IContextMenu2/3 へ渡すためだけの隠しウィンドウ。</summary>
    private sealed class MenuMessageHook : System.Windows.Forms.NativeWindow, IDisposable
    {
        private const int WM_INITMENUPOPUP = 0x0117;
        private const int WM_DRAWITEM = 0x002B;
        private const int WM_MEASUREITEM = 0x002C;
        private const int WM_MENUCHAR = 0x0120;

        private readonly IContextMenu2? _menu2;
        private readonly IContextMenu3? _menu3;

        public MenuMessageHook(object contextMenu)
        {
            _menu2 = contextMenu as IContextMenu2;
            _menu3 = contextMenu as IContextMenu3;
            CreateHandle(new System.Windows.Forms.CreateParams());
        }

        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            if (m.Msg is WM_INITMENUPOPUP or WM_DRAWITEM or WM_MEASUREITEM or WM_MENUCHAR)
            {
                if (_menu3 is not null)
                {
                    _menu3.HandleMenuMsg2((uint)m.Msg, m.WParam, m.LParam, out var result);
                    m.Result = result;
                    return;
                }
                if (_menu2 is not null)
                {
                    _menu2.HandleMenuMsg((uint)m.Msg, m.WParam, m.LParam);
                    m.Result = IntPtr.Zero;
                    return;
                }
            }
            base.WndProc(ref m);
        }

        public void Dispose() => DestroyHandle();
    }

    private const uint IdCmdFirst = 1;
    private const uint IdCmdLast = 0x7FFF;
    /// <summary>R-89: ReTAC の項目の番号の始まり。Shell に渡す範囲の外。</summary>
    private const uint AppIdFirst = 0x8000;
    private const uint MF_BYPOSITION = 0x0400;
    private const uint MF_STRING = 0x0000;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint MF_CHECKED = 0x0008;
    private const uint CMF_NORMAL = 0x00000000;
    private const uint CMF_EXPLORE = 0x00000004;
    private const uint TPM_LEFTALIGN = 0x0000;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint CMIC_MASK_UNICODE = 0x00004000;
    private const int SW_SHOWNORMAL = 1;

    private static Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr pidl,
        uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(IntPtr pidl, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellFolder folder, out IntPtr pidlLast);

    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "InsertMenuW")]
    private static extern bool InsertMenu(IntPtr menu, uint position, uint flags, UIntPtr id, string? text);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr tpmParams);

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr bindContext, string displayName,
            out uint eaten, out IntPtr pidl, ref uint attributes);
        [PreserveSig] int EnumObjects(IntPtr hwnd, int flags, out IntPtr enumIdList);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr bindContext, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr bindContext, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig] int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetAttributesOf(uint count, [In] IntPtr[] pidls, ref uint attributes);
        [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint count,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] pidls,
            ref Guid riid, IntPtr reserved, out IntPtr ppv);
        [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr name);
        [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, string name, uint flags, out IntPtr pidlOut);
    }

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
        [PreserveSig] int GetCommandString(IntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint max);
    }

    [ComImport, Guid("000214F4-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
        [PreserveSig] int GetCommandString(IntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint max);
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
    }

    [ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
        [PreserveSig] int GetCommandString(IntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint max);
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpTitle;
        public IntPtr lpVerbW;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParametersW;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectoryW;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpTitleW;
        public POINT ptInvoke;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }
}
