using ReTAC.Domain.Entries;
using ReTAC.Domain.Selection;

namespace ReTAC.Domain.Tools;

/// <summary>
/// プロパティの表示（`R`）で開く対象の決め方（R-68 / R-56-2 / R-56-3）。
/// 外部ツールは <see cref="LaunchPlanner.TargetsFor"/> を使う（`..` を含める点が違う）。
/// どちらも全体の設定「複数選択の時外部ツールの連続起動はしない」に従う（F-09）。
/// </summary>
public static class LaunchTargets
{
    /// <summary>
    /// R-56-3: これ以上の件数を一度に開くときは実行前に確認する。卓駆にない安全側の追加要件。
    /// <see cref="LaunchPlanner.ManyLaunchThreshold"/> と同じ値（M-2: 定義はそちらの 1 箇所だけ）。
    /// </summary>
    public const int ConfirmThreshold = LaunchPlanner.ManyLaunchThreshold;

    /// <param name="suppressMultiple">
    /// 設定「複数選択の時外部ツールの連続起動はしない」。現行は ON（N-03）。
    /// ON なら実効対象が何件あってもカーソル位置の 1 件だけを渡す（R-68）。
    /// </param>
    public static IReadOnlyList<Entry> For(ListState state, bool suppressMultiple)
    {
        if (!suppressMultiple) return state.EffectiveTarget();
        return state.Cursor is { IsParent: false } cursor ? [cursor] : [];
    }

    public static bool NeedsConfirmation(int count) => count >= ConfirmThreshold;
}
