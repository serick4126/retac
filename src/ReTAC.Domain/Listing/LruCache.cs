namespace ReTAC.Domain.Listing;

/// <summary>
/// R-117: 大きさ（コスト）の合計に上限のある LRU。追い出しは入れる前に行う（P-25。入れてから追い出すと、今入れたものを解放して返してしまう）。
/// UI のスレッドだけで使う（ロックしない）。
/// </summary>
public sealed class LruCache<TKey, TValue>(long capacity, Func<TValue, long> cost, Action<TValue> evict) where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map = new();
    private readonly LinkedList<(TKey Key, TValue Value)> _order = new();   // 先頭が新しい

    public int Count => _map.Count;
    public long Total { get; private set; }

    public bool TryGet(TKey key, out TValue value)
    {
        if (_map.TryGetValue(key, out var node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
        value = default!;
        return false;
    }

    public void Add(TKey key, TValue value)
    {
        if (_map.TryGetValue(key, out var existing))
        {
            // 同じインスタンスの再登録は解放しない（P-25。解放した値を持ち続けて返してしまう）。新しくするだけ。
            if (ReferenceEquals(existing.Value.Value, value))
            {
                _order.Remove(existing);
                _order.AddFirst(existing);
                return;
            }
            Drop(existing);
        }
        var size = cost(value);
        if (size > capacity) { evict(value); return; }
        while (Total + size > capacity && _order.Last is { } last) Drop(last);
        _map[key] = _order.AddFirst((key, value));
        Total += size;
    }

    public void Clear()
    {
        while (_order.Last is { } last) Drop(last);
    }

    private void Drop(LinkedListNode<(TKey Key, TValue Value)> node)
    {
        _order.Remove(node);
        _map.Remove(node.Value.Key);
        Total -= cost(node.Value.Value);
        evict(node.Value.Value);
    }
}
