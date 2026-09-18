using System.Runtime.CompilerServices;
using ReTAC.Domain.Navigation;

namespace ReTAC.App;

/// <summary>
/// Q12 / R-36: クイックアクセスは全ウィンドウで 1 つを共有する。ウィンドウごとにコピーを持つと、
/// 保存のたびにその窓の中身で書き戻すため、別の窓で足した項目が消えていた。設定の実体ごとに 1 つ持つ。
/// </summary>
public static class QuickAccessHost
{
    private static readonly ConditionalWeakTable<AppSettings, QuickAccessList> Lists = new();

    public static QuickAccessList For(AppSettings settings) => Lists.GetValue(settings, s => s.ToQuickAccess());
}
