using System.Runtime.InteropServices;
using ReTAC.Shell;
using static ReTAC.Shell.NameSpaceTreeInterop;

namespace ReTAC.Domain.Tests;

public class CurrentPathShellItemFilterTests
{
    [Fact]
    public void 現在位置を更新すると例外にする枝も移る()
    {
        var root = Path.Combine(Path.GetTempPath(), $"retac-filter-{Guid.NewGuid():N}");
        var oldBranch = Path.Combine(root, "old");
        var oldDescendant = Path.Combine(oldBranch, "descendant");
        var newBranch = Path.Combine(root, "new");
        var hiddenSibling = Path.Combine(root, "hidden-sibling");
        var systemSibling = Path.Combine(root, "system-sibling");
        Directory.CreateDirectory(oldDescendant);
        Directory.CreateDirectory(newBranch);
        Directory.CreateDirectory(hiddenSibling);
        Directory.CreateDirectory(systemSibling);
        File.SetAttributes(oldBranch, File.GetAttributes(oldBranch) | FileAttributes.Hidden | FileAttributes.System);
        File.SetAttributes(hiddenSibling, File.GetAttributes(hiddenSibling) | FileAttributes.Hidden);
        File.SetAttributes(systemSibling, File.GetAttributes(systemSibling) | FileAttributes.System);

        try
        {
            var filter = new CurrentPathShellItemFilter(oldBranch, new ShellTreeVisibility(false, false));
            Assert.True(IsIncluded(filter, oldBranch));
            Assert.Equal(EnumFlags.Folders | EnumFlags.IncludeHidden | EnumFlags.IncludeSuperHidden,
                EnumFlagsFor(filter, oldBranch));

            Assert.Equal(oldBranch, filter.UpdateCurrentPath(newBranch));

            Assert.Equal(EnumFlags.Folders, EnumFlagsFor(filter, oldBranch));
            Assert.Equal(EnumFlags.Folders | EnumFlags.IncludeHidden | EnumFlags.IncludeSuperHidden,
                EnumFlagsFor(filter, newBranch));
            Assert.False(IsIncluded(filter, oldBranch));
            Assert.False(filter.IncludesBranch(oldDescendant));
            Assert.False(IsIncluded(filter, hiddenSibling));
            Assert.False(IsIncluded(filter, systemSibling));
            Assert.True(IsIncluded(filter, newBranch));
        }
        finally
        {
            File.SetAttributes(oldBranch, FileAttributes.Directory);
            File.SetAttributes(hiddenSibling, FileAttributes.Directory);
            File.SetAttributes(systemSibling, FileAttributes.Directory);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 有効なShell項目でも属性を取得できなければ除外する()
    {
        var path = Path.Combine(Path.GetTempPath(), $"retac-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        var item = ShellItemPath.Create(path);
        var pointer = Marshal.GetIUnknownForObject(item);
        Directory.Delete(path);

        try
        {
            var filter = new CurrentPathShellItemFilter(Path.GetTempPath(), new ShellTreeVisibility(true, true));
            Assert.Equal(1, filter.IncludeItem(pointer));
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    // R-97 / Task2: デスクトップツリーは「PC」等、実パスを持たない仮想項目も選択・展開できる必要がある。
    [Fact]
    public void allowVirtualItemsが真なら実パスを持たない項目も含める()
    {
        var computer = ShellItemPath.CreateComputerFolder();
        var pointer = Marshal.GetIUnknownForObject(computer);
        try
        {
            var allow = new CurrentPathShellItemFilter(Path.GetTempPath(), new ShellTreeVisibility(false, false), allowVirtualItems: true);
            Assert.Equal(0, allow.IncludeItem(pointer));

            var deny = new CurrentPathShellItemFilter(Path.GetTempPath(), new ShellTreeVisibility(false, false));
            Assert.Equal(1, deny.IncludeItem(pointer));
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    [Theory]
    [InlineData(false, false, true, false, false, false)]
    [InlineData(true, false, true, true, false, false)]
    [InlineData(false, true, true, false, true, false)]
    [InlineData(true, true, true, true, true, true)]
    public void 通常項目は非表示とシステムの表示設定に従う(
        bool showHidden, bool showSystem,
        bool normalExpected, bool hiddenExpected, bool systemExpected, bool bothExpected)
    {
        var root = Path.Combine(Path.GetTempPath(), $"retac-filter-{Guid.NewGuid():N}");
        var current = Path.Combine(root, "current");
        var normal = Path.Combine(root, "normal");
        var hidden = Path.Combine(root, "hidden");
        var system = Path.Combine(root, "system");
        var both = Path.Combine(root, "both");
        foreach (var path in new[] { current, normal, hidden, system, both }) Directory.CreateDirectory(path);
        File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        File.SetAttributes(system, File.GetAttributes(system) | FileAttributes.System);
        File.SetAttributes(both, File.GetAttributes(both) | FileAttributes.Hidden | FileAttributes.System);

        try
        {
            var filter = new CurrentPathShellItemFilter(current, new ShellTreeVisibility(showHidden, showSystem));
            Assert.Equal(normalExpected, IsIncluded(filter, normal));
            Assert.Equal(hiddenExpected, IsIncluded(filter, hidden));
            Assert.Equal(systemExpected, IsIncluded(filter, system));
            Assert.Equal(bothExpected, IsIncluded(filter, both));
        }
        finally
        {
            File.SetAttributes(hidden, FileAttributes.Directory);
            File.SetAttributes(system, FileAttributes.Directory);
            File.SetAttributes(both, FileAttributes.Directory);
            Directory.Delete(root, recursive: true);
        }
    }

    private static EnumFlags EnumFlagsFor(CurrentPathShellItemFilter filter, string path)
    {
        var item = ShellItemPath.Create(path);
        var pointer = Marshal.GetIUnknownForObject(item);
        try
        {
            Assert.Equal(0, filter.GetEnumFlagsForItem(pointer, out var flags));
            return (EnumFlags)flags;
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    private static bool IsIncluded(CurrentPathShellItemFilter filter, string path)
    {
        var item = ShellItemPath.Create(path);
        var pointer = Marshal.GetIUnknownForObject(item);
        try { return filter.IncludeItem(pointer) == 0; }
        finally { Marshal.Release(pointer); }
    }
}
