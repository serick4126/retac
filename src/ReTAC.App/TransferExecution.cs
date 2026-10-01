using System.IO;
using ReTAC.Domain.FileOps;
using ReTAC.Shell;

namespace ReTAC.App;

/// <summary>
/// R-125: 転送の計画を OS の操作に登録し、移動のあとを片付ける。コピー・移動・ドロップ・貼り付けのすべてが
/// MainForm.ExecuteTransfer を通り、ここへ来る。MainForm から切り出したのは、本物の OS の転送でテストするため（画面に依らない）。
/// </summary>
internal static class TransferExecution
{
    /// <summary>
    /// 計画を作る前に、転送元と宛先を長いパスに直す。計画の中で一覧した名前は長いので、直すのは入口だけでよい
    /// （項目ごとに直すと、ネットワークドライブで 1 件ずつ問い合わせることになる）。
    /// </summary>
    public static (IReadOnlyList<string> Sources, string Destination) LongPaths(IReadOnlyList<string> sources, string destination) =>
        ([.. sources.Select(LongPath.Of)], LongPath.Of(destination));

    /// <summary>
    /// 計画を OS の操作に登録する。先に、転送のために作る・中をたどったフォルダの種類（無い／ファイル／フォルダ）が
    /// 計画の時点と同じかを確かめ、違えば<b>何も登録せず</b>にそのパスを返す
    /// （消えたフォルダを作り直して転送すると、複写条件の判断が当てはまらない）。
    /// </summary>
    /// <returns>種類が計画の時点と違っていたフォルダ。無ければ null</returns>
    public static string? Register(CopyPlan plan, bool moving, ShellFileOperation operation, UndoRecorder? recorder)
    {
        if (plan.Folders.FirstOrDefault(f => DestinationState.Of(f.FullPath).Kind != f.Expected.Kind) is { } changed)
            return changed.FullPath;

        foreach (var folder in plan.Folders)
        {
            if (folder.Expected.Kind == DestinationKind.Folder) continue;   // 既にある。作り直さない
            if (folder.Expected.Kind == DestinationKind.Absent) recorder?.AddCreatedFolder(folder.FullPath);
            Directory.CreateDirectory(folder.FullPath);
        }
        foreach (var item in plan.Items)
        {
            // 上書きの印は、計画で上書きすると決めた項目にだけ付ける（R-84。転送の直前にファイルごとに確かめ直さない）。
            // 計画の後に宛先が変わった項目は、Expect の比較で転送されないので、印の無い上書きは起きない
            if (item.Expected.Kind == DestinationKind.File) recorder?.MarkOverwrite(item.Target);
            operation.Expect(item.Target, item.Expected);
            if (moving) operation.Move(item.Source, item.DestinationFolder, item.NewName);
            else operation.Copy(item.Source, item.DestinationFolder, item.NewName);
        }
        return null;
    }

    /// <summary>
    /// 転送のあとの片付け。移動で、宛先が変わって中止していないときだけ、空になった転送元のフォルダを消す（R-125）。
    /// 宛先が変わって中止したときに消すと、何も転送していないのに、元から空だった転送元のフォルダが消える
    /// （転送 0 件の記録は残らないので、戻せない）。途中で止まった移動は、もう一度実行すれば続きから進む。
    /// </summary>
    /// <param name="changed">計画の後に変わっていた宛先（フォルダの確かめ・項目の確かめのどちらでも）。無ければ null</param>
    public static void Finish(CopyPlan plan, bool moving, string? changed, Action<string> removed)
    {
        if (moving && changed is null) RemoveEmptySources(plan, removed);
    }

    /// <summary>
    /// 移動のあと片付け。<b>中身が残っているフォルダには触らない。</b>
    /// 見るのは、計画が中をたどった転送元のフォルダだけ（深い順）。フォルダごと渡した項目の配下は見ない:
    /// 中止・失敗で動かなかったフォルダの、元から空だった入れ子を消してしまう（宛先には無いので、戻せない）。
    /// </summary>
    private static void RemoveEmptySources(CopyPlan plan, Action<string> removed)
    {
        foreach (var folder in plan.Folders.Select(f => f.Source).OfType<string>().OrderByDescending(f => f.Length))
        {
            try
            {
                if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder);
                    removed(folder);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 消せないなら残しておくだけでよい
            }
        }
    }
}
