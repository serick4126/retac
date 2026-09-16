using System.IO;

namespace ReTAC.Domain.FileOps;

/// <summary>転送 1 件。フォルダは作るだけで、転送するのはファイルだけ。</summary>
/// <param name="Source">転送元のファイル</param>
/// <param name="DestinationFolder">転送先のフォルダ（存在しなければ作る）</param>
/// <param name="NewName">別名で複写する場合の名前。元の名前のままなら null</param>
public sealed record CopyPlanItem(string Source, string DestinationFolder, string? NewName = null);

/// <param name="Folders">先に作っておく必要のあるフォルダ（深い順ではなく浅い順）</param>
/// <param name="Items">実際に OS へ渡す転送</param>
/// <param name="Skipped">複写条件によって転送しないと判断した件数</param>
/// <param name="Conflicts">
/// 同名衝突が起きた件数。<b>0 なら宛先に重なるものが一つも無い</b>ことを意味する。
/// 移動をフォルダごと OS に渡してよいかの判断に使う（数千件の移動で効く）。
/// </param>
public sealed record CopyPlan(IReadOnlyList<string> Folders, IReadOnlyList<CopyPlanItem> Items, int Skipped,
                              int Conflicts = 0);

/// <summary>
/// R-41-4: 同名衝突時の複写条件を自前で判定し、<b>実際に転送すべき対象だけ</b>を OS に渡すための計画を作る。
/// フォルダは再帰的に辿る。これにより「新しい時に複写」が何百件の衝突でも 1 回の指定で済む（R-41-6）。
/// </summary>
public static class CopyPlanner
{
    /// <summary>衝突 1 件。16.8 節のダイアログは元と先の日時・サイズを並べて示す。</summary>
    public sealed record Conflict(string Source, string Destination,
        DateTime SourceTime, DateTime DestinationTime, long SourceSize, long DestinationSize);

    /// <summary>衝突条件を一括で決める場合（R-41-6 のチェックボックス）。</summary>
    public static CopyPlan Build(IEnumerable<string> sources, string destinationFolder, CopyCondition condition) =>
        Build(sources, destinationFolder, _ => condition);

    /// <param name="resolve">
    /// 衝突するたびに呼ばれ、その 1 件の複写条件を返す（R-41-5）。
    /// 「以降すべてに適用」は呼び出し側が同じ値を返し続けることで表す。
    /// </param>
    public static CopyPlan Build(IEnumerable<string> sources, string destinationFolder,
        Func<Conflict, CopyCondition> resolve)
    {
        var folders = new List<string>();
        var items = new List<CopyPlanItem>();
        var skipped = 0;
        var conflicts = 0;

        foreach (var source in sources)
        {
            if (Directory.Exists(source))
            {
                var target = Path.Combine(destinationFolder, Path.GetFileName(Path.TrimEndingDirectorySeparator(source)));
                AddFolder(source, target, resolve, folders, items, ref skipped, ref conflicts);
            }
            else if (File.Exists(source))
            {
                AddFile(source, destinationFolder, resolve, items, ref skipped, ref conflicts);
            }
        }

        return new CopyPlan(folders, items, skipped, conflicts);
    }

    private static void AddFolder(string source, string target, Func<Conflict, CopyCondition> resolve,
        List<string> folders, List<CopyPlanItem> items, ref int skipped, ref int conflicts)
    {
        folders.Add(target);

        // ジャンクション／シンボリックリンクは辿らない（理由は TransferGuards.CanDescend）
        if (!TransferGuards.CanDescend(source)) return;

        foreach (var file in Directory.EnumerateFiles(source))
            AddFile(file, target, resolve, items, ref skipped, ref conflicts);

        foreach (var directory in Directory.EnumerateDirectories(source))
            AddFolder(directory, Path.Combine(target, Path.GetFileName(directory)), resolve, folders, items, ref skipped, ref conflicts);
    }

    private static void AddFile(string source, string destinationFolder, Func<Conflict, CopyCondition> resolve,
        List<CopyPlanItem> items, ref int skipped, ref int conflicts)
    {
        var name = Path.GetFileName(source);
        var destination = Path.Combine(destinationFolder, name);
        var sourceInfo = new FileInfo(source);

        // 宛先が無ければ衝突なし。そのまま転送する
        if (!File.Exists(destination))
        {
            items.Add(new CopyPlanItem(source, destinationFolder));
            return;
        }

        conflicts++;
        var destinationInfo = new FileInfo(destination);
        var condition = resolve(new Conflict(source, destination,
            sourceInfo.LastWriteTime, destinationInfo.LastWriteTime, sourceInfo.Length, destinationInfo.Length));
        var decision = ConflictResolver.Decide(sourceInfo.LastWriteTime, destinationInfo.LastWriteTime, condition);

        if (!decision.Transfer) { skipped++; return; }
        items.Add(new CopyPlanItem(source, destinationFolder, decision.RenameTarget ? UniqueName(destinationFolder, name) : null));
    }

    /// <summary>「名前を変更し複写」。エクスプローラーと同じ `名前 (2).拡張子` の作法。</summary>
    private static string UniqueName(string folder, string name)
    {
        var baseName = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var n = 2; ; n++)
        {
            var candidate = $"{baseName} ({n}){extension}";
            if (!File.Exists(Path.Combine(folder, candidate))) return candidate;
        }
    }
}
