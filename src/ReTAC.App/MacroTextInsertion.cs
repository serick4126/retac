using ReTAC.Domain.Tools;

namespace ReTAC.App;

/// <summary>R-136: マクロの通常挿入は選択範囲だけを置き換える。</summary>
internal static class MacroTextInsertion
{
    internal static (string Text, int Caret) Apply(string text, int start, int length, MacroCatalogEntry entry)
    {
        start = Math.Clamp(start, 0, text.Length);
        length = Math.Clamp(length, 0, text.Length - start);
        if (entry.Insert is not { } insert) return (text, start);
        return (text[..start] + insert + text[(start + length)..], start + Math.Clamp(entry.CaretOffset, 0, insert.Length));
    }
}
