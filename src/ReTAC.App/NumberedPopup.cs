using System.Drawing;
using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>
/// N-06 / R-57: キーボードから開くポップアップ。カーソル位置に出て、項目に番号が振られる。
/// 番号はニーモニック（<c>&amp;1</c>）として与えるので、メニューでは数字キーを押すだけで決まる。
/// `H`（フォルダ履歴）/ `J`（クイックアクセス）/ `I`（ファイル名コピー）で共用する。
/// </summary>
public static class NumberedPopup
{
    /// <param name="onBackSpace">BackSpace で閉じたときの後始末。null なら既定（1 つ上を選ぶ）のまま</param>
    /// <param name="footer">
    /// 区切り線の下に置く番号なしの項目（卓駆の「履歴のクリア(E)」）。
    /// 番号を振らないのは、数字キーの連打で誤って選ばせないため。
    /// <b>項目が 9 件を超えても番号を失わない</b>のも理由で、だからコマンドはここに置く。
    /// 直接選ぶ手段はラベルの <c>&amp;</c>（ニーモニック）が受け持つ
    /// </param>
    /// <param name="selectFirst">
    /// B-06: 開いた時点で先頭を選ぶか。
    /// <b>ファイルリストのインライン表示（<c>H</c> / <c>I</c> / <c>J</c>）では false。</b>
    /// 卓駆は何も選ばずに開き、<c>↓</c> で先頭・<c>↑</c> で最下段が選ばれる。
    /// パス入力欄の候補だけは開いた時点で先頭が選ばれているのが正しい（利用者確認済み）
    /// </param>
    public static void Show(Control owner, Point anchor, IReadOnlyList<(string Label, Action Choose)> items,
                            string emptyLabel = "（項目がありません）", Action? onBackSpace = null,
                            IReadOnlyList<(string Label, Action Choose)>? footer = null, bool selectFirst = false)
    {
        var menu = new Popup(onBackSpace) { ShowImageMargin = false };

        if (items.Count == 0)
        {
            menu.Items.Add(emptyLabel).Enabled = false;
        }
        else
        {
            var number = 0;
            for (var i = 0; i < items.Count; i++)
            {
                var (label, choose) = items[i];
                // ラベルが空の項目は区切り線として扱う（5-5 節のグループ分けに使う）
                if (label.Length == 0) { menu.Items.Add(new ToolStripSeparator()); continue; }
                var text = Escape(label);
                // 番号は 1〜9 まで。それより後ろは ↑↓ で選ぶ
                number++;
                var item = menu.Items.Add(number <= 9 ? $"&{number}  {text}" : $"    {text}");
                item.Click += (_, _) => choose();
            }
        }

        if (footer is { Count: > 0 })
        {
            menu.Items.Add(new ToolStripSeparator());
            foreach (var (label, choose) in footer) menu.Items.Add(label).Click += (_, _) => choose();
        }

        menu.Closed += (_, _) => menu.BeginInvoke(menu.Dispose);
        menu.Show(owner, anchor);

        // B-06: 既定では何も選ばずに開く。↓ で先頭が選ばれるのは ToolStripDropDown の既定のまま。
        // ↑ だけは Popup が横取りして「区切り線より上の最後」を選ぶ（下段はコマンドなので）
        if (!selectFirst) return;

        foreach (ToolStripItem item in menu.Items)
        {
            if (!item.CanSelect) continue;
            item.Select();
            break;
        }
    }

    /// <summary>
    /// ToolStripDropDown は BackSpace を ProcessDialogKey で「1 つ上を選ぶ」に使ってしまい、
    /// KeyDown まで降りてこない。呼び出し側が意味を持たせたいときだけ横取りする。
    /// </summary>
    private sealed class Popup(Action? onBackSpace) : ContextMenuStrip
    {
        private readonly Action? _onBackSpace = onBackSpace;   // 公開プロパティにするとデザイナ用の属性を要求される

        protected override bool ProcessDialogKey(Keys keyData)
        {
            // B-06: 何も選ばずに開いた直後の ↑ は、既定では最下段＝下段のコマンド
            // （「履歴のクリア」「このフォルダを追加」）に当たる。欲しいのはジャンプ先の最後なので、
            // 末尾の区切り線より上の最後を選ぶ。下段のコマンドは番号かニーモニックで直接選べる
            if (keyData == Keys.Up && LastBeforeTrailingSeparator() is { } entry
                && !Items.Cast<ToolStripItem>().Any(item => item.Selected))
            {
                entry.Select();
                return true;
            }

            if (keyData != Keys.Back || _onBackSpace is not { } handler) return base.ProcessDialogKey(keyData);
            Close();
            handler();
            return true;
        }

        /// <summary>末尾の区切り線より上にある最後の選べる項目。区切り線が無ければ null（既定の挙動に任せる）。</summary>
        private ToolStripItem? LastBeforeTrailingSeparator()
        {
            var separator = Items.Cast<ToolStripItem>().ToList().FindLastIndex(item => item is ToolStripSeparator);
            if (separator < 0) return null;
            for (var i = separator - 1; i >= 0; i--)
                if (Items[i].CanSelect) return Items[i];
            return null;
        }
    }

    /// <summary>`&amp;` を含む名前がニーモニックとして食われないようにする。</summary>
    private static string Escape(string label) => label.Replace("&", "&&");
}
