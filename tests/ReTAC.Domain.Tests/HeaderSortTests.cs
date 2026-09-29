using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Tests;

/// <summary>R-114 / Q23 / Q34: 見出しのクリックとソートの印。</summary>
public class HeaderSortTests
{
    private static SortOrder Order(SortKey key, SortDirection direction) => new(key, direction, ComparisonMode.Strict);

    public static TheoryData<SortKey, SortDirection, DetailsColumn?, SortKey?, SortDirection?> Clicks()
    {
        var data = new TheoryData<SortKey, SortDirection, DetailsColumn?, SortKey?, SortDirection?>();
        foreach (var key in Enum.GetValues<SortKey>())
            foreach (var direction in Enum.GetValues<SortDirection>())
                foreach (var column in new DetailsColumn?[] { null, DetailsColumn.Extension, DetailsColumn.Size, DetailsColumn.Modified,
                             DetailsColumn.Created, DetailsColumn.Type, DetailsColumn.Attributes })
                {
                    SortKey? target = column switch
                    {
                        null => SortKey.Name,
                        DetailsColumn.Extension or DetailsColumn.Type => SortKey.Extension,
                        DetailsColumn.Size => SortKey.Size,
                        DetailsColumn.Modified => SortKey.Date,
                        _ => null,
                    };
                    var flipped = direction == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending;
                    data.Add(key, direction, column, target, target is null ? null : target == key ? flipped : direction);
                }
        return data;
    }

    [Theory]
    [MemberData(nameof(Clicks))]
    public void 違う列はキーだけ変え同じキーは向きを反転し作成日時と属性は何もしない(
        SortKey key, SortDirection direction, DetailsColumn? column, SortKey? expectedKey, SortDirection? expectedDirection)
    {
        var result = HeaderSort.Click(Order(key, direction), column);
        Assert.Equal(expectedKey, result?.Key);
        Assert.Equal(expectedDirection, result?.Direction);
        if (result is not null) Assert.Equal(ComparisonMode.Strict, result.Mode);   // 比べ方は変えない
    }

    [Fact]
    public void 拡張子順なら拡張子と種類の両方に印を出す()
    {
        var order = Order(SortKey.Extension, SortDirection.Ascending);
        Assert.True(HeaderSort.ShowsArrow(order, DetailsColumn.Extension));
        Assert.True(HeaderSort.ShowsArrow(order, DetailsColumn.Type));
        Assert.False(HeaderSort.ShowsArrow(order, null));
    }

    [Fact]
    public void 並べ替えないときはどこにも印を出さない() =>
        Assert.DoesNotContain(new DetailsColumn?[] { null, DetailsColumn.Extension, DetailsColumn.Size, DetailsColumn.Modified,
            DetailsColumn.Created, DetailsColumn.Type, DetailsColumn.Attributes },
            column => HeaderSort.ShowsArrow(Order(SortKey.None, SortDirection.Ascending), column));
}
