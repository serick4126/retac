using System.Runtime.InteropServices;
using System.Text;
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
            WriteReplacing(dialog.FileName, Generate());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(owner, "書き出せませんでした。", "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// 一時ファイル経由で置き換える（V-07 と同じ理由。途中で失敗しても前のファイルを壊さない）。
    /// 一時ファイルは保存先と同じフォルダに一意な名前で新しく作り、自分が作ったものだけを片付ける。
    /// 「保存先.tmp」のような決まった名前だと、利用者の同名のファイルを黙って上書きし、失敗時には消してしまう。
    /// </summary>
    internal static void WriteReplacing(string path, string text)
    {
        var full = Path.GetFullPath(path);
        var temp = Path.Combine(Path.GetDirectoryName(full)!, $"{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        var created = false;
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(text);
            }
            File.Move(temp, full, overwrite: true);
            created = false;
        }
        finally
        {
            if (created)
            {
                try { File.Delete(temp); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>ダイアログのボタンで、メニューと同じ 2 項目を出す。</summary>
    public static ToolStripItem[] MenuItems(Action export, Action copy) =>
    [
        new ToolStripMenuItem("マークダウン形式でエクスポート(&E)...", null, (_, _) => export()),
        new ToolStripMenuItem("クリップボードにコピー(&C)", null, (_, _) => copy()),
    ];
}
