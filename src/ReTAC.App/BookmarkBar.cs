using System.Drawing;
using System.Windows.Forms;
using ReTAC.Domain.Navigation;

namespace ReTAC.App;

/// <summary>
/// R-89: ブックマークバー。ブラウザのブックマークバーと同じ仕組みでツールバーを兼ねる。
/// マウスでは（Click がフォーカスを取らないので）今までどおりファイル操作のキーはファイルリストだけが拾う。
/// R-107: `B` で明示的にフォーカスを移したときだけキーボードで操作できる（EnterKeyboardSelection）。
/// TabStop は立てない（既定で false。L のドライブバーと同じく Tab の巡回には混ざらない）。
/// ToolStrip は TabStop が false の間だけ Esc で元のコントロールへ焦点を戻す仕組みを自前で持つので
/// （フォーカスを渡す直前のコントロールを WM_SETFOCUS の wParam から覚えている）、それに任せる。
/// 入りきらない項目は ToolStrip の標準のオーバーフロー（»）に回す。
/// </summary>
public sealed class BookmarkBar : ToolStrip
{
    public BookmarkBar()
    {
        Dock = DockStyle.Top;
        GripStyle = ToolStripGripStyle.Hidden;
        CanOverflow = true;
        ShowItemToolTips = true;
        AutoSize = false;   // 高さはアドレスバーの行に揃える（SetRowHeight）
        ToolStripExtras.WidenOverflow(this);
    }

    /// <summary>
    /// `B`（ブックマークバーへ移動・R-107）。先頭の項目を選んだ状態でフォーカスする。
    /// 呼び出し側は事前に BookmarkRules.CanFocusBar で非表示・空でないことを確かめてから呼ぶこと。
    /// </summary>
    public bool EnterKeyboardSelection()
    {
        if (Items.Count == 0) return false;
        Focus();
        // ToolStrip.SelectNextToolStripItem は internal で外から呼べない。項目自身の Select()（public）で選ぶ
        Items[0].Select();
        return true;
    }

    /// <summary>上部の行（ドライブバー・アドレスバー）と同じ高さ・左右の余白にする。項目は縦の中央に並ぶ。</summary>
    public void SetRowHeight(int height, int sideMargin)
    {
        Height = height;
        Padding = new Padding(sideMargin, 0, sideMargin, 0);
    }

    /// <summary>
    /// R-107: 入りきらない項目をまとめる「»」（ToolStripOverflowButton）は ToolStrip が自前で作る組み込みの型で、
    /// 継承して独自の ProcessDialogKey を持たせられない。バー本体でこの 1 件だけ横取りする
    /// （BarDropDownButton と同じ「↑ は末尾を選んで開く」を、こちらにも揃えるため）。
    /// 末尾は OverflowButton.DropDownItems からは取れない（常に空）ので、BarKeyboardNav.OverflowSelectable で拾う。
    /// </summary>
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Up && OverflowButton.Selected && OverflowButton.Enabled && OverflowButton.HasDropDownItems
            && BookmarkRules.BarVerticalKey(canOpen: true, down: false) == BookmarkRules.BarVerticalKeyAction.OpenSelectLast)
        {
            OverflowButton.ShowDropDown();
            BarKeyboardNav.OverflowSelectable(this).LastOrDefault()?.Select();
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }

    private BookmarkItems? _items;
    /// <summary>R-89: 項目の無い所の右クリック。項目の上は項目の MouseUp が受ける。</summary>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right && GetItemAt(e.Location) is null) _items?.Host.ShowContextMenu(null, PointToScreen(e.Location));
    }

    public void Rebuild(IReadOnlyList<Bookmark> bar, BookmarkBarStyle style, BookmarkItems items)
    {
        // ドロップの受け口は 1 度だけ付ける（作り直しのたびに付けると、1 回のドロップで何件も入る）
        if (_items is null) BookmarkDropZone.Attach(this, items.Host.Bookmarks.Bar, items.Host, vertical: false);
        _items = items;
        SuspendLayout();
        BookmarkItems.Clear(Items);
        var size = 16 * DeviceDpi / 96;
        ImageScalingSize = new Size(size, size);   // 拡大縮小でぼやけないよう、取ったアイコンの大きさに合わせる
        if (bar.Count == 0)
        {
            // R-89: 空のときの案内。押しても何も起きない
            var hint = new ToolStripLabel("フォルダやファイルをここへドラッグして追加") { ForeColor = SystemColors.GrayText };
            items.AttachContextMenu(hint);   // Tag が無いので空いた所と同じメニュー
            Items.Add(hint);
        }
        else
        {
            Items.AddRange(items.BarItems(bar, style));
        }
        ResumeLayout();
    }
}
