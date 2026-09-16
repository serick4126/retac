namespace ReTAC.Domain.FileOps;

/// <summary>
/// ファイル名・フォルダ名として使えるかの検査。R-48 の「誤りは中止せず入力欄に戻す」ための文言を返す。
/// 名前の変更（`N`）・フォルダ作成（`K`）・名前を変えて複写（R-63）で共用する。
/// </summary>
public static class FileNameRules
{
    // 拡張子を付けても予約されたまま（CON.txt も作れない）
    private static readonly string[] Reserved =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <returns>使えない理由。使えるなら null</returns>
    public static string? Validate(string name)
    {
        if (name.Length == 0) return "名前を入力してください。";

        if (name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            return @"名前に \ / : * ? "" < > | は使えません。";

        // 末尾の . と空白は OS が黙って落とす。意図しない名前になるので断る
        if (name.EndsWith('.') || name.EndsWith(' ')) return "名前の末尾に . と空白は使えません。";

        var stem = name.Split('.')[0].TrimEnd();
        return Reserved.Contains(stem, StringComparer.OrdinalIgnoreCase)
            ? $"{stem} は Windows が予約している名前のため使えません。"
            : null;
    }
}
