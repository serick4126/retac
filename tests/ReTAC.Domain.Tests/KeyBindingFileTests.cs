using ReTAC.App;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Keys;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

/// <summary>キー割り当てのエクスポート・インポート（§4 / R-103）</summary>
public class KeyBindingFileTests
{
    private static readonly int[] AllToolIds =
        [DefaultExternalTools.EditorId, DefaultExternalTools.ViewerId, DefaultExternalTools.TerminalId];

    private static Dictionary<KeyBinding, CommandTarget?> DefaultAssignments()
    {
        var defaults = DefaultKeyMap.Create();
        return KeySlots.All.ToDictionary(slot => slot, slot => defaults.Resolve(slot));
    }

    [Fact]
    public void 書き出して読み込むと同じ割り当てに戻る()
    {
        var original = DefaultAssignments();
        var json = KeyBindingFile.Export(original);

        var result = KeyBindingFile.Import(json, original, AllToolIds);

        Assert.NotNull(result);
        foreach (var slot in KeySlots.All)
            Assert.Equal(original[slot], result!.Assignments[slot]);
        Assert.Equal(0, result!.UnknownKey);
        Assert.Equal(0, result.UnknownCommand);
        Assert.Equal(0, result.Duplicate);
        Assert.Equal(0, result.Tool);
    }

    [Fact]
    public void 外部ツールの枠はエクスポートで書かない()
    {
        // E は既定でエディタ（Tool:1）を指す（DefaultKeyMap）。Q9: この枠は書かない
        var json = KeyBindingFile.Export(DefaultAssignments());

        Assert.DoesNotContain("Tool:", json);
        Assert.DoesNotContain("\"E\"", json);
        Assert.Contains("\"F5\"", json); // 組み込みの枠は空でも既定でも書く
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"format\":\"Other\",\"keyBindings\":{}}")]
    [InlineData("{\"format\":\"ReTAC.KeyBindings\",\"keyBindings\":\"nope\"}")]
    [InlineData("[]")]
    public void 壊れたファイルは読み込めない(string json)
    {
        Assert.Null(KeyBindingFile.Import(json, DefaultAssignments(), AllToolIds));
    }

    [Fact]
    public void ファイルに無い枠は既定に戻る()
    {
        var current = DefaultAssignments();
        // B は既定では未割り当て。手で割り当てておいても、ファイルに書かれていなければ既定（未割り当て）に戻る
        var bSlot = new KeyBinding(Vk.Letter('B'));
        current[bSlot] = new BuiltinTarget(CommandId.Refresh);

        var json = "{\"format\":\"ReTAC.KeyBindings\",\"keyBindings\":{\"F5\":\"Refresh\"}}";
        var result = KeyBindingFile.Import(json, current, AllToolIds);

        Assert.NotNull(result);
        Assert.Null(result!.Assignments[bSlot]);
        var aSlot = new KeyBinding(Vk.Letter('A'));
        Assert.Equal(new BuiltinTarget(CommandId.ChangeAttributes), result.Assignments[aSlot]);
        Assert.Equal(1, result.Loaded);
    }

    [Fact]
    public void 解釈できないキーは捨てて数える()
    {
        // Ctrl+Z は R-83 により枠に無い。NotAKey はそもそも Enum.TryParse が通らない
        var json = "{\"format\":\"ReTAC.KeyBindings\",\"keyBindings\":{\"Ctrl+Z\":\"Refresh\",\"NotAKey\":\"Refresh\"}}";
        var result = KeyBindingFile.Import(json, DefaultAssignments(), AllToolIds);

        Assert.NotNull(result);
        Assert.Equal(2, result!.UnknownKey);
        Assert.Equal(0, result.Loaded);
    }

    [Fact]
    public void 知らないコマンドは捨てて数える()
    {
        var json = "{\"format\":\"ReTAC.KeyBindings\",\"keyBindings\":{\"F5\":\"NoSuchCommand\"}}";
        var result = KeyBindingFile.Import(json, DefaultAssignments(), AllToolIds);

        Assert.NotNull(result);
        Assert.Equal(1, result!.UnknownCommand);
    }

    [Fact]
    public void 外部ツールを指す値は捨てて数える()
    {
        // エクスポートでは書かない値（Tool:9）を手で書き足した想定
        var json = "{\"format\":\"ReTAC.KeyBindings\",\"keyBindings\":{\"F5\":\"Tool:9\"}}";
        var result = KeyBindingFile.Import(json, DefaultAssignments(), AllToolIds);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Tool);
        var f5 = new KeyBinding(Vk.Function(5));
        Assert.Equal(new BuiltinTarget(CommandId.Refresh), result.Assignments[f5]); // 既定に戻る
    }

    [Fact]
    public void 表記の揺れは同じ枠として重複で数える()
    {
        // Ctrl+ の大小文字は KeySlots.Parse が吸収する。先に出てきた方（IncrementalSearch）を採る
        var json = "{\"format\":\"ReTAC.KeyBindings\",\"keyBindings\":{\"Ctrl+E\":\"IncrementalSearch\",\"ctrl+E\":\"GoBack\"}}";
        var result = KeyBindingFile.Import(json, DefaultAssignments(), AllToolIds);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Duplicate);
        var ctrlE = new KeyBinding(Vk.Letter('E'), Ctrl: true);
        Assert.Equal(new BuiltinTarget(CommandId.IncrementalSearch), result.Assignments[ctrlE]);
    }

    [Fact]
    public void 今ツールが割り当たっている枠への値は捨てて数える()
    {
        var current = DefaultAssignments(); // E は既定でエディタ（Tool:1）
        var json = "{\"format\":\"ReTAC.KeyBindings\",\"keyBindings\":{\"E\":\"Refresh\"}}";

        var result = KeyBindingFile.Import(json, current, AllToolIds);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Tool);
        var eSlot = new KeyBinding(Vk.Letter('E'));
        Assert.Equal(new ToolTarget(DefaultExternalTools.EditorId), result.Assignments[eSlot]);
    }

    [Fact]
    public void 今のツールに無ければ既定は空になる()
    {
        var current = DefaultAssignments();
        var eSlot = new KeyBinding(Vk.Letter('E'));
        current[eSlot] = null; // 既定のツール割り当てを手で外した状態

        var json = "{\"format\":\"ReTAC.KeyBindings\",\"keyBindings\":{}}";
        // エディタを削除したのと同じ状態（existingToolIds に含めない）
        var result = KeyBindingFile.Import(
            json, current, [DefaultExternalTools.ViewerId, DefaultExternalTools.TerminalId]);

        Assert.NotNull(result);
        Assert.Null(result!.Assignments[eSlot]);
    }
}
