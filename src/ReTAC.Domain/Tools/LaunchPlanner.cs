using ReTAC.Domain.Entries;
using ReTAC.Domain.Selection;

namespace ReTAC.Domain.Tools;

/// <summary>1 回の起動（F-03）。</summary>
/// <param name="WorkingDirectory">常にカレントフォルダ（R-37-3）。値として持ち、奥で決め打ちしない</param>
/// <param name="TargetLabel">外部ツールキューの一覧に出す対象の名前</param>
public sealed record LaunchRequest(ExternalTool Tool, IReadOnlyList<string> Arguments, string WorkingDirectory, string TargetLabel)
{
    /// <summary>確認ダイアログに出すコマンドライン。空白を含む引数と空の引数は引用符で囲む。</summary>
    public string DisplayCommandLine() =>
        string.Join(" ", new[] { Tool.Path }.Concat(Arguments).Select(Quote));

    private static string Quote(string text) =>
        text.Length == 0 || text.Contains(' ') || text.Contains('\t') ? $"\"{text}\"" : text;
}

/// <summary>
/// 外部ツールの 1 回の実行を、起動の並びにする（F-03 / F-09）。
/// 実効対象を求めるのはこちら（コマンドの側）で、起動の処理は渡された対象だけを扱う。
/// </summary>
public static class LaunchPlanner
{
    /// <summary>R-56-3: 実際にこの回数以上起動するときは確認する。</summary>
    public const int ManyLaunchThreshold = 10;

    /// <summary>
    /// F-09: 渡す対象。全体の設定「連続起動はしない」が ON ならカーソル位置の 1 件、
    /// OFF ならマークしたもの（表示順）。マークが無ければカーソル位置の 1 件で、<c>..</c> でも含める
    /// （<c>${path}</c> をカレントフォルダにするため）。
    /// </summary>
    public static IReadOnlyList<Entry> TargetsFor(ListState state, bool suppressMultiple)
    {
        if (!suppressMultiple && state.Marks.Count > 0) return state.EffectiveTarget();
        return state.Cursor is { } cursor ? [cursor] : [];
    }

    /// <summary>F-09: ツールのチェックは全体の設定が OFF のときだけ効く。</summary>
    public static bool RunsPerItem(ExternalTool tool, bool suppressMultiple) =>
        tool.LaunchPerItem && !suppressMultiple;

    /// <returns>起動の並び。<c>!</c> で起動しないと決まったものは含めない</returns>
    public static IReadOnlyList<LaunchRequest> Plan(
        ExternalTool tool, ArgumentTemplate template, IReadOnlyList<Entry> targets, Entry? cursor,
        string currentFolder, IReadOnlyList<string> answers, bool suppressMultiple, Func<string, string>? pathForm = null)
    {
        var requests = new List<LaunchRequest>();

        if (RunsPerItem(tool, suppressMultiple))
        {
            foreach (var target in targets) Add([target], target.Name);
        }
        else
        {
            Add(targets, targets.Count == 1 ? targets[0].Name : $"{targets.Count} 件");
        }
        return requests;

        void Add(IReadOnlyList<Entry> part, string label)
        {
            var arguments = ArgumentExpander.Expand(template, new MacroContext(part, cursor, currentFolder, answers, pathForm));
            if (arguments is not null) requests.Add(new LaunchRequest(tool, arguments, currentFolder, label));
        }
    }

    public static bool NeedsManyConfirmation(ExternalTool tool, bool suppressMultiple, int requestCount) =>
        RunsPerItem(tool, suppressMultiple) && requestCount >= ManyLaunchThreshold;
}
