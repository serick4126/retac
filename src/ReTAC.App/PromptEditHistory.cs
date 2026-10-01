using ReTAC.Domain.Tools;

namespace ReTAC.App;

/// <summary>元に戻すために積む、操作の前の状態。タイトルと「前回の入力を初期値にする」は含めない（R-133）。</summary>
internal sealed record PromptEditState(IReadOnlyList<PromptItem> Items, IReadOnlyList<PromptArgument> Arguments, string? ImportedPath);

/// <summary>ヘルパーの「元に戻す」の積み置き場（R-133）。やり直しは持たない。</summary>
internal sealed class PromptEditHistory
{
    /// <summary>積む数の上限。1 つが小さな定義の複製でも、長く開いたままのダイアログで際限なく増やさないため。</summary>
    internal const int Limit = 100;

    private readonly List<PromptEditState> _states = [];

    public bool CanUndo => _states.Count > 0;

    private void Push(PromptEditState state)
    {
        _states.Add(state);
        if (_states.Count > Limit) _states.RemoveAt(0);
    }

    /// <summary>リストは複製して積む。後の操作で元のリストが書き換わっても、積んだ中身を変えないため。</summary>
    public void Push(IEnumerable<PromptItem> items, IEnumerable<PromptArgument> arguments, string? importedPath) =>
        Push(new PromptEditState([.. items], [.. arguments], importedPath));

    /// <summary>項目が同じ内容か。Choices は List で参照比較になるので、要素ごとに比べる。何も変わらない編集を積まないため。</summary>
    internal static bool SameItem(PromptItem a, PromptItem b)
    {
        var none = new List<PromptChoice>();   // 空のリストも別のインスタンスだと参照比較で食い違うので、共有する
        return a with { Choices = none } == b with { Choices = none } && a.Choices.SequenceEqual(b.Choices);
    }

    public bool TryUndo(out PromptEditState state)
    {
        if (_states.Count == 0)
        {
            state = null!;
            return false;
        }
        state = _states[^1];
        _states.RemoveAt(_states.Count - 1);
        return true;
    }
}
