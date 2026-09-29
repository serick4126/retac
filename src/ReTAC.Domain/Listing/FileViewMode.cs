namespace ReTAC.Domain.Listing;

/// <summary>
/// R-112-4 / V9: 表示モード。並びは Ctrl+ホイールの段の順（上から）。名前は設定ファイルに載るので変えない。
/// </summary>
public enum FileViewMode { ExtraLargeIcons, LargeIcons, MediumIcons, SmallIcons, List, Details, Tiles, Content }

/// <summary>R-112-4: 今の表示モードはアプリ全体で 1 つ（INV-VIEWMODE-APP-WIDE）。</summary>
public static class FileViewModes
{
    /// <summary>
    /// Q29: 作ったビューから順に切り替えの段へ足す。3.0.0 では 8 つすべて。
    /// 並びは列挙の順（Ctrl+ホイールの段の順）を保つ。
    /// </summary>
    public static readonly IReadOnlyList<FileViewMode> Built = [FileViewMode.List, FileViewMode.Details];

    /// <summary>知らない値・まだ作っていないモードは一覧に直す（手で書いた設定ファイルでだけ起こる）。</summary>
    public static FileViewMode Normalize(FileViewMode mode) => Built.Contains(mode) ? mode : FileViewMode.List;

    /// <param name="notches">正なら上の段（大きいアイコンの側）へ。WheelAccumulator.Add の戻り値と同じ符号</param>
    /// <returns>両端で止まる（折り返さない。V9）</returns>
    public static FileViewMode Step(FileViewMode current, int notches)
    {
        var index = Built.ToList().IndexOf(Normalize(current));
        return Built[Math.Clamp(index - notches, 0, Built.Count - 1)];
    }
}
