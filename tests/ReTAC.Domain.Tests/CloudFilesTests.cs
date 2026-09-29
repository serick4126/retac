using ReTAC.Domain.Entries;

namespace ReTAC.Domain.Tests;

/// <summary>R-117 / INV-THUMBNAIL-NO-CLOUD-DOWNLOAD: 取り込み（hydration）を誘発しうる項目の判定。</summary>
public class CloudFilesTests
{
    [Theory]
    [InlineData(0x00400000, true)]    // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS
    [InlineData(0x00040000, true)]    // FILE_ATTRIBUTE_RECALL_ON_OPEN
    [InlineData(0x00001000, true)]    // FILE_ATTRIBUTE_OFFLINE（古い形式のプレースホルダー）
    [InlineData(0x00400010, true)]    // フォルダでも同じ
    [InlineData(0x00000020, false)]   // Archive だけ
    [InlineData(0x00080020, false)]   // PINNED（常にこのデバイスに保持。中身はある）
    public void 取り込みを誘発しうる属性(int raw, bool expected) =>
        Assert.Equal(expected, CloudFiles.IsPlaceholder((FileAttributes)raw));
}
