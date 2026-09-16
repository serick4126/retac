using System.IO;
using ReTAC.Domain.FileOps;
using ReTAC.Domain.Formatting;

namespace ReTAC.Domain.Tests;

/// <summary>`I` の 3 形式（R-57）</summary>
public class NameFormatTests
{
    [Fact]
    public void 三形式それぞれの文字列になる()
    {
        var entry = TestEntries.File("memo.txt");

        Assert.Equal(@"C:\work\memo.txt", NameFormats.Format(entry, NameFormat.PathAndName));
        Assert.Equal("memo.txt", NameFormats.Format(entry, NameFormat.NameOnly));
        Assert.Equal("C:/work/memo.txt", NameFormats.Format(entry, NameFormat.SlashPath));
    }

    [Fact]
    public void 複数対象は改行区切りで親フォルダ項目は含めない()
    {
        var entries = new[] { TestEntries.Parent(), TestEntries.File("a.txt"), TestEntries.File("b.txt") };

        Assert.Equal($"a.txt{Environment.NewLine}b.txt", NameFormats.Format(entries, NameFormat.NameOnly));
    }
}

/// <summary>ファイルの連結（R-35-2）。テストは自分で作った一時フォルダの中だけを触る。</summary>
public sealed class FileConcatTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "retac_test_" + Guid.NewGuid().ToString("N"));

    public FileConcatTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string name, params byte[] content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    [Fact]
    public void 指定した順に連結する()
    {
        var a = Write("a.bin", (byte)'A');
        var b = Write("b.bin", (byte)'B');
        var destination = Path.Combine(_root, "out.bin");

        FileConcat.Concat([b, a], destination, cutEof: true, appendNewLine: false);

        Assert.Equal("BA", File.ReadAllText(destination));
    }

    [Fact]
    public void EOFカットは末尾の1Aだけを落とす()
    {
        var a = Write("a.bin", (byte)'A', FileConcat.EofMark);
        var b = Write("b.bin", FileConcat.EofMark, (byte)'B');
        var destination = Path.Combine(_root, "out.bin");

        FileConcat.Concat([a, b], destination, cutEof: true, appendNewLine: false);

        // a の末尾の 1A は落ち、b の途中（先頭）の 1A は残る
        Assert.Equal([(byte)'A', FileConcat.EofMark, (byte)'B'], File.ReadAllBytes(destination));
    }

    [Fact]
    public void EOFカットを外すとそのまま連結する()
    {
        var a = Write("a.bin", (byte)'A', FileConcat.EofMark);
        var destination = Path.Combine(_root, "out.bin");

        FileConcat.Concat([a], destination, cutEof: false, appendNewLine: false);

        Assert.Equal([(byte)'A', FileConcat.EofMark], File.ReadAllBytes(destination));
    }

    [Fact]
    public void 連結先が連結元に含まれていたら元を壊す前に断る()
    {
        // FileMode.Create は先に切り詰める。既定の宛先 concat.txt をマークしたまま再実行すると起きる
        var a = Write("a.bin", (byte)'A');
        var destination = Write("out.bin", (byte)'Z');

        Assert.Throws<IOException>(() => FileConcat.Concat([a, destination], destination, cutEof: true, appendNewLine: false));
        Assert.Equal([(byte)'Z'], File.ReadAllBytes(destination));
    }

    [Fact]
    public void 末尾に改行を付けられる()
    {
        var a = Write("a.bin", (byte)'A');
        var destination = Path.Combine(_root, "out.bin");

        FileConcat.Concat([a], destination, cutEof: true, appendNewLine: true);

        Assert.Equal("A\r\n", File.ReadAllText(destination));
    }
}
