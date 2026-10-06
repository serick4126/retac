namespace ReTAC.Domain.Startup;

/// <summary>R-26-2: 起動引数のパスの種類。</summary>
public enum StartupPathKind { None, Folder, File, Invalid }

/// <summary>R-26-2: 分類の結果。8.3 で動いている側へ渡すときも、この結果をそのまま送る（受けた側は確かめ直さない）。</summary>
public sealed record StartupPathResult(StartupPathKind Kind, string? Folder, string? FileName, string Raw);

/// <summary>R-26-2: 起動引数のパスを整え、フォルダかファイルかを分ける（副作用なし。存在の確認は注入）。</summary>
public static class StartupPath
{
    /// <summary>
    /// 整え方だけを行う（ファイルシステムに触らない）。空なら null。
    /// 前後の空白 → 前後の " → ドライブ名だけなら "C:\" → 相対なら workingDirectory から絶対。
    /// </summary>
    public static string? Normalize(string? raw, string workingDirectory)
    {
        // 前後の " を取る。"C:\" は Windows の引数の規則で C:" と末尾に残って渡される（外部ツールのマクロで出やすい）。
        // 先頭の " は普通シェルが外すが、"D: b" のように残っていたら外す
        var path = raw?.Trim().Trim('"').Trim();
        if (string.IsNullOrEmpty(path)) return null;
        // C: のままだと、そのドライブの「現在のフォルダ」の意味になる
        if (path.Length == 2 && char.IsAsciiLetter(path[0]) && path[1] == ':') path += '\\';
        try { return Path.GetFullPath(path, workingDirectory); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;   // 形が不正。存在しないので Classify が Invalid にする
        }
    }

    /// <summary>フォルダなら Folder = パス、ファイルなら Folder = 親・FileName = 名前。どちらでもなければ Invalid。</summary>
    public static StartupPathResult Classify(string raw, string? normalized,
        Func<string, bool> directoryExists, Func<string, bool> fileExists)
    {
        if (normalized is null) return new(StartupPathKind.None, null, null, raw);
        if (directoryExists(normalized)) return new(StartupPathKind.Folder, normalized, null, raw);
        if (fileExists(normalized) && Path.GetDirectoryName(normalized) is { Length: > 0 } parent)
            return new(StartupPathKind.File, parent, Path.GetFileName(normalized), raw);
        return new(StartupPathKind.Invalid, null, null, raw);
    }
}
