using System.Runtime.InteropServices;

namespace ReTAC.Domain.Listing;

/// <summary>R-05-3: 名前・拡張子の比較は大文字小文字を区別しない。</summary>
public static class NameComparers
{
    public static IComparer<string> For(ComparisonMode mode) =>
        mode == ComparisonMode.Natural ? Natural : Strict;

    /// <summary>名前の昇順。数字は文字列として比べる（<c>file10</c> → <c>file2</c>）。</summary>
    public static readonly IComparer<string> Strict = new ShellComparer(StrCmpW);

    /// <summary>R-05-2: 自然な昇順。連続する数字を数値として比べる（<c>file2</c> → <c>file10</c>）。</summary>
    public static readonly IComparer<string> Natural = new ShellComparer(StrCmpLogicalW);

    // B-02: エクスプローラの並びは shlwapi のこの 2 関数そのものである。
    // 2026-09-11 に利用者の記録した順序と突き合わせて実測で確認した。
    // 自前の比較では日本語の照合表を再現できない。
    //
    // ReTAC.Domain は net10.0（プラットフォーム非依存）を対象にしているが、
    // ReTAC は Windows アプリであり、テストも Windows 上でしか動かない。
    // TFM は変えず、ここだけ Win32 に依存させる。
    //
    // 探索先を System32 に固定する。配布物は自己完結の単一ファイルで、利用者は任意のフォルダに
    // 置く。既定の探索順は実行ファイルのあるフォルダが先なので、そこに置かれた shlwapi.dll を
    // 読んでしまう余地を残さない。
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string x, string y);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpW(string x, string y);

    private sealed class ShellComparer(Func<string, string, int> compare) : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x is null || y is null) return string.CompareOrdinal(x, y);

            var c = compare(x, y);
            if (c != 0) return c;

            // StrCmpLogicalW は大文字小文字を区別せず同値（0）を返す。ここで序数比較に落ちると
            // 'A'(0x41) が 'a'(0x61) より前に来て「名前の昇順」の実測結果と食い違うため、
            // 先に StrCmpW（Windows 自身の大文字小文字順）に訊く。数字だけの同値はここでも
            // 大小差が出ないので、その場合のみ最後に序数比較で総順序を保つ。
            c = StrCmpW(x, y);
            return c != 0 ? c : string.CompareOrdinal(x, y);
        }
    }
}
