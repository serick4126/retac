using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>R-146: 名前・パスをクリップボードへ入れ、入れられたらステータスバーで知らせる。形式・件数にかかわらず同じ文。</summary>
internal static class ClipboardNotice
{
    public const string CopiedMessage = "クリップボードにコピーしました";

    /// <param name="tryClipboard">MainForm.TryClipboard。他のアプリが使用中なら警告を出して false</param>
    public static void CopyText(string text, Func<Action, bool> tryClipboard, Action<string> notify)
    {
        if (text.Length == 0) return;
        if (tryClipboard(() => Clipboard.SetText(text))) notify(CopiedMessage);
    }
}
