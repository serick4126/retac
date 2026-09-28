using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ReTAC.Updater.Core;

/// <summary>
/// R-109-2 / INV-UPDATER-VERIFY-IN-WRITER: ダウンロードした zip の照合と展開。
/// zip は <b>開いたハンドルのまま</b>扱う。ほかのプロセスに開かせず（FileShare.None）、閉じたら OS が消す（DeleteOnClose）。
/// 照合と展開は同じハンドルから行うので、照合してから展開するまでの間に差し替えられることが無い。
/// </summary>
public static class ZipPackage
{
    /// <summary>ダウンロード先のファイルを作る。閉じる（プロセスが落ちるのを含む）と消える。</summary>
    public static FileStream CreateDownloadFile(string folder) =>
        new(Path.Combine(folder, "ReTAC-" + Guid.NewGuid().ToString("N") + ".zip"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);

    /// <summary>先頭から読み直して SHA-256 を計算し、GitHub の <c>digest</c>（<c>sha256:16進</c>）と比べる。</summary>
    public static bool Matches(Stream zip, string digest)
    {
        const string prefix = "sha256:";
        if (!digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        return string.Equals(Sha256(zip), digest.Substring(prefix.Length), StringComparison.OrdinalIgnoreCase);
    }

    public static string Sha256(Stream stream)
    {
        stream.Position = 0;
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(stream);
        var text = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) text.Append(b.ToString("x2"));
        return text.ToString();
    }

    /// <summary>
    /// 先頭から読み直して zip として開き、全項目を検査してから（Distribution.Check）準備フォルダへ書き出す。
    /// 書き出したファイルはディスクへ書き出してから閉じる（停電のあとで、名前だけ新しく中身が空のファイルが残らないように）。
    /// </summary>
    public static Reason? Extract(Stream zip, StagingFolder staging)
    {
        zip.Position = 0;
        try
        {
            using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
            var entries = archive.Entries.ToList();
            if (Distribution.Check(entries.Select(e => e.FullName), staging.FullPath) is { } problem) return problem;

            foreach (var entry in entries)
            {
                var name = Distribution.Canonical(Path.GetFileName(entry.FullName))!;
                using var source = entry.Open();
                using var target = new FileStream(staging.PathFor(name), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                source.CopyTo(target);
                target.Flush(flushToDisk: true);
            }
            return null;
        }
        catch (InvalidDataException) { return Reason.Corrupt; }
    }
}
