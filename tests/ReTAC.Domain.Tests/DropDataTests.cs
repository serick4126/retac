using System.IO.Compression;
using System.Windows.Forms;
using ReTAC.App;
using ReTAC.Shell;

namespace ReTAC.Domain.Tests;

/// <summary>R-146-1 / P2-4: 貼り付け（コピー・切り取り）のクリップボードの中身からも、ドロップと同じく仮想項目を覚える。</summary>
public class DropDataTests
{
    [Theory]
    [InlineData(DragDropEffects.Copy)]
    [InlineData(DragDropEffects.Move)]   // 切り取り。取り出しはコピーで行い、書庫は変えない（S1）
    public void CaptureVirtual_FromClipboardShape(DragDropEffects preferred) => Sta.Run(() => Sta.WithTempFolder(root =>
    {
        var zip = Path.Combine(root, "a.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("x.txt").Open())) writer.Write("x");
        var data = new DataObject(ShellDataObject.For([Path.Combine(zip, "x.txt")])!);
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes((int)preferred)));

        Assert.True(DropData.HasVirtual(data));
        using var items = DropData.CaptureVirtual(data);
        Assert.NotNull(items);
        Assert.Equal(["x.txt"], items!.Items.Select(i => i.Name));
    }));

    [Fact]
    public void HasVirtual_FalseForFileDrop()
    {
        var data = new DataObject(DataFormats.FileDrop, new[] { @"C:\a\x.txt" });
        data.SetData(VirtualItems.ShellIdListFormat, new MemoryStream(new byte[8]));
        Assert.False(DropData.HasVirtual(data));
        Assert.False(DropData.HasVirtual(null));
    }
}
