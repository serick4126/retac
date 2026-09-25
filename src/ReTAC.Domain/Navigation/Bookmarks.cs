using System.IO;
using ReTAC.Domain.Commands;

namespace ReTAC.Domain.Navigation;

/// <summary>R-89 / R-92: 登録先の種類。値の名前は設定ファイルに載るので変えない。</summary>
public enum BookmarkKind { Folder, File, Command, Group }

/// <summary>R-89: バーの表示の形。値の名前は設定ファイルに載るので変えない。</summary>
public enum BookmarkBarStyle { IconAndText, IconOnly, TextOnly }

/// <summary>
/// R-89: ブックマーク 1 件。Target は Folder / File ならパス、Command なら CommandTarget.Serialize() の文字列。
/// Group のときだけ Children を使う（段数の制限は無い）。重複は許す（Q5）。
/// </summary>
public sealed record Bookmark(string Title, BookmarkKind Kind, string Target = "", List<Bookmark>? Children = null)
{
    /// <summary>
    /// R-98: 画面に出さない安定 ID。展開状態はグループのこの ID で覚える。位置引数にせず初期化子で持つので、
    /// new で作れば新しい ID、with（編集・名前変更）では元の ID のまま、JSON に無ければ読み込み時に新しい ID が付く。
    /// 一意性は BookmarkRules.EnsureIds が読み込み時に保証する。
    /// </summary>
    public string Id { get; init; } = NewId();

    /// <summary>
    /// R-106: バーに直接置いたボタンだけに効く「アイコンだけ表示」。グループの中・ブックマークメニュー・
    /// 左パネルのビューでは指定があっても名前を出す（BookmarkRules.BarStyleOf で判定に使うのはバーだけ）。
    /// 種類を問わず持てる。既定 false。`required` を付けない（S-14。欠けた設定は false で埋まる）。
    /// </summary>
    public bool IconOnly { get; init; }

    internal static string NewId() => Guid.NewGuid().ToString("N");
}

/// <summary>R-89: 置き場は 2 つで固定（INV-BOOKMARK-FIXED-ROOTS）。固定の欄で持つので、削除・改名・移動できない。</summary>
public sealed class BookmarkSet
{
    public List<Bookmark> Bar { get; set; } = [];
    public List<Bookmark> Other { get; set; } = [];
}

public static class BookmarkRules
{
    /// <returns>誤りの説明。正しければ null</returns>
    public static string? Validate(Bookmark b) => b.Kind switch
    {
        BookmarkKind.Group when string.IsNullOrWhiteSpace(b.Title) => "グループの名前を入れてください。",
        BookmarkKind.Group => null,
        _ when string.IsNullOrWhiteSpace(b.Target) => "登録先を入れてください。",
        // R-106-2: コマンドの名前は任意。空なら DisplayName がコマンドの名前で表示する。グループには名前の代わりが無いので必須のまま
        BookmarkKind.Command when CommandTarget.Parse(b.Target) is null => "コマンドを読み取れません。",
        _ => null,
    };

    /// <summary>
    /// R-106: バーのボタンの表示の形。項目の IconOnly（アイコンがある場合）が全体の BookmarkBarStyle より優先する。
    /// アイコンの無い項目を「アイコンだけ」にすると空のボタンになるので、そのときは名前を出す（全体の IconOnly と同じ扱い）。
    /// </summary>
    public static BookmarkBarStyle BarStyleOf(Bookmark b, BookmarkBarStyle global, bool hasIcon)
    {
        if (b.IconOnly && hasIcon) return BookmarkBarStyle.IconOnly;
        return global == BookmarkBarStyle.IconOnly && !hasIcon ? BookmarkBarStyle.TextOnly : global;
    }

    /// <summary>
    /// Q4 / Q10: 消した外部ツールを指す項目と、読めないコマンドを落とす。入れ子の中も落とす。
    /// コピーを返さずその場で書き換え、変わったら true（KeyMap.DropUnknownTools と同じ形）。
    /// </summary>
    public static bool DropUnknownTools(BookmarkSet set, IEnumerable<int> existingToolIds)
    {
        var ids = existingToolIds.ToHashSet();
        return Drop(set.Bar, ids) | Drop(set.Other, ids);   // | は両方を必ず走らせる
    }

    private static bool Drop(List<Bookmark> items, HashSet<int> ids)
    {
        // 手で書いた JSON の null（System.Text.Json は非 nullable の欄にも入れる）も、ここで落とす・空にする
        var changed = items.RemoveAll(b => b is null || (b.Kind == BookmarkKind.Command && !IsKnown(b.Target ?? "", ids))) > 0;
        for (var i = 0; i < items.Count; i++)
            if (items[i] is { Title: null } or { Target: null })
                items[i] = items[i] with { Title = items[i].Title ?? "", Target = items[i].Target ?? "" };
        foreach (var group in items.Where(b => b.Children is not null)) changed |= Drop(group.Children!, ids);
        return changed;
    }

    /// <summary>
    /// R-98: 空・重複の ID を新しい ID に振り直す（手で書いた JSON・複製への備え）。先に出てきた 1 件は元の ID のまま。
    /// DropUnknownTools と同じく、その場で書き換えて変わったら true。
    /// </summary>
    public static bool EnsureIds(BookmarkSet set)
    {
        var seen = new HashSet<string>();
        return EnsureIds(set.Bar, seen) | EnsureIds(set.Other, seen);   // | は両方を必ず走らせる
    }

    private static bool EnsureIds(List<Bookmark> items, HashSet<string> seen)
    {
        var changed = false;
        for (var i = 0; i < items.Count; i++)
        {
            if (string.IsNullOrEmpty(items[i].Id) || !seen.Add(items[i].Id))
            {
                // record なので差し替えになる。子の並びは同じインスタンスを引き継ぐ
                items[i] = items[i] with { Id = Bookmark.NewId() };
                seen.Add(items[i].Id);
                changed = true;
            }
            if (items[i].Children is { } children) changed |= EnsureIds(children, seen);
        }
        return changed;
    }

    /// <summary>
    /// その項目（参照が同じもの）を、入れ子の中まで探して取り除く。取り除けたら true。
    /// 重複を許すので、値が同じ別の項目を消さないよう参照で比べる。
    /// </summary>
    public static bool Remove(BookmarkSet set, Bookmark target)
    {
        if (Locate(set, target) is not var (list, index)) return false;
        list.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// 項目を dest の index の前へ移す（並べ替え・グループへの出し入れ）。移せたら true。
    /// グループを自分の中へは移せない（入れ子が輪になって消える）。index は移す前の並びで数える。
    /// </summary>
    public static bool Move(BookmarkSet set, Bookmark item, List<Bookmark> dest, int index)
    {
        if (Owns(item, dest) || Locate(set, item) is not var (source, from)) return false;
        source.RemoveAt(from);
        if (ReferenceEquals(source, dest) && from < index) index--;
        dest.Insert(Math.Clamp(index, 0, dest.Count), item);
        return true;
    }

    private static bool Owns(Bookmark group, List<Bookmark> list) =>
        group.Children is { } children && (ReferenceEquals(children, list) || children.Any(b => Owns(b, list)));

    /// <summary>
    /// その項目（参照が同じもの）が入っている並びと位置。入れ子の中まで探す。無ければ null。
    /// 後ろへの追加・差し替えの起点（record なので IndexOf は値で比べてしまい、重複した別の項目を指す）。
    /// </summary>
    public static (List<Bookmark> List, int Index)? Locate(BookmarkSet set, Bookmark target) =>
        Locate(set.Bar, target) ?? Locate(set.Other, target);

    private static (List<Bookmark>, int)? Locate(List<Bookmark> items, Bookmark target)
    {
        var index = items.FindIndex(b => ReferenceEquals(b, target));
        if (index >= 0) return (items, index);
        foreach (var group in items.Where(b => b.Children is not null))
            if (Locate(group.Children!, target) is { } found) return found;
        return null;
    }

    /// <summary>コマンドの文字列が読めて、外部ツールなら今もある。</summary>
    internal static bool IsKnown(string target, HashSet<int> ids) => CommandTarget.Parse(target) switch
    {
        ToolTarget tool => ids.Contains(tool.ToolId),
        null => false,
        _ => true,
    };

    /// <summary>
    /// R-107: 「ブックマークバーへ移動」を実行してよいか。バーが非表示、または項目が無い（案内の文だけ）なら
    /// 何もしない（Q17）。ドライブバーの「非表示ならポップアップ」（R-77）とは違う扱い。
    /// </summary>
    public static bool CanFocusBar(bool shown, int barCount) => shown && barCount > 0;

    /// <summary>
    /// R-107: ブックマークバーから開いたドロップダウンの中で ←/→ を呑み込むか（バーの項目間の移動へ漏らさないため）。
    /// level はドロップダウンの深さ（バーの項目から直接開いた 1 段目 = 1、その中のグループ・フォルダの中は 2 以降）。
    /// hasSubmenu は選んだ項目がさらに開けるか（サブフォルダ・サブグループ）。forward は → なら true、← なら false。
    /// → は「開けるものが無ければ何もしない」で段数を問わない。← は 1 段目だけ何もしない
    /// （2 段目以降は標準どおり 1 段閉じて戻る。バーの項目間の移動は Esc で抜けてから行う）。
    /// </summary>
    public static bool SwallowArrowKey(int level, bool hasSubmenu, bool forward) =>
        forward ? !hasSubmenu : level <= 1;

    /// <summary>R-107: バーのボタン自身（バー直下、まだ何も開いていない状態）に ↑/↓ が来たときにすること。</summary>
    public enum BarVerticalKeyAction
    {
        /// <summary>開けないボタン（ファイル・コマンド）。何もしない（横並びのバーで上下移動に使わせない）。</summary>
        None,
        /// <summary>↓: 開いて先頭の項目を選ぶ（フォルダ・グループ・オーバーフロー「»」の今までの動き）。</summary>
        OpenSelectFirst,
        /// <summary>↑: 開いて末尾の項目を選ぶ。</summary>
        OpenSelectLast,
    }

    /// <summary>
    /// R-107: canOpen はそのボタンが開けるか（フォルダ・グループ・「»」）。down は ↓ なら true、↑ なら false。
    /// </summary>
    public static BarVerticalKeyAction BarVerticalKey(bool canOpen, bool down) =>
        !canOpen ? BarVerticalKeyAction.None : down ? BarVerticalKeyAction.OpenSelectFirst : BarVerticalKeyAction.OpenSelectLast;

    /// <summary>
    /// R-107: 1 段目のドロップダウンの端で ↑/↓ が来たとき、バーのボタンへ戻ってよいか（Esc と同じ、
    /// バーがキーボード操作を持ち続ける形）。2 段目以降は対象外（標準の折り返しのまま）。
    /// isFirst/isLast は区切り線・無効な項目を除いた並びでの位置。↑ は先頭のときだけ、↓ は末尾のときだけ戻る。
    /// </summary>
    public static bool ReturnsToBarButton(int level, bool isFirst, bool isLast, bool down) =>
        level <= 1 && (down ? isLast : isFirst);

    /// <summary>
    /// R-107: 「»」の一覧（入りきらない項目）の中で ↑/↓ を押したとき、次に選ぶ項目の位置。null は一覧を閉じて
    /// 「»」のボタンへ戻す。「»」の一覧は 1 段目として扱うので、端では ReturnsToBarButton と同じくボタンへ戻る。
    /// 見た目の上下ではなく並び順で隣へ移るのは、「»」の一覧が項目を横に詰めて折り返す並べ方で、アイコンだけの
    /// 項目（R-106）が 1 行に何個も並ぶと、見た目の上下だけでは同じ行の項目へ届かないため（←/→ はここでは何もしない）。
    /// index は選べる項目だけの並びでの位置。見つからない（負）ときは、迷わせないようボタンへ戻す。
    /// </summary>
    public static int? OverflowListStep(int index, int count, bool down) =>
        index < 0 || ReturnsToBarButton(1, isFirst: index == 0, isLast: index == count - 1, down) ? null : index + (down ? 1 : -1);

    /// <summary>表示名。題名が空の Folder / File はパスの末尾の名前（ルートはパスそのもの）。</summary>
    public static string DisplayName(Bookmark b, Func<CommandTarget, string> commandLabel) => b switch
    {
        _ when !string.IsNullOrWhiteSpace(b.Title) => b.Title,
        { Kind: BookmarkKind.Command } when CommandTarget.Parse(b.Target) is { } target => commandLabel(target),
        _ => Path.GetFileName(b.Target.TrimEnd('\\')) is { Length: > 0 } name ? name : b.Target,
    };
}
