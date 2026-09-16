using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

/// <summary>F-04: 設定ダイアログに出すマクロの一覧</summary>
public class MacroCatalogTests
{
    [Fact]
    public void すべてのマクロが一覧に載っている()
    {
        var inserts = MacroCatalog.Entries.Where(e => e.Group == MacroCatalog.MacroGroup).Select(e => e.Insert ?? "").ToList();
        foreach (var name in ArgumentTemplate.Names.Keys)
            Assert.Contains(inserts, insert => insert.StartsWith("${" + name, StringComparison.Ordinal));
    }

    [Fact]
    public void 挿入する文字はそのまま正しい引数になり挿入後のカーソルは文字の中にある()
    {
        foreach (var entry in MacroCatalog.Entries.Where(e => e.Insert is not null))
        {
            Assert.True(ArgumentTemplate.Parse(entry.Insert!).IsValid, entry.Insert);
            Assert.InRange(entry.CaretOffset, 0, entry.Insert!.Length);
        }
    }

    [Fact]
    public void スクリプトの例は挿入せず仮想環境とバッチファイルの案内を含む()
    {
        var scripts = MacroCatalog.Entries.Where(e => e.Group == MacroCatalog.ScriptGroup).ToList();
        Assert.All(scripts, e => Assert.Null(e.Insert));
        Assert.Contains(scripts, e => e.Label.Contains("仮想環境"));
        Assert.Contains(scripts, e => e.Description.Contains("%~dp0"));
    }
}
