using System.Runtime.InteropServices;

namespace ReTAC.Shell;

/// <summary>
/// シェルのコンテキストメニュー（0x8328）。
/// 書庫機能をスコープ外にできている前提そのもの — 圧縮・解凍は WinRAR の項目へ委譲する。
/// </summary>
public static class ShellContextMenu
{
    /// <param name="ownerHandle">メニューの所有者。実際の追跡はメッセージ転送用の隠しウィンドウで行う</param>
    /// <param name="paths">対象。同一フォルダ内の項目であること（シェルの仕様）</param>
    /// <param name="screenX">スクリーン座標</param>
    public static void Show(IntPtr ownerHandle, IReadOnlyList<string> paths, int screenX, int screenY)
    {
        if (paths.Count == 0) return;

        var pidls = new List<IntPtr>();
        var childPidls = new List<IntPtr>();
        IShellFolder? parent = null;
        var menu = IntPtr.Zero;
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
            if (parent is null || childPidls.Count == 0) return;

            var contextGuid = IID_IContextMenu;
            var children = childPidls.ToArray();
            var uiHr = parent.GetUIObjectOf(ownerHandle, (uint)children.Length, children, ref contextGuid, IntPtr.Zero, out var unknown);
            if (uiHr != 0 || unknown == IntPtr.Zero) return;

            contextMenu = Marshal.GetObjectForIUnknown(unknown);
            Marshal.Release(unknown);
            if (contextMenu is not IContextMenu shellMenu) return;

            menu = CreatePopupMenu();
            // CMF_EXPLORE: エクスプローラーと同じ既定の並び。拡張（WinRAR など）もこの経路で入る
            if (shellMenu.QueryContextMenu(menu, 0, IdCmdFirst, IdCmdLast, CMF_NORMAL | CMF_EXPLORE) < 0) return;

            // 拡張の項目はオーナードローのことがあり、メニュー用のメッセージを
            // IContextMenu2/3 へ転送しないと中身が出ない
            using var hook = new MenuMessageHook(contextMenu);
            var command = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_LEFTALIGN | TPM_RIGHTBUTTON,
                screenX, screenY, hook.Handle, IntPtr.Zero);
            if (command < IdCmdFirst) return;

            var invoke = new CMINVOKECOMMANDINFOEX
            {
                cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                fMask = CMIC_MASK_UNICODE,
                hwnd = ownerHandle,
                lpVerb = (IntPtr)(command - IdCmdFirst),
                lpVerbW = (IntPtr)(command - IdCmdFirst),
                nShow = SW_SHOWNORMAL,
            };
            shellMenu.InvokeCommand(ref invoke);
        }
        finally
        {
            if (menu != IntPtr.Zero) DestroyMenu(menu);
            if (contextMenu is not null) Marshal.ReleaseComObject(contextMenu);
            if (parent is not null) Marshal.ReleaseComObject(parent);
            // childPidls は親 pidl の内部を指すだけなので解放しない
            foreach (var pidl in pidls) Marshal.FreeCoTaskMem(pidl);
        }
    }

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
