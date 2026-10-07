using System.Text;

namespace ReTAC.App;

/// <summary>
/// V-07: ファイルを一時ファイル経由で置き換える。直接上書きすると、書き込み中に落ちたときに中身が壊れる。
/// 設定の保存・キー割り当てのエクスポート・マクロ構築プロンプトのエクスポートが同じものを使う。
/// </summary>
internal static class SafeFileWrite
{
    /// <summary>
    /// 一時ファイルは保存先と同じフォルダに一意な名前で新しく作り、自分が作ったものだけを片付ける。
    /// 「保存先.tmp」のような決まった名前だと、利用者の同名のファイルを黙って上書きし、失敗時には消してしまう。
    /// 名前は保存先の名前に依存させない。保存先の名前に足すと、有効な長い名前（233 文字など）でも
    /// 1 つの名前の上限（255 文字）を超えて失敗する。文字コードは BOM なしの UTF-8。
    /// </summary>
    /// <param name="move">一時ファイルを保存先へ差し替える処理。テストで失敗を起こすための差し替え口</param>
    internal static void WriteReplacing(string path, string text, Action<string, string>? move = null)
    {
        var full = Path.GetFullPath(path);
        var temp = Path.Combine(Path.GetDirectoryName(full)!, $"retac-{Guid.NewGuid():N}.tmp");
        var created = false;
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(text);
            }
            (move ?? ((from, to) => File.Move(from, to, overwrite: true)))(temp, full);
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
}
