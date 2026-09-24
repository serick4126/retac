using System.Drawing;
using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>
/// 統合した設定画面の枠（R-102）。左のサイドバーで 6 ページを切り替え、下端の OK / キャンセル / 適用で
/// 下書き（<see cref="SettingsDraft"/>）をまとめて確定する（R-102-3）。旧来の 6 つの個別ダイアログは
/// 呼び出し側（MainForm、Task 5）の配線が済むまで残す。
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

    // もっとも大きいページ（配色・キー割り当て・外部ツール）が幅 754、キー割り当てが高さ 496（Task 3）。
    // 余白は他のダイアログと合わせて 14px（ExternalToolDialog 等）。
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
        // 6 ページの (&x) と衝突しない文字を選ぶ（既に A は ExternalToolPage の「追加」・QuickAccessPage の
        // 「フォルダを追加」と、E・I は Task 6 で足す KeyAssignPage のエクスポート・インポートと衝突する）
        var applyButton = new Button { Text = "適用(&S)", Bounds = new Rectangle(applyRight - ButtonWidth, buttonTop, ButtonWidth, ButtonHeight) };
        applyButton.Click += (_, _) => TryApply();
        AcceptButton = ok;   // R-102: キー割り当てページは Enter を自分で使うので、そちらが先に拾う
        CancelButton = cancel;

        // C-1: 6 ページすべてを Controls に入れてから AutoScaleMode を代入する。後から足したページは
        // PerformAutoScale の対象にならず、150% で切れる
        Controls.Add(_sidebar);
        foreach (var page in _pages.Values) Controls.Add(page);
        Controls.AddRange([ok, cancel, applyButton]);

        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16: 座標と大きさは 96 DPI（100%）で書いてある
        AutoScaleMode = AutoScaleMode.Dpi;

        // ListView の列幅は PerformAutoScale では拡大されないため、AutoScaleMode を当てた直後と
        // DpiChanged のたびに決め直す（Task 3。モニターをまたいで移動したときのため）
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
            if (DialogResult != DialogResult.OK) return;   // キャンセル・Esc・閉じるボタン: 何もせず閉じる
            if (!TryApply()) e.Cancel = true;
        };

#if DEBUG
        // 実機の 150% で見落とさないための最終確認（実際の検査は Task 8）
        Shown += (_, _) => CheckLayout();
#endif
    }

    /// <inheritdoc/>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Tab)) { CyclePage(1); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.Tab)) { CyclePage(-1); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void CyclePage(int delta)
    {
        var count = Sidebar.Length;
        _sidebar.SelectedIndex = (_sidebar.SelectedIndex + delta + count) % count;
    }

    private void SelectPage(SettingsPage page) =>
        _sidebar.SelectedIndex = Array.FindIndex(Sidebar, s => s.Page == page);

    private void ShowSelectedPage()
    {
        var page = Sidebar[_sidebar.SelectedIndex].Page;
        foreach (var (key, control) in _pages) control.Visible = key == page;
    }

    /// <summary>外部ツールだけ確定前検証がある（§2.3）。不備があればそのページを出してメッセージを見せる。</summary>
    private bool TryApply()
    {
        if (_externalToolPage.Validate() is { } message)
        {
            SelectPage(SettingsPage.ExternalTool);
            MessageBox.Show(this, message, "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        return _apply(_draft);
    }

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
