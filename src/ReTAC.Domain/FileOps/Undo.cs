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
public sealed record UndoItem(string? Before, string After, ItemStamp Stamp, bool Overwrote);

/// <summary>R-82: 1 回のコマンドの記録。1 回の「元に戻す」で戻る単位。</summary>
/// <param name="CreatedFolders">操作のために作ったフォルダ（浅い順）。戻した後、空なら消す</param>
/// <param name="RemovedFolders">後片付けで消したフォルダ（浅い順）。戻す前に作り直す</param>
public sealed record UndoRecord(
    UndoKind Kind,
    IReadOnlyList<UndoItem> Items,
    IReadOnlyList<string> CreatedFolders,
    IReadOnlyList<string> RemovedFolders);

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
