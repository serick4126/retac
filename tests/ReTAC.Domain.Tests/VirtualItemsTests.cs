using System.IO.Compression;
using System.Runtime.InteropServices;
using ReTAC.Domain.FileOps;
using ReTAC.Shell;

namespace ReTAC.Domain.Tests;

/// <summary>R-146-1: 書庫の中の項目を識別子の複製として覚え、データを手放した後で取り出せる（P1-2）。日時は書庫の中のまま（S2）。</summary>
public class VirtualItemsTests
{
    // ZIP の日時は 2 秒単位・ローカル時刻。偶数秒にする
    private static readonly DateTime Stamp = new(2020, 1, 2, 3, 4, 6);

    private static string MakeZip(string root)
    {
        var zip = Path.Combine(root, "a.zip");
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var name in new[] { "x.txt", "sub/y.txt" })
        {
            var entry = archive.CreateEntry(name);
            entry.LastWriteTime = new DateTimeOffset(Stamp, TimeZoneInfo.Local.GetUtcOffset(Stamp));
            using var writer = new StreamWriter(entry.Open());
            writer.Write(name);
        }
        return zip;
    }

    [Fact]
    public void Capture_SurvivesReleasingData() => Sta.Run(() => Sta.WithTempFolder(root =>
    {
        var zip = MakeZip(root);
        var data = ShellDataObject.For([Path.Combine(zip, "x.txt"), Path.Combine(zip, "sub")]);
        Assert.NotNull(data);
        using var items = VirtualItems.FromDataObject(data!);
        Marshal.FinalReleaseComObject(data!);   // ドロップが終わってデータが使えなくなった状態
        Assert.NotNull(items);
        Assert.Equal(["x.txt", "sub"], items!.Items.Select(i => i.Name));
        Assert.Equal(Path.Combine(zip, "x.txt"), items.Items[0].DisplayPath, ignoreCase: true);

        var dest = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
        using (var operation = new ShellFileOperation(IntPtr.Zero, silentOverwrite: false, noUi: true))
        {
            foreach (var item in items.Items) operation.CopyIdList(item.IdList, dest);
            Assert.True(operation.Execute());
        }
        Assert.Equal("x.txt", File.ReadAllText(Path.Combine(dest, "x.txt")));
        Assert.Equal("sub/y.txt", File.ReadAllText(Path.Combine(dest, "sub", "y.txt")));
    }));

    [Fact]
    public void CopiedItem_KeepsArchiveTimestamp() => Sta.Run(() => Sta.WithTempFolder(root =>
    {
        var zip = MakeZip(root);
        var data = ShellDataObject.For([Path.Combine(zip, "x.txt")]);
        using var items = VirtualItems.FromDataObject(data!)!;
        var dest = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
        using (var operation = new ShellFileOperation(IntPtr.Zero, silentOverwrite: false, noUi: true))
        {
            operation.CopyIdList(items.Items[0].IdList, dest);
            Assert.True(operation.Execute());
        }
        // R-146-1: ファイルの日時は書庫の中の日時のまま届く（コピーした時刻にならない）。違ったら実装を止めて報告する
        Assert.Equal(Stamp, File.GetLastWriteTime(Path.Combine(dest, "x.txt")));
    }));

    [Fact]
    public void FileSystemItems_AreNotVirtual() => Sta.Run(() => Sta.WithTempFolder(root =>
    {
        var file = Path.Combine(root, "real.txt");
        File.WriteAllText(file, "x");   // 安全: テストが新しく作ったフォルダ（Sta.WithTempFolder）の中
        var data = ShellDataObject.For([file]);
        Assert.Null(VirtualItems.FromDataObject(data!));   // ディスク上の項目は今までどおりパスで扱う
    }));
}
