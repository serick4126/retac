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
        BookmarkKind.Command when string.IsNullOrWhiteSpace(b.Title) => "名前を入れてください。",
        BookmarkKind.Command when CommandTarget.Parse(b.Target) is null => "コマンドを読み取れません。",
        _ => null,
    };

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

    /// <summary>R-98: ids のうち、今あるグループの ID だけ。消えたグループ・グループでない項目の状態は捨てる。</summary>
    public static IEnumerable<string> ExistingGroupIds(BookmarkSet set, IEnumerable<string> ids)
    {
        var groups = new HashSet<string>();
        void Collect(List<Bookmark> items)
        {
            foreach (var b in items.Where(b => b.Kind == BookmarkKind.Group))
            {
                groups.Add(b.Id);
                if (b.Children is { } children) Collect(children);
            }
        }
        Collect(set.Bar);
        Collect(set.Other);
        return ids.Where(groups.Contains).Distinct();
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

    /// <summary>表示名。題名が空の Folder / File はパスの末尾の名前（ルートはパスそのもの）。</summary>
    public static string DisplayName(Bookmark b, Func<CommandTarget, string> commandLabel) => b switch
    {
        _ when !string.IsNullOrWhiteSpace(b.Title) => b.Title,
        { Kind: BookmarkKind.Command } when CommandTarget.Parse(b.Target) is { } target => commandLabel(target),
        _ => Path.GetFileName(b.Target.TrimEnd('\\')) is { Length: > 0 } name ? name : b.Target,
    };
}
