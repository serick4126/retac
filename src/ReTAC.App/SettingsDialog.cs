using System.Drawing;
using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>
/// 統合した設定画面の枠（R-102）。左のサイドバーで 6 ページを切り替え、下端の OK / キャンセル / 適用で
/// 下書き（<see cref="SettingsDraft"/>）をまとめて確定する（R-102-3）。
/// </summary>
public sealed class SettingsDialog : Form
{
    private static readonly (SettingsPage Page, string Label)[] Sidebar =
    [
        (SettingsPage.Environment, "動作環境"),
        (SettingsPage.ColorFont, "配色・フォント"),
        (SettingsPage.KeyAssign, "キー割り当て"),
        (SettingsPage.ExternalTool, "外部ツール"),
        (SettingsPage.DriveVisibility, "表示するドライブ"),
        (SettingsPage.QuickAccess, "クイックアクセス"),
    ];

    // もっとも大きいページ（配色・キー割り当て・外部ツール）が幅 754、キー割り当てが高さ 496。
    // 余白は他の単発ダイアログと合わせて 14px（ExternalToolPage 等）。
    private const int Pad = 14;
    private const int SidebarWidth = 160;
    private const int PageWidth = 754;
    private const int PageHeight = 496;
    private const int ButtonWidth = 90;
    private const int ButtonHeight = 28;
    private const int ButtonGap = 10;

    private readonly SettingsDraft _draft;
    private readonly Func<SettingsDraft, bool> _apply;
    private readonly ListBox _sidebar = new() { Bounds = new Rectangle(Pad, Pad, SidebarWidth, PageHeight), IntegralHeight = false };
    private readonly Dictionary<SettingsPage, Control> _pages;
    private readonly ExternalToolPage _externalToolPage;

    public SettingsDialog(SettingsDraft draft, SettingsPage initial, string currentFolder, Func<SettingsDraft, bool> apply)
    {
        _draft = draft;
        _apply = apply;

        Text = "設定";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;   // ダイアログはタスクバーに出さない
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;

        var pageLeft = Pad + SidebarWidth + Pad;
        var clientWidth = pageLeft + PageWidth + Pad;
        var buttonTop = Pad + PageHeight;
        ClientSize = new Size(clientWidth, buttonTop + ButtonHeight + Pad);

        var pageLocation = new Point(pageLeft, Pad);
        var environmentPage = new EnvironmentPage(draft) { Location = pageLocation };
        var colorFontPage = new ColorFontPage(draft) { Location = pageLocation };
        var keyAssignPage = new KeyAssignPage(draft) { Location = pageLocation };
        _externalToolPage = new ExternalToolPage(draft) { Location = pageLocation };
        var driveVisibilityPage = new DriveVisibilityPage(draft) { Location = pageLocation };
        var quickAccessPage = new QuickAccessPage(draft, currentFolder) { Location = pageLocation };

        _pages = new Dictionary<SettingsPage, Control>
        {
            [SettingsPage.Environment] = environmentPage,
            [SettingsPage.ColorFont] = colorFontPage,
            [SettingsPage.KeyAssign] = keyAssignPage,
            [SettingsPage.ExternalTool] = _externalToolPage,
            [SettingsPage.DriveVisibility] = driveVisibilityPage,
            [SettingsPage.QuickAccess] = quickAccessPage,
        };

        foreach (var (_, label) in Sidebar) _sidebar.Items.Add(label);
        _sidebar.SelectedIndexChanged += (_, _) => ShowSelectedPage();

        var applyRight = clientWidth - Pad;
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(applyRight - ButtonWidth * 3 - ButtonGap * 2, buttonTop, ButtonWidth, ButtonHeight) };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(applyRight - ButtonWidth * 2 - ButtonGap, buttonTop, ButtonWidth, ButtonHeight) };
        // 6 ページの (&x) と衝突しない文字を選ぶ。A は ExternalToolPage の「追加」・QuickAccessPage の
        // 「フォルダを追加」と衝突する。E・I は、キー割り当てページにエクスポート・インポートのボタンを
        // 足す予定があるため、今は使っていなくても避ける
        var applyButton = new Button { Text = "適用(&S)", Bounds = new Rectangle(applyRight - ButtonWidth, buttonTop, ButtonWidth, ButtonHeight) };
        applyButton.Click += (_, _) => TryApply();
        AcceptButton = ok;   // R-102: キー割り当てページは Enter を自分で使うので、そちらが先に拾う
        CancelButton = cancel;
        ShowValidationMessage = message => MessageBox.Show(this, message, "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        // C-1: 6 ページすべてを Controls に入れてから AutoScaleMode を代入する。後から足したページは
        // PerformAutoScale の対象にならず、150% で切れる
        Controls.Add(_sidebar);
        foreach (var page in _pages.Values) Controls.Add(page);
        Controls.AddRange([ok, cancel, applyButton]);

        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16: 座標と大きさは 96 DPI（100%）で書いてある
        AutoScaleMode = AutoScaleMode.Dpi;

        // ListView の列幅は PerformAutoScale では拡大されないため、AutoScaleMode を当てた直後と
        // DpiChanged のたびに決め直す（モニターをまたいで移動したときのため）
        keyAssignPage.ApplyDpi(DeviceDpi);
        quickAccessPage.ApplyDpi(DeviceDpi);
        DpiChanged += (_, _) =>
        {
            keyAssignPage.ApplyDpi(DeviceDpi);
            quickAccessPage.ApplyDpi(DeviceDpi);
        };

        SelectPage(initial);

        FormClosing += (_, e) =>
        {
            if (!RaiseClosing(DialogResult)) e.Cancel = true;
        };

#if DEBUG
        // 開発機の DPI では収まっていても、実機の 150% では切れることがあるための最終確認
        Shown += (_, _) => CheckLayout();
#endif
    }

    /// <inheritdoc/>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (TryHandleCmdKey(keyData)) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// <see cref="ProcessCmdKey"/> の判定本体。ProcessCmdKey 自体は <see cref="Message"/> を引数に取り
    /// ShowDialog なしのテストから呼びにくいので、判定だけ internal に分けてある。
    /// </summary>
    internal bool TryHandleCmdKey(Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Tab)) { CyclePage(1); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.Tab)) { CyclePage(-1); return true; }
        return false;
    }

    private void CyclePage(int delta)
    {
        var count = Sidebar.Length;
        _sidebar.SelectedIndex = (_sidebar.SelectedIndex + delta + count) % count;
    }

    private void SelectPage(SettingsPage page) =>
        _sidebar.SelectedIndex = Array.FindIndex(Sidebar, s => s.Page == page);

    /// <summary>テストが初期ページの選択・ページ巡回・検証失敗時の切り替えを確かめるための入口。</summary>
    internal SettingsPage SelectedPage => Sidebar[_sidebar.SelectedIndex].Page;

    private void ShowSelectedPage()
    {
        var page = Sidebar[_sidebar.SelectedIndex].Page;
        foreach (var (key, control) in _pages) control.Visible = key == page;
    }

    /// <summary>
    /// 検証に失敗したときの知らせ方。既定は MessageBox だが、ShowDialog を使わないテストでは
    /// 表示せずに横取りできるよう、差し替え可能にしてある。プロパティにするとデザイナ用の
    /// 直列化属性を求められる（WFO1000。designer からは使わないので欄で持つ）。
    /// </summary>
    internal Action<string> ShowValidationMessage = _ => { };

    /// <summary>
    /// 外部ツールだけ確定前検証がある（R-102-3）。不備があればそのページを出してメッセージを見せる。
    /// 適用ボタンの処理そのものなので、ShowDialog なしのテストから直接呼べるよう internal にしてある。
    /// </summary>
    internal bool TryApply()
    {
        if (_externalToolPage.Validate() is { } message)
        {
            SelectPage(SettingsPage.ExternalTool);
            ShowValidationMessage(message);
            return false;
        }
        return _apply(_draft);
    }

    /// <summary>
    /// OK・キャンセル・Esc・閉じるボタンいずれで閉じようとしたかは <see cref="DialogResult"/> に出る。
    /// FormClosing の判定本体を internal に分け、ShowDialog を使わないテストから直接呼べるようにする。
    /// OK 以外は確定処理を通さずそのまま閉じてよい。
    /// </summary>
    internal bool RaiseClosing(DialogResult result) => result != DialogResult.OK || TryApply();

#if DEBUG
    private void CheckLayout()
    {
        foreach (var page in _pages.Values)
        {
            if (page.Left < 0 || page.Top < 0 || page.Right > ClientSize.Width || page.Bottom > ClientSize.Height)
                System.Diagnostics.Debug.Fail($"{page.GetType().Name} が設定画面の枠からはみ出しています。");
            CheckChildrenWithin(page);
        }
    }

    private static void CheckChildrenWithin(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child.Left < 0 || child.Top < 0 || child.Right > parent.ClientSize.Width || child.Bottom > parent.ClientSize.Height)
                System.Diagnostics.Debug.Fail($"{parent.GetType().Name} の中の {child.GetType().Name} がはみ出しています。");
            CheckChildrenWithin(child);
        }
    }
#endif
}
