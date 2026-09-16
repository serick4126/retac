using System.Globalization;

namespace ReTAC.Domain.Formatting;

/// <summary>ステータスバーの表示書式。5-1 節「ファイル日付は西暦 4 桁」・R-69「単位付き表示に固定」。</summary>
public static class Display
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>
    /// R-69: バイト表示は実装せず単位付きに固定する。
    /// 卓駆の実測では、ファイルサイズが小数 1 桁（<c>535.1KB</c>）、
    /// ドライブ容量が小数 2 桁（<c>3725.90GB</c>）で表示されている。
    /// </summary>
    public static string Size(long bytes, int decimals = 1)
    {
        if (bytes < 0) return "";
        if (bytes < 1024) return $"{bytes}B";

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return value.ToString("F" + decimals, CultureInfo.InvariantCulture) + Units[unit];
    }

    /// <summary>5-1 節: ファイル日付の西暦は 4 桁で表示する。</summary>
    public static string Timestamp(DateTime value) =>
        value == default ? "" : value.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>ドライブ容量の区画（R-34 ①）。</summary>
    public static string DriveCapacity(string driveLetter, long total, long free)
    {
        if (total <= 0) return $"{driveLetter}:";
        var usedPercent = (int)Math.Round((total - free) * 100.0 / total);
        return $"{driveLetter}: 全{Size(total, 2)} 空{Size(free, 2)} Use:{usedPercent}%";
    }
}
