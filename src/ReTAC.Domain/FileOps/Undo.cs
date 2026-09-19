using System.IO;

namespace ReTAC.Domain.FileOps;

/// <summary>R-82: 元に戻せる操作の種類。削除は第 2 段階。</summary>
public enum UndoKind { Rename, Move, Copy, Create }

/// <summary>
/// R-85: 記録した時点の中身の手がかり。ファイルはサイズと更新日時、フォルダは更新日時だけを見る
/// （フォルダの更新日時は直下の増減しか映さない。奥の変化は検出しない。全体を調べると大きなフォルダで遅い）。
/// </summary>
public sealed record ItemStamp(bool IsFolder, long Size, DateTime LastWriteTimeUtc);

/// <param name="Before">操作前のパス。作成では null</param>
/// <param name="After">操作後のパス（今あるはずの場所）</param>
/// <param name="Overwrote">コピー・移動で、宛先の同名の項目を上書きした</param>
/// <param name="Kind">記録と種類が違う項目だけが持つ（ドロップでコピーと移動が混ざった 1 件の記録。R-84）。null なら記録の種類</param>
public sealed record UndoItem(string? Before, string After, ItemStamp Stamp, bool Overwrote, UndoKind? Kind = null);

/// <summary>R-82: 1 回のコマンドの記録。1 回の「元に戻す」で戻る単位。</summary>
/// <param name="CreatedFolders">操作のために作ったフォルダ（浅い順）。戻した後、空なら消す</param>
/// <param name="RemovedFolders">後片付けで消したフォルダ（浅い順）。戻す前に作り直す</param>
public sealed record UndoRecord(
    UndoKind Kind,
    IReadOnlyList<UndoItem> Items,
    IReadOnlyList<string> CreatedFolders,
    IReadOnlyList<string> RemovedFolders)
{
    /// <summary>その項目の種類。混ざった記録では項目ごとに違う。</summary>
    public UndoKind KindOf(UndoItem item) => item.Kind ?? Kind;

    /// <summary>戻す手順。実行した順の逆（ドロップのコピー → 移動は、移動 → コピーの順に戻す）。</summary>
    public IEnumerable<(UndoKind Kind, UndoItem Item)> Steps(IEnumerable<UndoItem> items) =>
        items.Reverse().Select(item => (KindOf(item), item));

    /// <summary>
    /// R-84: 1 回のコマンドの転送の記録。ドロップ（R-93）でコピーと移動が混ざっても 1 件にし、Ctrl+Z 1 回で両方戻す。
    /// 作ったフォルダ・消したフォルダは記録全体に付ける（全部戻した後、作ったフォルダのうち空のものを消す）。
    /// </summary>
    /// <param name="copies">実行した順。コピーは移動より先に実行する</param>
    public static UndoRecord Transfer(IReadOnlyList<UndoItem> copies, IReadOnlyList<UndoItem> moves,
                                      IReadOnlyList<string> createdFolders, IReadOnlyList<string> removedFolders)
    {
        if (moves.Count == 0) return new(UndoKind.Copy, copies, createdFolders, removedFolders);
        if (copies.Count == 0) return new(UndoKind.Move, moves, createdFolders, removedFolders);
        return new(UndoKind.Move, [.. copies.Select(c => c with { Kind = UndoKind.Copy }), .. moves], createdFolders, removedFolders);
    }
}

/// <summary>R-82: 履歴。設定には保存しない（INV-UNDO-NOT-PERSISTED）。上限を超えたら古いものから捨てる。</summary>
public sealed class UndoHistory
{
    public const int Capacity = 100;
    private readonly LinkedList<UndoRecord> _records = new();

    public int Count => _records.Count;

    public void Push(UndoRecord record)
    {
        if (record.Items.Count == 0 && record.CreatedFolders.Count == 0) return;
        _records.AddLast(record);
        if (_records.Count > Capacity) _records.RemoveFirst();
    }

    public UndoRecord? Peek() => _records.Last?.Value;

    public void Pop()
    {
        if (_records.Count > 0) _records.RemoveLast();
    }
}

public enum UndoProblem { None, Missing, Changed, OriginalOccupied, OverwroteOnCopy }

/// <summary>R-85: 戻す前に、記録した後の変化を調べる。ファイルシステムを見る部分は呼び出し側が渡す。</summary>
public static class UndoCheck
{
    public static UndoProblem Check(UndoKind kind, UndoItem item,
                                    Func<string, ItemStamp?> probe, Func<string, bool> isEmptyFolder)
    {
        // INV-UNDO-NO-DATA-LOSS: 上書きしたコピーを消すと、上書きされた側も上書きした側も失われる
        if (kind == UndoKind.Copy && item.Overwrote) return UndoProblem.OverwroteOnCopy;

        if (probe(item.After) is not { } now) return UndoProblem.Missing;
        if (Changed(item.Stamp, now)
            // 「作る → 中へコピー → コピーを戻す」の順だと更新日時だけが変わる。空なら失うものは無い
            && !(kind == UndoKind.Create && item.Stamp.IsFolder && isEmptyFolder(item.After)))
            return UndoProblem.Changed;

        if (kind is UndoKind.Rename or UndoKind.Move && item.Before is { } before
            && !string.Equals(before, item.After, StringComparison.OrdinalIgnoreCase)
            && probe(before) is not null)
            return UndoProblem.OriginalOccupied;

        return UndoProblem.None;
    }

    private static bool Changed(ItemStamp then, ItemStamp now) =>
        then.IsFolder != now.IsFolder
        || then.LastWriteTimeUtc != now.LastWriteTimeUtc
        || (!then.IsFolder && then.Size != now.Size);
}

public static class UndoText
{
    public static string Describe(UndoRecord record)
    {
        var what = record.Items.Count switch
        {
            0 => record.CreatedFolders.Count > 0 ? $"『{Name(record.CreatedFolders[^1])}』" : "",
            1 => $"『{Name(record.Kind == UndoKind.Rename ? record.Items[0].Before! : record.Items[0].After)}』",
            var n => $"{n} 個の項目",
        };
        var kinds = record.Items.Select(record.KindOf).Distinct().ToList();
        if (kinds.Count > 1) return what + "のコピーと移動";   // 混ざるのはドロップのコピーと移動だけ
        return what + record.Kind switch
        {
            UndoKind.Rename => "の名前の変更",
            UndoKind.Move => "の移動",
            UndoKind.Copy => "のコピー",
            _ => "の作成",
        };
    }

    /// <summary>確認の文に出す理由。</summary>
    public static string Reason(UndoProblem problem) => problem switch
    {
        UndoProblem.Missing => "見つかりません",
        UndoProblem.Changed => "記録した後に変更されています",
        UndoProblem.OriginalOccupied => "元の場所に同じ名前の項目があります",
        UndoProblem.OverwroteOnCopy => "上書きしたコピーは戻せません",
        _ => "",
    };

    private static string Name(string path) => Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
}
