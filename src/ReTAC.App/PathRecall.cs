using System.Drawing;
using System.Windows.Forms;
using ReTAC.Domain.Navigation;

namespace ReTAC.App;

/// <summary>
/// T6-2 / R-87: パスの入力欄の `↑`（フォルダ履歴）と `↓`（クイックアクセス）の一覧。
/// ダイアログとアドレスバーで同じものを出す。
/// </summary>
public static class PathRecall
{
    /// <summary>`↑` `↓` なら一覧を開いて true。</summary>
    public static bool HandleKey(TextBox input, Keys key, FolderHistory history, QuickAccessList? quickAccess)
    {
        switch (key)
        {
            case Keys.Up:
                Show(input, history.Recent, "（履歴がありません）");
                return true;
            case Keys.Down:
                // R-53: クイックアクセスはフルパスで示す。宛先として使うため別名では困る
                Show(input, QuickPaths(quickAccess), "（登録がありません）");
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// マウス用。履歴とクイックアクセスを区切り線で分けて 1 つの一覧に出す。
    /// 卓駆は入力欄の右端の ▲▼ に割り当てていたが、押しにくいので 1 つのボタンにまとめた（実機指摘）。
    /// </summary>
    public static void ShowBoth(TextBox input, FolderHistory history, QuickAccessList? quickAccess)
    {
        var quick = QuickPaths(quickAccess);
        List<string> paths = [.. history.Recent];
        if (paths.Count > 0 && quick.Count > 0) paths.Add("");   // 空ラベルは区切り線になる
        paths.AddRange(quick);
        Show(input, paths, "（履歴もクイックアクセスもありません）");
    }

    private static List<string> QuickPaths(QuickAccessList? quickAccess) =>
        quickAccess?.Items.Select(q => q.Path).ToList() ?? [];

    private static void Show(TextBox input, IReadOnlyList<string> paths, string emptyLabel)
    {
        var items = paths.Select(path => (path, (Action)(() =>
        {
            if (path.Length == 0) return;   // 区切り線
            input.Text = path;
            input.SelectAll();
            input.Focus();
        }))).ToList();

        // R-46 の全選択と揃える: BackSpace は一覧を閉じて入力欄を空にする。
        // ポップアップに任せると「1 つ上を選ぶ」になってしまう（実機指摘）
        NumberedPopup.Show(input, new Point(0, input.Height), items, emptyLabel,
            onBackSpace: () => { input.Text = ""; input.Focus(); },
            selectFirst: true);
    }
}
