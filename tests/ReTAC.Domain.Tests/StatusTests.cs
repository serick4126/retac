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
    public void Size_RoundsUpToNextUnit()
    {
        // R-69-2: 丸めて 1024 になったら次の単位で出す
        Assert.Equal("1023B", Display.Size(1023));
        Assert.Equal("1.0KB", Display.Size(1024));
        Assert.Equal("1.0MB", Display.Size(1_048_570));
        Assert.Equal("1.0MB", Display.Size(1_048_575));
        Assert.Equal("1.00GB", Display.Size(1024L * 1024 * 1024 - 1, 2));
    }

    [Fact]
    public void Size_PbBoundaryGoesToEb()
    {
        var pb = 1024L * 1024 * 1024 * 1024 * 1024;
        Assert.Equal("1.0EB", Display.Size((long)(pb * 1023.0 + pb * 0.96)));
        Assert.Equal("1.0EB", Display.Size(pb * 1024));
    }

    [Fact]
    public void Size_LongMaxIsEb()
    {
        Assert.Equal("8.0EB", Display.Size(long.MaxValue));
        Assert.Equal("8.00EB", Display.Size(long.MaxValue, 2));
    }

    [Fact]
    public void Size_IntegerPartBounded()
    {
        // R-69-2: 整数部は B〜PB で 1023 以下、EB で 8 以下
        long[] samples = [0, 1, 1023, 1024, 1_048_570, 1_048_575, 1L << 30, (1L << 30) - 1, 1L << 40, (1L << 40) - 1,
            1L << 50, (1L << 50) - 1, (1L << 50) * 1023, (1L << 50) * 1024 - 1, 1L << 60, long.MaxValue];
        foreach (var bytes in samples)
        foreach (var decimals in new[] { 1, 2 })
        {
            var text = Display.Size(bytes, decimals);
            var unitAt = text.IndexOfAny(['K', 'M', 'G', 'T', 'P', 'E']);
            var unit = unitAt < 0 ? "B" : text[unitAt..];
            var integer = int.Parse(text[..(text.Length - unit.Length)].Split('.')[0]);
            Assert.True(integer <= (unit == "EB" ? 8 : 1023), $"{bytes} -> {text}");
        }
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
    public void ドライブバーの説明は名前と合計_使用_空きの4行()
    {
        // R-32-4: 合計 7452.02GB・空き 2112.21GB のドライブ。1024GB 以上は TB で出る（R-69-2）
        var gb = 1024.0 * 1024 * 1024;
        Assert.Equal("ローカル ディスク (K:)\n合計: 7.28TB\n使用: 5.21TB\n空き: 2.06TB",
            Display.DriveTooltip("ローカル ディスク (K:)", (long)(7452.02 * gb), (long)(2112.21 * gb)));
    }

    [Fact]
    public void ドライブバーの説明は容量が取れなければ名前だけ()
    {
        // R-32-4: メディアの無いドライブなど
        Assert.Equal("DVD ドライブ (E:)", Display.DriveTooltip("DVD ドライブ (E:)", 0, 0));
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
    public void Cursor_OrderIsDateSizeTypeName()
    {
        // R-34: 更新日時・サイズ・種別・名前を別々に返す
        var mtime = new DateTime(2026, 10, 2, 9, 8, 7);
        var file = ReTAC.App.StatusBar.DescribeCursor(TestEntries.File("資料.xlsx", size: 512, mtime: mtime));
        Assert.Equal(Display.Timestamp(mtime), file.Date);
        Assert.Equal("512B", file.Size);
        Assert.NotEqual("", file.Type);
        Assert.Equal("資料.xlsx", file.Name);

        var folder = ReTAC.App.StatusBar.DescribeCursor(TestEntries.Folder("資料", mtime: mtime));
        Assert.Equal(Display.Timestamp(mtime), folder.Date);
        Assert.Equal("", folder.Size);
        Assert.NotEqual("", folder.Type);
        Assert.Equal("資料", folder.Name);

        Assert.Equal(("", "", "", ""), ReTAC.App.StatusBar.DescribeCursor(null));
        Assert.Equal(("", "", "", ""), ReTAC.App.StatusBar.DescribeCursor(TestEntries.Parent()));
    }

    [Fact]
    public void SizeColumn_FitsWidest()
    {
        var width = ReTAC.App.StatusBar.SizeColumnWidth(s => s.Length);
        Assert.True(width >= "1023.9PB".Length);
        Assert.True(width >= Display.Size(long.MaxValue).Length);
    }

    [Fact]
    public void CursorLayout_SizeRightAligned()
    {
        // R-34: サイズ欄の幅が同じなら、サイズの文字列によらず右端も種別の始まりも同じ（フォルダも同じ）
        var a = ReTAC.App.StatusBar.CursorLayout(10, 120, 60, 12, 80);
        var b = ReTAC.App.StatusBar.CursorLayout(10, 120, 60, 12, 80);
        Assert.Equal(a, b);
        Assert.Equal(10 + 120 + 12 + 60, a.SizeRight);
        Assert.Equal(a.SizeRight + 12, a.TypeX);
        Assert.Equal(a.TypeX + 80 + 12, a.NameX);
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
