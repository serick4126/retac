using System.IO;

namespace ReTAC.Domain.Navigation;

/// <summary>R-94: パンくずの 1 段。Name は表示の名前、Path はクリックでジャンプする先。</summary>
public sealed record BreadcrumbSegment(string Name, string Path);

/// <summary>R-94: アドレスバーのパンくず表示の、段への分け方と、入りきらないときの畳み方。</summary>
public static class Breadcrumb
{
    /// <summary>
    /// パスを段に分ける。先頭はドライブ（名前 `C:`、パス `C:\`）か UNC の共有（`\\server\share`）で 1 段。
    /// 末尾の `\` は無視する。ルートの無いパスは全体を 1 段にする。
    /// </summary>
    public static IReadOnlyList<BreadcrumbSegment> Split(string folder)
    {
        if (string.IsNullOrEmpty(folder)) return [];
        var root = System.IO.Path.GetPathRoot(folder);
        if (string.IsNullOrEmpty(root)) return [new BreadcrumbSegment(folder, folder)];

        // ドライブのルートは `C:\` のまま渡す（`C:` だとそのドライブのカレントフォルダを指してしまう）
        var segments = new List<BreadcrumbSegment> { new(root.TrimEnd('\\', '/'), root) };
        var path = root;
        foreach (var part in folder[root.Length..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            path = System.IO.Path.Combine(path, part);
            segments.Add(new BreadcrumbSegment(part, path));
        }
        return segments;
    }

    /// <summary>
    /// 表示する先頭の段の添字。入りきらなければ先頭側から畳み、畳んだら `…` の幅も足して数える。<b>最後の段は必ず残す</b>
    /// （最後の段も入りきらないときは最後の段を返す。描く側がその幅を available に切り詰める）。
    /// </summary>
    /// <param name="widths">段ごとの幅。段の名前と、その直後の `▸` を含む</param>
    /// <param name="ellipsisWidth">`…` の幅（その直後の区切りを含む）</param>
    /// <param name="available">パンくずに使える幅。先頭のアイコン・左右の余白・枠を除いたもの。0 以下もあり得る</param>
    public static int FirstShown(IReadOnlyList<int> widths, int ellipsisWidth, int available)
    {
        if (widths.Count == 0) return 0;
        var rest = 0;
        for (var i = widths.Count - 1; i >= 0; i--) rest += widths[i];
        if (rest <= available) return 0;

        // 先頭から 1 段ずつ畳む。rest は i 段目から最後までの幅
        for (var i = 0; i < widths.Count - 1; i++)
        {
            rest -= widths[i];
            if (rest + ellipsisWidth <= available) return i + 1;
        }
        return widths.Count - 1;
    }
}
