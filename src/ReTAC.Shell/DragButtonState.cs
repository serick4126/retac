namespace ReTAC.Shell;

/// <summary>
/// R-111-2 / T4: 右ボタンのドラッグか。落とした時点の KeyState にはボタンが残らないので、DragEnter / DragOver で覚えておく。
/// ReTAC の受け口（ファイルリスト・ツリー・バー・ブックマーク）はすべて UI スレッドで呼ばれ、同時に 2 つのドラッグは起きないので、
/// プロセスで 1 つにする。受け口は DragLeave と落とした後に <see cref="Reset"/> し、次のドラッグへ残さない。
/// </summary>
public static class DragButtonState
{
    private const uint MK_RBUTTON = 0x0002;
    private static bool s_observed;

    /// <summary>
    /// T1: ReTAC が右ボタンで始めたドラッグ。NSTC は普通のフォルダの上で OnDragEnter / OnDragOver を呼ばないので、
    /// ツリーの受け口を包めなかったときでも、自分で始めたドラッグは見分けられるようにする。ShellDrag が立てて下ろす。
    /// </summary>
    public static bool SourceRight { get; set; }

    public static bool Right => s_observed || SourceRight;

    public static void Enter(uint keyState) => s_observed = (keyState & MK_RBUTTON) != 0;

    public static void Over(uint keyState)
    {
        if ((keyState & MK_RBUTTON) != 0) s_observed = true;
    }

    public static void Reset() => s_observed = false;
}
