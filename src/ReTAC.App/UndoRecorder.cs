using System.IO;
using ReTAC.Domain.FileOps;
using ReTAC.Shell;

namespace ReTAC.App;

/// <summary>
/// R-84: 1 回のコマンドで起きた結果を集め、元に戻すの記録にする。
/// 種類ごとに 1 件。ただしコピーと移動は 1 件にまとめる（ドロップで混ざっても Ctrl+Z 1 回で戻る。UndoRecord.Transfer）。
/// </summary>
internal sealed class UndoRecorder
{
    private readonly List<UndoItem> _renames = [], _moves = [], _copies = [], _creates = [];
    private readonly List<string> _createdFolders = [], _removedFolders = [];
    /// <summary>OS に渡す前に宛先に同名があった項目（上書き）。</summary>
    private readonly HashSet<string> _overwritten = new(StringComparer.OrdinalIgnoreCase);

    public void MarkOverwrite(string destinationPath) => _overwritten.Add(destinationPath);

    /// <summary>OS の操作 1 回の結果。日時は Commit で取る。</summary>
    private readonly List<OperationResult> _results = [];

    public void AddResults(IEnumerable<OperationResult> results) =>
        // 中身を除くのは操作 1 回の中だけ。操作をまたいで除くと、コピーしたフォルダの中へ移した項目が記録から落ち、戻せなくなる
        _results.AddRange(UndoRecord.TopLevel(results, r => r.Created));

    /// <summary>
    /// 日時は、コマンドのすべての操作が終わってから取る。操作ごとに取ると、ドロップの後半の移動（R-93）で前半にコピーした
    /// フォルダの中身が増え、外から何も変えていないのに「記録した後に変更されています」になる。
    /// </summary>
    private void StampResults()
    {
        foreach (var result in _results)
        {
            if (Stamp(result.Created) is not { } stamp) continue;
            var item = new UndoItem(result.Source, result.Created, stamp, _overwritten.Contains(result.Created));
            (result.Kind switch
            {
                OperationKind.Rename => _renames,
                OperationKind.Move => _moves,
                _ => _copies,
            }).Add(item);
        }
        _results.Clear();
    }

    /// <summary>`K`・`O`・連結で作った項目。作る前に無かったものだけを渡すこと。</summary>
    public void AddCreated(string path)
    {
        if (Stamp(path) is { } stamp) _creates.Add(new UndoItem(null, path, stamp, false));
    }

    /// <summary>転送のために作ったフォルダ。作る前に無かったものだけを、浅い順に渡すこと。</summary>
    public void AddCreatedFolder(string path) => _createdFolders.Add(path);

    public void AddRemovedFolder(string path) => _removedFolders.Add(path);

    /// <summary>履歴へ積む。空の種類は積まない（UndoHistory.Push が捨てる）。</summary>
    public void Commit(UndoHistory history)
    {
        StampResults();
        history.Push(new UndoRecord(UndoKind.Rename, _renames, [], []));
        history.Push(new UndoRecord(UndoKind.Create, _creates, [], []));

        // RemovedFolders は浅い順が約束（UndoLast が深い順に作り直す）
        var removed = _removedFolders.OrderBy(f => f.Length).ToList();
        if (_moves.Count == 0 && _copies.Count == 0 && _createdFolders.Count > 0)
        {
            // 転送は 0 件で宛先のフォルダだけを作った。作ったフォルダの「作成」として積む
            // 一番上の段だけを項目にする（下の段は上の段と一緒にごみ箱へ入る）
            var folders = _createdFolders
                .Where(f => !_createdFolders.Any(p => f.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                .Select(f => Stamp(f) is { } s ? new UndoItem(null, f, s, false) : null)
                .OfType<UndoItem>().ToList();
            history.Push(new UndoRecord(UndoKind.Create, folders, [], []));
            return;
        }
        history.Push(UndoRecord.Transfer(_copies, _moves, [.. _createdFolders], removed));
    }

    public static ItemStamp? Stamp(string path)
    {
        try
        {
            if (Directory.Exists(path)) return new ItemStamp(true, 0, Directory.GetLastWriteTimeUtc(path));
            if (File.Exists(path)) return new ItemStamp(false, new FileInfo(path).Length, File.GetLastWriteTimeUtc(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }

    public static bool IsEmptyFolder(string path)
    {
        try { return Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>path を作る前に無かった段を、浅い順に返す（Directory.CreateDirectory は途中の段もまとめて作る）。</summary>
    public static List<string> MissingLevels(string path)
    {
        var missing = new List<string>();
        for (var current = path; current is not null && !Directory.Exists(current);
             current = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(current)))
            missing.Insert(0, current);
        return missing;
    }
}
