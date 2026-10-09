using ReTAC.App;
using ReTAC.Shell;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-97-5: ツリーが位置を合わせるかは、移動の要求に付いて来る起点（fromTree）だけで決まる。
/// ツリーの側で確定したフォルダを覚えていた頃は、開けなかった移動の印が残り、後でアドレスバーから
/// 同じフォルダへ移ったときに「ツリーから」と取り違えて位置を合わせなかった（コードレビュー 2026-10-09）。
/// ハンドルは作らない（xunit は MTA。要求を受けるだけで、ツリーには渡らない）。
/// </summary>
public class DriveTreeViewOriginTests
{
    private static readonly ShellTreeVisibility Visibility = new(false, true);

    [Theory]
    [InlineData(NameSpaceTreeRootKind.Drive)]
    [InlineData(NameSpaceTreeRootKind.Desktop)]
    public void 起点は要求ごとに決まり_失敗した確定は後の移動に残らない(NameSpaceTreeRootKind kind)
    {
        using var tree = new DriveTreeView(kind);

        // ツリーで確定した移動
        tree.SyncCurrentFolder(@"C:\a", Visibility, resetExpansion: false, fromTree: true);
        Assert.True(tree.RequestedFromTree);

        // ツリーで C:\b を確定したが開けなかった（同期は来ない）。その後アドレスバーで C:\b へ移る
        tree.SyncCurrentFolder(@"C:\b", Visibility, resetExpansion: false, fromTree: false);
        Assert.False(tree.RequestedFromTree);
    }

    [Fact]
    public void 同じフォルダの再同期は_選択の途中のツリーからの移動を書き換えない()
    {
        using var tree = new DriveTreeView();
        tree.SyncCurrentFolder(@"C:\a", Visibility, resetExpansion: false, fromTree: true);

        // 一覧の更新（自動更新・F5）は fromTree なしで同じフォルダを同期する
        tree.SyncCurrentFolder(@"C:\a\", Visibility, resetExpansion: false, fromTree: false);
        Assert.True(tree.RequestedFromTree);

        // 表示の設定が変わって作り直すときは、新しい要求として扱う（ツリーのクリックの続きではない）
        tree.SyncCurrentFolder(@"C:\a", new ShellTreeVisibility(true, true), resetExpansion: false, fromTree: false);
        Assert.False(tree.RequestedFromTree);
    }
}
