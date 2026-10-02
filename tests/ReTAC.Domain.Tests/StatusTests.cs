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

public class StatusBarCursorTests
{
    [Fact]
    public void ステータスバーの項目の情報は更新日時_種別_サイズの順で名前は別に返す()
    {
        var mtime = new DateTime(2026, 10, 2, 9, 8, 7);
        var (name, detail) = ReTAC.App.StatusBar.DescribeCursor(TestEntries.File("資料.xlsx", size: 512, mtime: mtime));
        Assert.Equal("資料.xlsx", name);
        Assert.DoesNotContain("資料", detail);
        Assert.StartsWith(Display.Timestamp(mtime), detail);
        Assert.EndsWith("  " + Display.Size(512), detail);   // サイズは種別の後
    }

    [Fact]
    public void ステータスバーのフォルダの情報はサイズを含まない()
    {
        var mtime = new DateTime(2026, 10, 2, 9, 8, 7);
        var (name, detail) = ReTAC.App.StatusBar.DescribeCursor(TestEntries.Folder("資料", mtime: mtime));
        Assert.Equal("資料", name);
        Assert.StartsWith(Display.Timestamp(mtime), detail);
        Assert.DoesNotContain(Display.Size(0), detail);
    }

    [Fact]
    public void ステータスバーはnullと親フォルダの項目で何も出さない()
    {
        Assert.Equal(("", ""), ReTAC.App.StatusBar.DescribeCursor(null));
        Assert.Equal(("", ""), ReTAC.App.StatusBar.DescribeCursor(TestEntries.Parent()));
    }

    // 名前は x=300 から幅 100。pad は 6。キューの区画が無いときの queueLeft は int.MaxValue（OnPaint と同じ）
    [Theory]
    [InlineData(1000, int.MaxValue, 100)]   // 名前まで収まる
    [InlineData(356, int.MaxValue, 50)]     // 日時・種別・サイズは収まり、名前だけ縮める
    [InlineData(280, int.MaxValue, 0)]      // 日時・種別・サイズも収まらない。名前は出さない
    [InlineData(1000, 356, 50)]             // キューを出している間は、その手前が右端
    [InlineData(1000, 280, 0)]
    public void ステータスバーの名前の幅は区画の右端までに縮める(int controlWidth, int queueLeft, int expected)
    {
        Assert.Equal(expected, ReTAC.App.StatusBar.CursorNameWidth(300, 100, controlWidth, queueLeft, 6));
    }
}
