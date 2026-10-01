using ReTAC.Domain.Tools;

namespace ReTAC.App;

/// <summary>
/// R-134: 変換の候補を調べている間に選択が変わったとき、古い要求の結果を捨てるための世代番号。
/// 遅いパスの調べは押した順に終わるとは限らないので、「最新の要求か」で見分ける。
/// 画面から切り離してあるのは、ハンドル無しでテストするため。
/// </summary>
internal sealed class ConvertRequestGate
{
    private int _generation;

    public int Begin() => ++_generation;

    public bool IsCurrent(int generation) => generation == _generation;

    /// <summary>
    /// 行を参照で探す。同じ値の固定の行が 2 つあり得るので、record の値の比較では取り違える。
    /// </summary>
    /// <returns>今の位置。無ければ -1</returns>
    public static int IndexOfSame(IReadOnlyList<PromptArgument> rows, PromptArgument target)
    {
        for (var i = 0; i < rows.Count; i++)
            if (ReferenceEquals(rows[i], target)) return i;
        return -1;
    }
}
