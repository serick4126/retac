using System.Drawing;
using WinKeys = System.Windows.Forms.Keys;
using ReTAC.App;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Keys;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-25 / R-39: 修飾キーなしの BackSpace は固定のキーで、いつも親フォルダへ戻る。割り当ての枠には無い。
/// ファイルリストのキー入力は、ハンドルを作らずに PressKey で届ける。
/// </summary>
public class BackSpaceFixedKeyTests
{
    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    public void 修飾キーなしのBackSpaceだけが親フォルダへ戻る固定のキー(bool shift, bool ctrl, bool alt, bool expected)
    {
        Assert.Equal(expected, FixedKeys.IsGoParent(Vk.Back, shift, ctrl, alt));
    }

    [Fact]
    public void BackSpace以外は固定のキーにしない()
    {
        Assert.False(FixedKeys.IsGoParent(Vk.Enter, false, false, false));
        Assert.False(FixedKeys.IsGoParent(Vk.Delete, false, false, false));
    }

    [Fact]
    public void 保存済みのBackSpaceの割り当ては読み込み時に無視される()
    {
        // 3.4.1 までは BackSpace が枠だった。移行はせず、枠に無いキーとして捨てる（Ctrl+Z と同じ）
        var settings = new AppSettings { KeyBindings = new() { ["BackSpace"] = new BuiltinTarget(CommandId.Refresh).Serialize() } };
        Assert.Null(settings.ToKeyMap().Resolve(new KeyBinding(Vk.Back)));
    }

    [Fact]
    public void 読み込むJSONのBackSpaceは解釈できないキーとして捨てて数える()
    {
        // R-103-2
        var defaults = DefaultKeyMap.Create();
        var current = KeySlots.All.ToDictionary(slot => slot, slot => defaults.Resolve(slot));
        int[] tools = [DefaultExternalTools.EditorId, DefaultExternalTools.ViewerId, DefaultExternalTools.TerminalId];
        var json = "{\"format\":\"ReTAC.KeyBindings\",\"keyBindings\":{\"BackSpace\":\"Refresh\"}}";

        var result = KeyBindingFile.Import(json, current, tools);

        Assert.NotNull(result);
        Assert.Equal(1, result!.UnknownKey);
        Assert.Equal(0, result.Loaded);
    }

    // ---- ファイルリストのキー入力からイベントまで（仕様書の再レビューの指摘） ----

    private static (FileListView List, List<string> Events) View(bool empty)
    {
        var list = new FileListView { Size = new Size(600, 300) };
        list.SetEntries(empty ? [] : Enumerable.Range(0, 5).Select(i => TestEntries.File($"f{i}.txt")).ToList());
        var events = new List<string>();
        list.ParentRequested += (_, _) => events.Add("Parent");
        list.CommandKey += (_, e) => events.Add(e.Control ? "CommandKey+Ctrl" : "CommandKey");
        return (list, events);
    }

    [Fact]
    public void 項目のある一覧で修飾キーなしのBackSpaceは親フォルダへ()
    {
        var (list, events) = View(empty: false);
        using var _ = list;

        list.PressKey(WinKeys.Back);

        Assert.Equal(["Parent"], events);
    }

    [Theory]
    [InlineData(WinKeys.Shift)]
    [InlineData(WinKeys.Alt)]
    public void 項目のある一覧でShiftやAlt付きのBackSpaceは親フォルダへ移らない(WinKeys modifier)
    {
        var (list, events) = View(empty: false);
        using var _ = list;

        list.PressKey(WinKeys.Back | modifier);

        // 割り当ての経路へ渡す。BackSpace の枠は無いので、その先では何もしない
        Assert.Equal(["CommandKey"], events);
    }

    [Fact]
    public void 項目のある一覧でCtrlとBackSpaceは割り当ての経路へ渡る()
    {
        var (list, events) = View(empty: false);
        using var _ = list;

        list.PressKey(WinKeys.Back | WinKeys.Control);

        Assert.Equal(["CommandKey+Ctrl"], events);
    }

    [Theory]
    [InlineData(WinKeys.None)]
    [InlineData(WinKeys.Shift)]
    [InlineData(WinKeys.Alt)]
    [InlineData(WinKeys.Control)]
    public void 空の一覧ではBackSpaceをすべてMainFormへ渡す(WinKeys modifier)
    {
        // 空の一覧はキーをすべて OnCommandKey に渡す。そこで修飾キーなしだけが親フォルダへ移る（判定は上のテスト）
        var (list, events) = View(empty: true);
        using var _ = list;

        list.PressKey(WinKeys.Back | modifier);

        Assert.Equal([modifier == WinKeys.Control ? "CommandKey+Ctrl" : "CommandKey"], events);
    }
}
