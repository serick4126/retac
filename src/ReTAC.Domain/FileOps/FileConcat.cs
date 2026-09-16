using System.IO;

namespace ReTAC.Domain.FileOps;

/// <summary>
/// ファイルの連結（0x82E4）。
/// R-35: 連結順は利用者が並べ替えられること。順序の指定ができなければ機能として成立しない。
/// R-35-2: オプションは「EOF をカットする」（既定オン）と「末尾に改行コードをつける」の 2 つ。
/// </summary>
public static class FileConcat
{
    /// <summary>MS-DOS 由来のファイル末尾記号（Ctrl+Z）。</summary>
    public const byte EofMark = 0x1A;

    /// <param name="sources">連結する順に並んでいること</param>
    /// <returns>書き出した総バイト数</returns>
    public static long Concat(IReadOnlyList<string> sources, string destination, bool cutEof, bool appendNewLine)
    {
        // FileMode.Create は先に切り詰めるので、宛先が連結元に混ざっていると中身が消える
        // （既定の宛先は カレント\concat.txt。それをマークしたまま再実行すると起きる）
        var destinationFull = Path.GetFullPath(destination);
        foreach (var source in sources)
            if (string.Equals(Path.GetFullPath(source), destinationFull, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"連結先が連結元に含まれています: {Path.GetFileName(destination)}");

        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write);

        foreach (var source in sources)
        {
            var bytes = File.ReadAllBytes(source);
            var length = bytes.Length;

            // EOF のカットは末尾の 0x1A だけを落とす。中身の 0x1A には触らない
            if (cutEof && length > 0 && bytes[length - 1] == EofMark) length--;

            output.Write(bytes, 0, length);
        }

        if (appendNewLine) output.Write("\r\n"u8);
        return output.Length;
    }
}
