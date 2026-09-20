using System.Runtime.InteropServices;

namespace ReTAC.Shell;

/// <summary>Windows SDK 10.0.22000 の ShObjIdl_core.idl / ShObjIdl.idl に合わせた宣言。</summary>
public static class NameSpaceTreeInterop
{
    internal static readonly Guid ClsidNameSpaceTreeControl = new("AE054212-3535-4430-83ED-D501AA6680E6");

    [Flags]
    internal enum TreeStyle : uint
    {
        HasExpandos = 0x00000001,
        HasLines = 0x00000002,
        HorizontalScroll = 0x00000020,
        RootHasExpando = 0x00000040,
        ShowSelectionAlways = 0x00000080,
        NoEditLabels = 0x00010000,
        TabStop = 0x00020000,
    }

    [Flags]
    internal enum RootStyle : uint { Visible = 0, Hidden = 1, Expanded = 2 }

    [Flags]
    public enum ItemState : uint { None = 0, Selected = 1, Expanded = 2, Bold = 4, Disabled = 8, SelectedNoExpand = 0x10 }

    [Flags]
    internal enum EnumFlags : uint
    {
        Folders = 0x20,
        IncludeHidden = 0x80,
        IncludeSuperHidden = 0x10000,
    }

    internal enum NextItem : uint
    {
        Next, NextVisible, Previous, PreviousVisible, Parent, Child, FirstVisible, LastVisible,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal NativeRect(Rectangle value) =>
            (Left, Top, Right, Bottom) = (value.Left, value.Top, value.Right, value.Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint { internal int X; internal int Y; }

    [ComImport, Guid("028212A3-B627-47E9-8856-C14265554E4F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface INameSpaceTreeControl
    {
        [PreserveSig] int Initialize(IntPtr parent, ref NativeRect bounds, TreeStyle style);
        [PreserveSig] int TreeAdvise(IntPtr sink, out uint cookie);
        [PreserveSig] int TreeUnadvise(uint cookie);
        [PreserveSig] int AppendRoot(IShellItem root, EnumFlags enumFlags, RootStyle rootStyle, IntPtr filter);
        [PreserveSig] int InsertRoot(int index, IShellItem root, EnumFlags enumFlags, RootStyle rootStyle, IntPtr filter);
        [PreserveSig] int RemoveRoot(IShellItem root);
        [PreserveSig] int RemoveAllRoots();
        [PreserveSig] int GetRootItems(out IntPtr rootItems);
        [PreserveSig] int SetItemState(IShellItem item, ItemState mask, ItemState state);
        [PreserveSig] int GetItemState(IShellItem item, ItemState mask, out ItemState state);
        [PreserveSig] int GetSelectedItems(out IntPtr selectedItems);
        [PreserveSig] int GetItemCustomState(IShellItem item, out int stateNumber);
        [PreserveSig] int SetItemCustomState(IShellItem item, int stateNumber);
        [PreserveSig] int EnsureItemVisible(IShellItem item);
    }

    [ComImport, Guid("00000114-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IOleWindow
    {
        [PreserveSig] int GetWindow(out IntPtr hwnd);
        [PreserveSig] int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enterMode);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid iid, out IntPtr result);
        [PreserveSig] int GetParent(out IShellItem parent);
        [PreserveSig] int GetDisplayName(uint nameKind, out IntPtr name);
        [PreserveSig] int GetAttributes(uint mask, out uint attributes);
        [PreserveSig] int Compare(IShellItem other, uint hint, out int order);
    }

    [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItemArray
    {
        [PreserveSig] int BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid iid, out IntPtr result);
        [PreserveSig] int GetPropertyStore(uint flags, ref Guid iid, out IntPtr result);
        [PreserveSig] int GetPropertyDescriptionList(ref PropertyKey key, ref Guid iid, out IntPtr result);
        [PreserveSig] int GetAttributes(uint flags, uint mask, out uint attributes);
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetItemAt(uint index, out IShellItem item);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey { internal Guid FormatId; internal uint PropertyId; }

    [ComVisible(true), Guid("93D77985-B3D8-4484-8318-672CDDA002CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface INameSpaceTreeControlEvents
    {
        [PreserveSig] int OnItemClick(IntPtr item, ShellTreeHitTest hitTest, ShellTreeClickType clickType);
        [PreserveSig] int OnPropertyItemCommit(IntPtr item);
        [PreserveSig] int OnItemStateChanging(IntPtr item, ItemState mask, ItemState state);
        [PreserveSig] int OnItemStateChanged(IntPtr item, ItemState mask, ItemState state);
        [PreserveSig] int OnSelectionChanged(IntPtr selection);
        [PreserveSig] int OnKeyboardInput(uint message, nuint wParam, nint lParam);
        [PreserveSig] int OnBeforeExpand(IntPtr item);
        [PreserveSig] int OnAfterExpand(IntPtr item);
        [PreserveSig] int OnBeginLabelEdit(IntPtr item);
        [PreserveSig] int OnEndLabelEdit(IntPtr item);
        [PreserveSig] int OnGetToolTip(IntPtr item, IntPtr tip, int characterCount);
        [PreserveSig] int OnBeforeItemDelete(IntPtr item);
        [PreserveSig] int OnItemAdded(IntPtr item, [MarshalAs(UnmanagedType.Bool)] bool isRoot);
        [PreserveSig] int OnItemDeleted(IntPtr item, [MarshalAs(UnmanagedType.Bool)] bool isRoot);
        [PreserveSig] int OnBeforeContextMenu(IntPtr item, ref Guid iid, out IntPtr result);
        [PreserveSig] int OnAfterContextMenu(IntPtr item, IntPtr contextMenu, ref Guid iid, out IntPtr result);
        [PreserveSig] int OnBeforeStateImageChange(IntPtr item);
        [PreserveSig] int OnGetDefaultIconIndex(IntPtr item, out int defaultIcon, out int openIcon);
    }

    [ComVisible(true), Guid("F9C665D6-C2F2-4C19-BF33-8322D7352F51"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface INameSpaceTreeControlDropHandler
    {
        [PreserveSig] int OnDragEnter(IntPtr over, IntPtr data, [MarshalAs(UnmanagedType.Bool)] bool outsideSource, uint keyState, ref uint effect);
        [PreserveSig] int OnDragOver(IntPtr over, IntPtr data, uint keyState, ref uint effect);
        [PreserveSig] int OnDragPosition(IntPtr over, IntPtr data, int newPosition, int oldPosition);
        [PreserveSig] int OnDrop(IntPtr over, IntPtr data, int position, uint keyState, ref uint effect);
        [PreserveSig] int OnDropPosition(IntPtr over, IntPtr data, int newPosition, int oldPosition);
        [PreserveSig] int OnDragLeave(IntPtr over);
    }
}
