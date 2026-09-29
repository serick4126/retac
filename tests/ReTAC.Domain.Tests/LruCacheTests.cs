using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Tests;

/// <summary>R-117 / P-25: サムネイルのキャッシュ。追い出しは入れる前。追い出したものは解放する。</summary>
public class LruCacheTests
{
    private sealed class Item(int size) { public int Size = size; public bool Disposed; }

    private static LruCache<string, Item> Cache(long capacity) => new(capacity, i => i.Size, i => i.Disposed = true);

    [Fact]
    public void 上限を超える前に古いものから捨てる()
    {
        var cache = Cache(10);
        var a = new Item(4); var b = new Item(4); var c = new Item(4);
        cache.Add("a", a); cache.Add("b", b);
        cache.Add("c", c);
        Assert.True(a.Disposed);
        Assert.False(b.Disposed);
        Assert.False(c.Disposed);   // 今入れたものは捨てない
        Assert.True(cache.TryGet("c", out var got) && ReferenceEquals(got, c));
    }

    [Fact]
    public void 使ったものは新しくなる()
    {
        var cache = Cache(10);
        var a = new Item(4); var b = new Item(4);
        cache.Add("a", a); cache.Add("b", b);
        cache.TryGet("a", out _);
        cache.Add("c", new Item(4));
        Assert.False(a.Disposed);
        Assert.True(b.Disposed);
    }

    [Fact]
    public void 同じ鍵は古い値を解放して置き換える()
    {
        var cache = Cache(10);
        var old = new Item(4);
        cache.Add("a", old);
        cache.Add("a", new Item(4));
        Assert.True(old.Disposed);
        Assert.Equal(1, cache.Count);
        Assert.Equal(4, cache.Total);
    }

    [Fact]
    public void 上限より大きい1つは入れずに解放する()
    {
        var cache = Cache(10);
        var big = new Item(11);
        cache.Add("big", big);
        Assert.True(big.Disposed);
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Total);
    }

    [Fact]
    public void 消すとすべて解放する()
    {
        var cache = Cache(10);
        var a = new Item(4);
        cache.Add("a", a);
        cache.Clear();
        Assert.True(a.Disposed);
        Assert.Equal(0, cache.Total);
    }

    [Fact]
    public void 同じインスタンスをもう一度入れても解放しない()
    {
        var cache = Cache(10);
        var a = new Item(4);
        cache.Add("a", a);
        cache.Add("a", a);
        Assert.False(a.Disposed);
        Assert.True(cache.TryGet("a", out var got) && ReferenceEquals(got, a));
        Assert.Equal(1, cache.Count);
        Assert.Equal(4, cache.Total);
    }

    [Fact]
    public void 既存の鍵を上限より大きい値で置き換えると旧値も新値も解放する()
    {
        var cache = Cache(10);
        var old = new Item(4); var big = new Item(11);
        cache.Add("a", old);
        cache.Add("a", big);
        Assert.True(old.Disposed);
        Assert.True(big.Disposed);
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Total);
    }

    [Fact]
    public void ちょうど満杯のときは必要な分だけ古いものから捨てる()
    {
        var cache = Cache(10);
        var a = new Item(5); var b = new Item(5); var c = new Item(3);
        cache.Add("a", a); cache.Add("b", b);
        cache.Add("c", c);
        Assert.True(a.Disposed);
        Assert.False(b.Disposed);
        Assert.False(c.Disposed);
        Assert.Equal(8, cache.Total);
    }
}
