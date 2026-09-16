using ReTAC.Domain.Formatting;
using ReTAC.Domain.Selection;

namespace ReTAC.Domain.Tests;

/// <summary>ステータスバーの内容と書式（開発プラン T3-3 / T3-5）</summary>
public class DisplayTests
{
    [Theory]
    [InlineData(0, "0B")]
    [InlineData(1023, "1023B")]
    [InlineData(1024, "1.0KB")]
    [InlineData(1331, "1.3KB")]
    [InlineData(548045, "535.2KB")]
    [InlineData(1024L * 1024 * 1024, "1.0GB")]
    public void サイズは単位付きで表示する(long bytes, string expected)
    {
        // R-69: バイト表示は実装せず単位付きに固定する
        Assert.Equal(expected, Display.Size(bytes));
    }

    [Fact]
    public void ドライブ容量は小数2桁で表示する()
    {
        // 卓駆の実測表示: D:全3725.90GB 空75.77GB Use:98%
        var text = Display.DriveCapacity("D", 4_000_000_000_000L, 81_000_000_000L);
        Assert.StartsWith("D: 全3", text);
        Assert.Contains("GB", text);
        Assert.Contains("Use:98%", text);
    }

    [Fact]
    public void 日付は西暦4桁で表示する()
    {
        // 5-1 節「ファイル日付の西暦は 4 桁で表示」
        Assert.Equal("1997/10/14 20:32:50", Display.Timestamp(new DateTime(1997, 10, 14, 20, 32, 50)));
    }

    [Fact]
    public void 日時が未取得なら空文字を返す()
    {
        // 6 章: 取得できなかった情報は空欄として表示する
        Assert.Equal("", Display.Timestamp(default));
    }
}

public class ListSummaryTests
{
    private static ListState MakeState() => new(
    [
        TestEntries.Parent(),
        TestEntries.Folder("sub"),
        TestEntries.Folder("other"),
        TestEntries.File("a.txt", size: 100),
        TestEntries.File("b.txt", size: 200),
        TestEntries.File("c.log", size: 300),
    ]);

    [Fact]
    public void マークが0件ならカレントフォルダ全体を数える()
    {
        // 実機: マークなしなら ②③ ともアイコンはそのままで総数を表示
        var summary = ListSummary.Of(MakeState());
        Assert.Equal(2, summary.FolderCount);
        Assert.False(summary.FolderFromMarks);
        Assert.Equal(3, summary.FileCount);
        Assert.Equal(600, summary.TotalSize);
        Assert.False(summary.FileFromMarks);
    }

    [Fact]
    public void 親フォルダ項目は件数に数えない()
    {
        // R-04
        var summary = ListSummary.Of(MakeState());
        Assert.Equal(5, summary.FolderCount + summary.FileCount);   // 6 エントリ中 .. を除く
    }

    [Fact]
    public void ファイルだけマークすると3の区画だけが切り替わる()
    {
        // 実機: ファイルアイコンのみ★になり星付きのみカウント。
        // ② はフォルダアイコンのままカレントフォルダの総数を表示する
        var state = MakeState();
        state.ToggleMark(4);   // b.txt
        var summary = ListSummary.Of(state);

        Assert.Equal(2, summary.FolderCount);      // マーク数 0 ではなく総数
        Assert.False(summary.FolderFromMarks);     // フォルダアイコンのまま
        Assert.Equal(1, summary.FileCount);
        Assert.Equal(200, summary.TotalSize);
        Assert.True(summary.FileFromMarks);        // ファイル側だけ★
    }

    [Fact]
    public void フォルダをマークすると2と3の両方が切り替わる()
    {
        // 実機: フォルダとファイル両方のアイコンに★がつき、それぞれ選択中の数をカウントする
        var state = MakeState();
        state.ToggleMark(1);   // sub
        state.ToggleMark(4);   // b.txt
        var summary = ListSummary.Of(state);

        Assert.Equal(1, summary.FolderCount);
        Assert.True(summary.FolderFromMarks);
        Assert.Equal(1, summary.FileCount);
        Assert.Equal(200, summary.TotalSize);
        Assert.True(summary.FileFromMarks);
    }

    [Fact]
    public void フォルダだけマークするとファイル側は0件の星付きになる()
    {
        var state = MakeState();
        state.ToggleMark(1);
        state.ToggleMark(2);
        var summary = ListSummary.Of(state);

        Assert.Equal(2, summary.FolderCount);
        Assert.True(summary.FolderFromMarks);
        Assert.Equal(0, summary.FileCount);
        Assert.Equal(0, summary.TotalSize);        // フォルダはサイズに数えない
        Assert.True(summary.FileFromMarks);
    }
}
