using System.Text.Json;
using System.Text.Json.Nodes;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Keys;

namespace ReTAC.App;

/// <summary>
/// キー割り当ての読み込み結果（R-103-2）。<see cref="Assignments"/> は <see cref="KeySlots.All"/> の
/// 全枠を持つ（ファイルに無かった枠は既定で埋めてある）。それ以外の欄は捨てた件数
/// （「N 件を読み込みました」の通知に使う）。
/// </summary>
/// <param name="UnknownKey">解釈できないキー（<see cref="KeySlots.Parse"/> が null、または枠に無いもの。Ctrl+Z を含む）</param>
/// <param name="UnknownCommand">知らないコマンド（<see cref="CommandTarget.Parse"/> が null）</param>
/// <param name="Duplicate">同じ枠を指す 2 件目以降（表記の揺れで同じ枠になったものを含む）</param>
/// <param name="Tool">外部ツールを指す値（エクスポートでは書かない）、または今の下書きで外部ツールに割り当たっている枠への値</param>
public sealed record ImportResult(
    Dictionary<KeyBinding, CommandTarget?> Assignments,
    int Loaded,
    int UnknownKey,
    int UnknownCommand,
    int Duplicate,
    int Tool);

/// <summary>
/// キー割り当てのエクスポート・インポート（R-103）。ここはファイルの中身の変換だけを持つ
/// 純粋な関数で、ディスクの読み書き（一時ファイル経由の置換・V-07 と同じ理由）は呼び出し側が行う。
/// </summary>
public static class KeyBindingFile
{
    private const string Format = "ReTAC.KeyBindings";

    /// <summary>R-103-1: 下書きの全枠を書き出す。外部ツールの枠は書かない（Q9）。</summary>
    public static string Export(IReadOnlyDictionary<KeyBinding, CommandTarget?> assignments)
    {
        var keyBindings = new JsonObject();
        foreach (var slot in KeySlots.All)
        {
            var target = assignments.GetValueOrDefault(slot);
            if (target is ToolTarget) continue; // Q9: 外部ツールの枠はそもそも書かない
            keyBindings[KeySlots.Label(slot)] = target?.Serialize() ?? "";
        }

        var root = new JsonObject { ["format"] = Format, ["keyBindings"] = keyBindings };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// R-103-2: 読み込んだファイルは信用できない入力として扱う。読めない・形が違えば null（何も変えない）。
    /// </summary>
    /// <param name="current">今の下書き。外部ツールに割り当たっている枠はそのまま残す（Q9）。</param>
    /// <param name="existingToolIds">
    /// 今登録されている外部ツールの Id。ファイルに無い枠を既定で埋めるとき、既定が外部ツールを指し、
    /// かつそのツールが無ければ未割り当てにする（既定に戻す・KeyAssignPage と同じ扱い）。
    /// </param>
    public static ImportResult? Import(
        string json,
        IReadOnlyDictionary<KeyBinding, CommandTarget?> current,
        IEnumerable<int> existingToolIds)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("format", out var formatProperty) ||
                formatProperty.ValueKind != JsonValueKind.String ||
                formatProperty.GetString() != Format)
                return null;
            if (!root.TryGetProperty("keyBindings", out var bindingsProperty) ||
                bindingsProperty.ValueKind != JsonValueKind.Object)
                return null;

            // 重複はプロパティの出現順で決める（先勝ち）。Dictionary に読むと後勝ちになり数えられない
            var fromFile = new Dictionary<KeyBinding, CommandTarget?>();
            var seenSlots = new HashSet<KeyBinding>();
            int unknownKey = 0, unknownCommand = 0, duplicate = 0, tool = 0;

            foreach (var property in bindingsProperty.EnumerateObject())
            {
                if (KeySlots.Parse(property.Name) is not { } slot || !KeySlots.All.Contains(slot))
                {
                    unknownKey++;
                    continue;
                }
                if (!seenSlots.Add(slot))
                {
                    duplicate++;
                    continue;
                }

                var text = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : null;
                if (text is null)
                {
                    unknownCommand++; // 値が文字列ですらない
                    continue;
                }

                var target = text.Length == 0 ? null : CommandTarget.Parse(text);
                if (text.Length > 0 && target is null)
                {
                    unknownCommand++;
                    continue;
                }
                if (target is ToolTarget)
                {
                    tool++; // エクスポートでは書かない値。手で書き足されたもの
                    continue;
                }

                fromFile[slot] = target;
            }

            var existing = existingToolIds.ToHashSet();
            var defaults = DefaultKeyMap.Create();
            var assignments = new Dictionary<KeyBinding, CommandTarget?>();
            var loaded = 0;

            foreach (var slot in KeySlots.All)
            {
                var now = current.GetValueOrDefault(slot);
                if (now is ToolTarget)
                {
                    // 今の下書きで外部ツールに割り当たっている枠は、ファイルの値を捨ててそのまま残す（Q9）
                    if (fromFile.ContainsKey(slot)) tool++;
                    assignments[slot] = now;
                    continue;
                }

                if (fromFile.TryGetValue(slot, out var fileValue))
                {
                    assignments[slot] = fileValue;
                    loaded++;
                    continue;
                }

                // ファイルに無い枠は既定で埋める（新しい版で足した枠を、古い版の書き出しで空にしないため）
                var byDefault = defaults.Resolve(slot);
                assignments[slot] = byDefault is ToolTarget defaultTool && !existing.Contains(defaultTool.ToolId)
                    ? null
                    : byDefault;
            }

            return new ImportResult(assignments, loaded, unknownKey, unknownCommand, duplicate, tool);
        }
    }
}
