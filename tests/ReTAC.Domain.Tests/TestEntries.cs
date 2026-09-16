using System.IO;
using ReTAC.Domain.Entries;

namespace ReTAC.Domain.Tests;

internal static class TestEntries
{
    public static Entry File(string name, FileAttributes attributes = FileAttributes.Normal, long size = 0, DateTime? mtime = null) =>
        Entry.ForFile($@"C:\work\{name}", name, attributes, size, mtime ?? new DateTime(2026, 1, 1));

    public static Entry Folder(string name, FileAttributes attributes = FileAttributes.Directory, DateTime? mtime = null) =>
        Entry.ForFolder($@"C:\work\{name}", name, attributes, mtime ?? new DateTime(2026, 1, 1));

    public static Entry Parent() => Entry.ForParent(@"C:\");

    public static string[] Names(IEnumerable<Entry> entries) => entries.Select(e => e.Name).ToArray();
}
