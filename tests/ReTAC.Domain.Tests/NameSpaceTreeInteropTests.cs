using System.Reflection;
using ReTAC.Shell;

namespace ReTAC.Domain.Tests;

public class NameSpaceTreeInteropTests
{
    [Fact]
    public void INameSpaceTreeControl2のABIはSDK順で宣言する()
    {
        var type = typeof(NameSpaceTreeInterop).GetNestedType(
            "INameSpaceTreeControl2", BindingFlags.NonPublic);

        Assert.NotNull(type);
        Assert.Equal(new Guid("7CC7AED8-290E-49BC-8945-C1401CC9306C"), type.GUID);
        Assert.Equal(
            [
                "Initialize", "TreeAdvise", "TreeUnadvise", "AppendRoot", "InsertRoot", "RemoveRoot",
                "RemoveAllRoots", "GetRootItems", "SetItemState", "GetItemState", "GetSelectedItems",
                "GetItemCustomState", "SetItemCustomState", "EnsureItemVisible", "SetTheme", "GetNextItem",
                "HitTest", "GetItemRect", "CollapseAll", "SetControlStyle", "GetControlStyle",
                "SetControlStyle2", "GetControlStyle2",
            ],
            type.GetMethods().OrderBy(method => method.MetadataToken).Select(method => method.Name));

        var styleType = typeof(NameSpaceTreeInterop).GetNestedType("TreeStyle2", BindingFlags.NonPublic);
        Assert.NotNull(styleType);
        Assert.Equal(0x10u, Convert.ToUInt32(Enum.Parse(styleType, "NoSingletonAutoExpand")));
        Assert.Equal(0x20u, Convert.ToUInt32(Enum.Parse(styleType, "NeverInsertNonEnumerated")));
    }
}
