namespace ReTAC.App;

/// <summary>Q9: 上部の行の幅の計算。副作用を持たせずテストする。</summary>
public static class TopRowLayout
{
    /// <summary>アドレスバーの最小幅（96 dpi）。</summary>
    public const int MinAddressWidth = 200;

    public static bool ShouldCompact(int rowWidth, int fullDriveWidth, int minAddressWidth) =>
        rowWidth - fullDriveWidth < minAddressWidth;
}
