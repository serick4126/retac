using System.IO;

namespace ReTAC.Domain.FileOps;

/// <summary>
/// ファイルシステムを辿る／転送するときに、<b>触ってはいけない対象を外すための判断</b>。
///
/// この 2 つは同じ判断が複数の経路で要る。散らすと片方だけ直して片方が残るので
/// （実際にそうなっていた）、ここ 1 か所に置いて全経路から呼ぶ。
/// </summary>
public static class TransferGuards
{
    /// <summary>
    /// このフォルダの中へ再帰してよいか。<b>ジャンクション／シンボリックリンクは辿らない。</b>
    ///
    /// 上位を指すリンクがあると再帰が終わらず、最後は catch できない
    /// <see cref="StackOverflowException"/> でプロセスごと落ちる。
    /// その手前で、利用者が選んでいないフォルダ配下のファイルにまで触ってしまう。
    /// </summary>
    public static bool CanDescend(string folder) =>
        !File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint);

    /// <summary>
    /// <paramref name="destination"/> が <paramref name="source"/> そのもの、またはその配下か。
    ///
    /// 転送先が転送元の中にあると、作った転送先をさらに転送元として辿ることになり、
    /// 移動では後始末（空フォルダの削除）が転送先まで巻き込む。
    /// コピー・移動・ドロップ・貼り付けのすべてで外す。
    /// </summary>
    public static bool IsInsideOrSame(string destination, string source)
    {
        if (string.IsNullOrEmpty(destination) || string.IsNullOrEmpty(source)) return false;

        var outer = Path.TrimEndingDirectorySeparator(source);
        var inner = Path.TrimEndingDirectorySeparator(destination);
        return string.Equals(outer, inner, StringComparison.OrdinalIgnoreCase)
            // 名前が前方一致するだけの別フォルダ（C:\work と C:\work2）を巻き込まないよう
            // 区切り文字まで見る
            || inner.StartsWith(outer + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
