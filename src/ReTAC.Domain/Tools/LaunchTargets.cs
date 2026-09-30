using ReTAC.Domain.Entries;
using ReTAC.Domain.Selection;

namespace ReTAC.Domain.Tools;

/// <summary>
/// プロパティの表示（`R`）で開く対象の決め方（R-68 / R-56-2 / R-56-3）。
/// 外部ツールは <see cref="LaunchPlanner.TargetsFor"/> を使う（`..` を含める点が違う）。
/// </summary>
public static class LaunchTargets
{
    /// <summary>
    /// R-56-3: これ以上の件数を一度に開くときは実行前に確認する。卓駆にない安全側の追加要件。
    /// <see cref="LaunchPlanner.ManyLaunchThreshold"/> と同じ値（M-2: 定義はそちらの 1 箇所だけ）。
    /// </summary>
    public const int ConfirmThreshold = LaunchPlanner.ManyLaunchThreshold;

    /// <summary>R-56-2: 実効対象すべて（マークがあればマークしたもの、無ければカーソルの 1 件。<c>..</c> は含めない）。</summary>
    public static IReadOnlyList<Entry> For(ListState state) => state.EffectiveTarget();

    public static bool NeedsConfirmation(int count) => count >= ConfirmThreshold;
}
