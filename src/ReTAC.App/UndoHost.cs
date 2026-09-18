using ReTAC.Domain.FileOps;

namespace ReTAC.App;

/// <summary>
/// R-82: 元に戻す履歴はプロセスで 1 本。どのウィンドウの操作も、どのウィンドウからでも戻せる。
/// すべて UI スレッドから触るので排他はしない。
/// </summary>
internal static class UndoHost
{
    public static UndoHistory History { get; } = new();
}
