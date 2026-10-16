namespace ReTAC.Domain.Keys;

/// <summary>
/// 固定のキー（R-25）のうち、ファイルリストとツリーの両方の経路で同じ判定を要るもの。
/// ファイルリストは自分でキーを処理し、ツリーはキーを MainForm に流すので、判定を 1 か所にそろえる。
/// </summary>
public static class FixedKeys
{
    /// <summary>
    /// 修飾キーなしの BackSpace だけが「親フォルダに戻る」（R-25 / R-39）。Ctrl+BackSpace は割り当ての枠に残す。
    /// Shift・Alt 付きを同じに扱うと、ファイルリストだけ Shift+BackSpace で親へ移っていた（Phase 28.1 で直した）。
    /// </summary>
    public static bool IsGoParent(ushort virtualKey, bool shift, bool ctrl, bool alt) =>
        virtualKey == Vk.Back && !shift && !ctrl && !alt;
}
