using System.Windows.Forms;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Tools;

namespace ReTAC.App;

/// <summary>
/// 分類ごとの組み込みコマンドと登録済みの外部ツールの一覧。キー割り当ての画面と、
/// コマンドを選ぶダイアログ（R-89・R-92）で同じ一覧を出す。項目の Tag が CommandTarget（「割り当てなし」は null）。
/// </summary>
public static class CommandCatalog
{
    private const string ToolCategory = "登録した外部ツール";

    /// <summary>絞り込みの語で一覧を作り直す。分類名でも名前でも当たる。</summary>
    /// <param name="includeUnassigned">先頭に「割り当てなし」の行を置く（キー割り当ての画面だけ）</param>
    public static void Fill(ListView list, string filter, IReadOnlyList<ExternalTool> tools, bool includeUnassigned)
    {
        filter = filter.Trim();
        bool Matches(string category, string label) =>
            filter.Length == 0
            || label.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || category.Contains(filter, StringComparison.OrdinalIgnoreCase);

        list.BeginUpdate();
        list.Items.Clear();
        list.Groups.Clear();

        if (includeUnassigned)
        {
            // 先頭に解除用の 1 行。グループ名を付けないと Windows が「既定」の見出しでまとめてしまう
            var none = new ListViewGroup("解除");
            list.Groups.Add(none);
            list.Items.Add(new ListViewItem([CommandLabels.Unassigned, ""]) { Group = none, Tag = null });
        }

        ListViewGroup? group = null;
        foreach (var (category, command, label) in CommandLabels.Grouped)
        {
            if (!Matches(category, label)) continue;
            if (group?.Header != category)
            {
                group = new ListViewGroup(category);
                list.Groups.Add(group);
            }
            list.Items.Add(new ListViewItem([label, ""]) { Group = group, Tag = new BuiltinTarget(command) });
        }

        ListViewGroup? toolGroup = null;
        foreach (var tool in tools)
        {
            if (!Matches(ToolCategory, tool.Name)) continue;
            if (toolGroup is null)
            {
                toolGroup = new ListViewGroup(ToolCategory);
                list.Groups.Add(toolGroup);
            }
            list.Items.Add(new ListViewItem([tool.Name, ""]) { Group = toolGroup, Tag = new ToolTarget(tool.Id) });
        }

        list.EndUpdate();
    }
}
