using System.Windows.Forms;
using ReTAC.App;
using ReTAC.App.Rendering;
using ReTAC.Domain.Navigation;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-102-3: 統合設定画面の OK・適用・キャンセル・ページ切り替えを、ShowDialog を使わずに確かめる。
/// SettingsDialog がテスト用に公開している internal な入口（<see cref="SettingsDialog.RaiseClosing"/>・
/// <see cref="SettingsDialog.TryApply"/>・<see cref="SettingsDialog.TryHandleCmdKey"/>・
/// <see cref="SettingsDialog.SelectedPage"/>・<see cref="SettingsDialog.ShowValidationMessage"/>）を使う。
/// </summary>
public class SettingsDialogBehaviorTests
{
    /// <summary>apply デリゲートが呼ばれた回数。テストのラムダから書き換えられるよう参照型にしてある。</summary>
    private sealed class Counter { public int Count; }

    private static SettingsDialog CreateDialog(SettingsPage initial, Counter apply)
    {
        var settings = new AppSettings();
        var draft = SettingsDraft.From(settings, settings.ToKeyMap(), Theme.Default, new QuickAccessList());
        var dialog = new SettingsDialog(draft, initial, @"C:\", _ => { apply.Count++; return true; });
        dialog.ShowValidationMessage = _ => { };   // メッセージボックスはテストでは出さない
        return dialog;
    }

    [Fact]
    public void OKでapplyが1回呼ばれて閉じる()
    {
        var apply = new Counter();
        using var dialog = CreateDialog(SettingsPage.Environment, apply);

        var canClose = dialog.RaiseClosing(DialogResult.OK);

        Assert.True(canClose);
        Assert.Equal(1, apply.Count);
    }

    [Fact]
    public void 適用でapplyが1回呼ばれる()
    {
        var apply = new Counter();
        using var dialog = CreateDialog(SettingsPage.Environment, apply);

        var ok = dialog.TryApply();   // 適用ボタンの Click ハンドラーと同じ処理

        Assert.True(ok);
        Assert.Equal(1, apply.Count);
    }

    [Theory]
    [InlineData(DialogResult.Cancel)]  // キャンセルボタン・Esc（CancelButton の DialogResult）
    [InlineData(DialogResult.None)]    // 閉じるボタン（CancelButton を経由しない既定値）
    public void キャンセルEsc閉じるボタンではapplyが呼ばれない(DialogResult result)
    {
        var apply = new Counter();
        using var dialog = CreateDialog(SettingsPage.Environment, apply);

        var canClose = dialog.RaiseClosing(result);

        Assert.True(canClose);
        Assert.Equal(0, apply.Count);
    }

    [Fact]
    public void 外部ツールの検証に失敗すると外部ツールのページが選ばれapplyは呼ばれない()
    {
        var settings = new AppSettings();
        var draft = SettingsDraft.From(settings, settings.ToKeyMap(), Theme.Default, new QuickAccessList());
        draft.ExternalTools = [new ExternalTool { Id = 1, Name = "" }];   // 名前なし: 検証に失敗する
        var apply = new Counter();
        using var dialog = new SettingsDialog(draft, SettingsPage.Environment, @"C:\", _ => { apply.Count++; return true; });
        string? shown = null;
        dialog.ShowValidationMessage = message => shown = message;

        var ok = dialog.TryApply();

        Assert.False(ok);
        Assert.Equal(0, apply.Count);
        Assert.Equal(SettingsPage.ExternalTool, dialog.SelectedPage);
        Assert.NotNull(shown);
    }

    [Fact]
    public void CtrlTabとCtrlShiftTabでページが巡回する()
    {
        var apply = new Counter();
        using var dialog = CreateDialog(SettingsPage.Environment, apply);

        Assert.True(dialog.TryHandleCmdKey(System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Tab));
        Assert.Equal(SettingsPage.ColorFont, dialog.SelectedPage);

        Assert.True(dialog.TryHandleCmdKey(System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Shift | System.Windows.Forms.Keys.Tab));
        Assert.Equal(SettingsPage.Environment, dialog.SelectedPage);
    }

    [Theory]
    [InlineData(SettingsPage.Environment)]
    [InlineData(SettingsPage.KeyAssign)]
    [InlineData(SettingsPage.QuickAccess)]
    public void 指定した初期ページが選ばれている(SettingsPage page)
    {
        var apply = new Counter();
        using var dialog = CreateDialog(page, apply);

        Assert.Equal(page, dialog.SelectedPage);
    }
}
