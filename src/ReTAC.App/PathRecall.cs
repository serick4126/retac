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
                Show(input, Items(history.Recent, []), "（履歴がありません）");
                return true;
            case Keys.Down:
                // R-53: クイックアクセスはフルパスで示す。宛先として使うため別名では困る
                Show(input, Items([], QuickPaths(quickAccess)), "（登録がありません）");
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// マウス用。履歴とクイックアクセスを区切り線で分けて 1 つの一覧に出す。
    /// 卓駆は入力欄の右端の ▲▼ に割り当てていたが、押しにくいので 1 つのボタンにまとめた（実機指摘）。
    /// </summary>
    public static void ShowBoth(TextBox input, FolderHistory history, QuickAccessList? quickAccess) =>
        Show(input, Items(history.Recent, QuickPaths(quickAccess)), "（履歴もクイックアクセスもありません）");

    /// <summary>
    /// 一覧の項目を作る。履歴は実パスをそのまま出し、クイックアクセスは保存した値（式）を印を外して出す（R-53）。
    /// 履歴とクイックアクセスの両方があるときは、空ラベルの項目（区切り線）を挟む。
    /// </summary>
    internal static List<RecallItem> Items(IReadOnlyList<string> history, IReadOnlyList<string> quickSaved)
    {
        List<RecallItem> items = [.. history.Select(p => new RecallItem(p, p, false))];
        if (items.Count > 0 && quickSaved.Count > 0) items.Add(new RecallItem("", "", false));
        items.AddRange(quickSaved.Select(s => new RecallItem(PathExpression.Display(s), s, true)));
        return items;
    }

    /// <summary>
    /// 項目を選んだとき欄に入れる文字。履歴は実パス、クイックアクセスは保存した式から作った実パスで、
    /// どちらも式に書いて入れる（R-147）。保存した値が使えなければ入れず、知らせる文を返す（旧設定の値は登録し直しを促す）。
    /// </summary>
    internal static RecallPick Pick(RecallItem item, Func<string, string?> env)
    {
        if (!item.IsSaved) return new RecallPick(PathField.Text(item.Value), null);
        if (PathExpression.ResolveSaved(item.Value, env) is { } real) return new RecallPick(PathField.Text(real), null);
        var display = PathExpression.Display(item.Value);
        return new RecallPick(null, PathExpression.Read(item.Value) is null ? $"{display} は登録し直してください。" : $"{display} は使えません。");
    }

    /// <summary>R-92: 宛先・ジャンプ先に使うので、フォルダの項目だけを出す（ファイル・コマンドは出さない）。</summary>
    private static List<string> QuickPaths(QuickAccessList? quickAccess) =>
        quickAccess?.Items.Where(q => q.Kind == BookmarkKind.Folder).Select(q => q.Path).ToList() ?? [];

    private static void Show(TextBox input, IReadOnlyList<RecallItem> recall, string emptyLabel)
    {
        var items = recall.Select(item => (item.Label, (Action)(() =>
        {
            if (item.Label.Length == 0) return;   // 区切り線
            var pick = Pick(item, Environment.GetEnvironmentVariable);
            if (pick.Insert is null)
            {
                MessageBox.Show(input.FindForm(), pick.Message, "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            input.Text = pick.Insert;
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

/// <summary>一覧の 1 項目。Value は履歴なら実パス、クイックアクセスなら保存した値（IsSaved）。Label が空なら区切り線。</summary>
internal readonly record struct RecallItem(string Label, string Value, bool IsSaved);

/// <summary>選んだ結果。Insert が null なら欄に入れず、Message を知らせる。</summary>
internal readonly record struct RecallPick(string? Insert, string? Message);
