using System.Runtime.InteropServices;
using System.Windows.Forms;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Tools;

namespace ReTAC.App;

/// <summary>R-145-1: 外部ツールのマクロ構築プロンプトのコピーとエクスポート。メニューと 2 つのダイアログが同じものを使う。</summary>
internal static class MacroBuildPromptActions
{
    public const string CopiedMessage = "マクロ構築プロンプトをコピーしました";
    public const string Guide = "使いたいコマンドと一緒に AI に送ると、設定を組み立ててくれます";

    private static string Version => Application.ProductVersion.Split('+')[0];

    /// <summary>設定画面の開き方。メニューの文言を Domain に二重に持たないよう、表示名の表から作る</summary>
    private static string SettingsMenuPath => $"「設定」メニューの「{CommandLabels.Of(CommandId.ExternalToolSettings)}」";

    private static string Generate() => MacroBuildPrompt.Generate(DateTime.Now, Version, SettingsMenuPath);

    /// <returns>コピーできたら true。呼び出し側が場所に合わせて知らせる</returns>
    public static bool Copy(IWin32Window owner)
    {
        try
        {
            Clipboard.SetText(Generate());
            return true;
        }
        catch (ExternalException)
        {
            // 他のプロセスが掴んでいると投げる。MainForm.TryClipboard と同じ文で知らせる
            MessageBox.Show(owner, "クリップボードを他のアプリが使用中です。少し待ってからやり直してください。",
                "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    public static void Export(IWin32Window owner)
    {
        using var dialog = new SaveFileDialog
        {
            FileName = $"ReTAC_外部ツールのマクロ構築プロンプト_{Version}.md",
            Filter = "Markdown ファイル (*.md)|*.md",
            DefaultExt = "md",
        };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;

        try
        {
            SafeFileWrite.WriteReplacing(dialog.FileName, Generate());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(owner, "書き出せませんでした。", "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>ダイアログのボタンで、メニューと同じ 2 項目を出す。</summary>
    public static ToolStripItem[] MenuItems(Action export, Action copy) =>
    [
        new ToolStripMenuItem("マークダウン形式でエクスポート(&E)...", null, (_, _) => export()),
        new ToolStripMenuItem("クリップボードにコピー(&C)", null, (_, _) => copy()),
    ];
}
