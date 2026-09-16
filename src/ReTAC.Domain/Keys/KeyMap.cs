using ReTAC.Domain.Commands;

namespace ReTAC.Domain.Keys;

/// <summary>
/// キーバインド。仮想キーコード（VK_*）＋ Shift ＋ Ctrl（F-06）。Alt はキーマップに含まれない。
/// Ctrl+Shift の枠は無い（仕様書 F-06）。
/// </summary>
public readonly record struct KeyBinding(ushort VirtualKey, bool Shift = false, bool Ctrl = false);

/// <summary>R-12: キー入力からコマンドへの解決は、この 1 つの経路だけで行う。</summary>
public sealed class KeyMap
{
    private readonly Dictionary<KeyBinding, CommandTarget> _bindings;

    public KeyMap(IEnumerable<KeyValuePair<KeyBinding, CommandTarget>> bindings)
    {
        _bindings = new Dictionary<KeyBinding, CommandTarget>(bindings);
    }

    public CommandTarget? Resolve(KeyBinding key) =>
        _bindings.TryGetValue(key, out var target) ? target : null;

    public void Assign(KeyBinding key, CommandTarget? target)
    {
        if (target is null) _bindings.Remove(key);
        else _bindings[key] = target;
    }

    /// <summary>F-01: 削除した外部ツールに割り当てていたキーを未割り当てに戻す。</summary>
    public void ReleaseTool(int toolId)
    {
        var target = new ToolTarget(toolId);
        foreach (var key in _bindings.Where(b => b.Value == target).Select(b => b.Key).ToList())
            _bindings.Remove(key);
    }

    /// <summary>
    /// 存在しない外部ツールを指す割り当てを落とす。設定を読み込んだ直後に通す。
    /// 既定のキー（Tool:1〜3）はツールを消しても残るため、ここで閉じないと
    /// 消したはずのツールを指すキーが生き続ける。R-25 が KeySlotList に対してしているのと同じ扱い。
    /// </summary>
    public void DropUnknownTools(IEnumerable<int> existingToolIds)
    {
        var known = existingToolIds.ToHashSet();
        foreach (var key in _bindings
                     .Where(b => b.Value is ToolTarget tool && !known.Contains(tool.ToolId))
                     .Select(b => b.Key).ToList())
            _bindings.Remove(key);
    }

    public IReadOnlyDictionary<KeyBinding, CommandTarget> Bindings => _bindings;
}
