using System.IO;

namespace ReTAC.Domain.Entries;

/// <summary>属性配色の区分。実際の色は UI 層が割り当てる（Domain は System.Drawing に依存しない）。</summary>
public enum AttributeColor
{
    Normal,
    System,
    ReadOnly,
    Hidden,
    Compressed,
    Encrypted,
}

/// <summary>R-06: システム ＞ 書込禁止 ＞ 隠し ＞ NTFS圧縮／暗号化 ＞ 通常。</summary>
public static class AttributeColorRule
{
    public static AttributeColor Classify(FileAttributes attributes)
    {
        if (attributes.HasFlag(FileAttributes.System)) return AttributeColor.System;
        if (attributes.HasFlag(FileAttributes.ReadOnly)) return AttributeColor.ReadOnly;
        if (attributes.HasFlag(FileAttributes.Hidden)) return AttributeColor.Hidden;
        // 圧縮と暗号化は同時に成立しないため両者の優劣は問わない（R-06）
        if (attributes.HasFlag(FileAttributes.Compressed)) return AttributeColor.Compressed;
        if (attributes.HasFlag(FileAttributes.Encrypted)) return AttributeColor.Encrypted;
        return AttributeColor.Normal;
    }
}
