using ReTAC.Domain.Entries;

namespace ReTAC.Domain.Selection;

/// <summary>
/// ステータスバー ②③ 区画の内容（R-34 / R-34-2 / R-34-3）。
///
/// 利用者による実機確認（2026-09-09）で、②③ は同時にではなく<b>区画ごとに独立して</b>
/// 切り替わることが分かった。R-34-3 の本文より、こちらの実測が正である。
///
/// <list type="table">
///   <item><term>マークなし</term>
///     <description>②③ ともアイコンはそのまま。カレントフォルダ全体の件数を表示</description></item>
///   <item><term>ファイルだけマーク</term>
///     <description>③ のみ★に切り替わりマーク数を表示。② はフォルダアイコンのまま総数を表示</description></item>
///   <item><term>フォルダをマーク</term>
///     <description>②③ とも★に切り替わり、それぞれマーク数を表示</description></item>
/// </list>
/// </summary>
public readonly record struct ListSummary(
    int FolderCount,
    bool FolderFromMarks,
    int FileCount,
    long TotalSize,
    bool FileFromMarks)
{
    public static ListSummary Of(ListState state)
    {
        var totalFolders = 0;
        var totalFiles = 0;
        var totalSize = 0L;
        var markedFolders = 0;
        var markedFiles = 0;
        var markedSize = 0L;

        for (var i = 0; i < state.Entries.Count; i++)
        {
            var entry = state.Entries[i];
            if (entry.IsParent) continue;   // R-04: 親フォルダ項目は数えない
            var marked = state.Marks.Contains(i);

            if (entry.Kind == EntryKind.Folder)
            {
                totalFolders++;
                if (marked) markedFolders++;
                continue;
            }

            totalFiles++;
            totalSize += entry.Size;
            if (!marked) continue;
            markedFiles++;
            markedSize += entry.Size;
        }

        // ② はフォルダがマークされたときだけ切り替わる
        var folderFromMarks = markedFolders > 0;
        // ③ はマークが 1 件でもあれば切り替わる（フォルダだけをマークした場合も★になる）
        var fileFromMarks = state.Marks.Count > 0;

        return new ListSummary(
            folderFromMarks ? markedFolders : totalFolders,
            folderFromMarks,
            fileFromMarks ? markedFiles : totalFiles,
            fileFromMarks ? markedSize : totalSize,
            fileFromMarks);
    }
}
