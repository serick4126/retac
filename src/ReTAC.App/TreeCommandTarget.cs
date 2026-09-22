using ReTAC.Domain.Entries;

namespace ReTAC.App;

/// <summary>
/// R-97-3: ツリーにフォーカスがあるときのファイル操作・外部ツールの対象を決める、副作用のない判定。
/// 移動・履歴コマンドは元から _currentFolder/_history だけを見ており、ここを通らない。
/// </summary>
public static class TreeCommandTarget
{
    /// <returns>
    /// ツリーのキー経由でなければ null(呼び出し側はファイル表示パネルの対象を使う)。
    /// ツリー経由なら、選択中の実フォルダの Entry を 1 件だけ返す。パスを持たない項目や
    /// 選択なしのときは空を返す(対象なし。R-97-3)。
    /// </returns>
    public static IReadOnlyList<Entry>? Resolve(bool fromTreeCommandKey, Entry? selectedFolderEntry) =>
        !fromTreeCommandKey ? null : selectedFolderEntry is { } entry ? [entry] : [];
}
