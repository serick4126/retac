using System.Runtime.InteropServices;
using static ReTAC.Shell.NameSpaceTreeInterop;

namespace ReTAC.Shell;

/// <summary>R-97: 非表示項目を除きつつ、現在位置へ至る枝だけは列挙できるようにする。</summary>
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
internal sealed class CurrentPathShellItemFilter(
    string currentPath, ShellTreeVisibility visibility, bool allowVirtualItems = false) : IShellItemFilter
{
    private const int S_OK = 0;
    private const int S_FALSE = 1;
    private string _currentPath = currentPath;

    internal ShellTreeVisibility Visibility => visibility;

    internal string UpdateCurrentPath(string path)
    {
        var previous = _currentPath;
        _currentPath = path;
        return previous;
    }

    /// <summary>
    /// R-97: ドライブのルートの隠し・システム属性は Windows が付けたもので、利用者が隠したフォルダではない。
    /// 除くとデスクトップツリーの PC の下からそのドライブが消え、今のフォルダが別のドライブにある間に列挙された後は、
    /// そのドライブへ移っても辿れない（例外の枝の判定はドライブの下の段から見るので、作り直しも起きない）。
    /// </summary>
    private static bool IsDriveRoot(string path) =>
        !string.IsNullOrEmpty(path) && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetPathRoot(path) ?? ""),
            Path.TrimEndingDirectorySeparator(path), StringComparison.OrdinalIgnoreCase);

    internal bool RequiresException(string path)
    {
        if (IsDriveRoot(path)) return false;
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.Directory) == 0
                || (!visibility.ShowHidden && (attributes & FileAttributes.Hidden) != 0)
                || (!visibility.ShowSystem && (attributes & FileAttributes.System) != 0);
        }
        catch (Exception)
        {
            return true;
        }
    }

    internal bool IncludesPath(string path) =>
        NameSpaceTreePolicy.IsCurrentPathOrAncestor(path, _currentPath) || !RequiresException(path);

    internal bool IncludesBranch(string path) =>
        ShellItemPath.ParentPathsFromRoot(path).Append(path).All(IncludesPath);

    public int IncludeItem(IntPtr item)
    {
        try
        {
            var path = ShellItemPath.FileSystemPathOf(item);
            // R-97: デスクトップツリーは「PC」「ネットワーク」等、実パスを持たない仮想項目も辿れる必要がある。
            if (path is null) return allowVirtualItems ? S_OK : S_FALSE;
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) == 0) return S_FALSE;
            if (NameSpaceTreePolicy.IsCurrentPathOrAncestor(path, _currentPath) || IsDriveRoot(path)) return S_OK;
            if (!visibility.ShowHidden && (attributes & FileAttributes.Hidden) != 0) return S_FALSE;
            if (!visibility.ShowSystem && (attributes & FileAttributes.System) != 0) return S_FALSE;
            return S_OK;
        }
        catch (Exception)
        {
            // Shell の属性取得失敗でツリー全体の列挙まで止めない。
            return S_FALSE;
        }
    }

    public int GetEnumFlagsForItem(IntPtr item, out uint flags)
    {
        var value = EnumFlags.Folders;
        try
        {
            var path = ShellItemPath.FileSystemPathOf(item);
            if (path is not null && NameSpaceTreePolicy.IsCurrentPathOrAncestor(path, _currentPath))
            {
                value |= EnumFlags.IncludeHidden | EnumFlags.IncludeSuperHidden;
            }
            else
            {
                if (visibility.ShowHidden) value |= EnumFlags.IncludeHidden;
                if (visibility.ShowSystem) value |= EnumFlags.IncludeSuperHidden;
            }
        }
        catch (Exception)
        {
            // 列挙フラグを最小構成へ戻せば、取得できる通常フォルダは使い続けられる。
        }

        flags = (uint)value;
        return S_OK;
    }
}
