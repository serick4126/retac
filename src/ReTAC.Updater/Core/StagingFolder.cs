using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace ReTAC.Updater.Core;

/// <summary>
/// R-109-4: 準備フォルダ。入れ替えは同じドライブの中の名前の付け替えで行うので、新しいファイルはいったんインストール先の中に置く。
/// 名前は毎回新しい GUID にし、<b>今回作ったもの</b>であることを作成の成否で確かめる（既にあれば別の GUID で作り直す）。
/// INV-UPDATER-KEEPS-FILES: 消してよいのは、今回作った準備フォルダの中の今回書き出したファイルと、空になったその準備フォルダだけ。
/// 再帰的に消さない。リパースポイント（ジャンクション・シンボリックリンク）に触れない。前回までの準備フォルダにも触れない
/// （アップデータの持ち物だと証明できないため）。
/// </summary>
public sealed class StagingFolder
{
    public const string Prefix = "ReTAC.update-";

    private readonly List<string> _written = new();

    private StagingFolder(string fullPath) => FullPath = fullPath;

    public string FullPath { get; }

    /// <summary>書き出したファイルの名前（書き出した順）。入れ替えで動かした後も残る。</summary>
    public IReadOnlyList<string> WrittenNames => _written;

    /// <summary>インストール先の中に、今回の準備フォルダを作る。</summary>
    public static StagingFolder Create(string installFolder)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var path = Path.Combine(installFolder, Prefix + Guid.NewGuid().ToString("N"));
            // Directory.CreateDirectory は既にあっても成功するので、今回作ったと言えない。Win32 の CreateDirectory で確かめる
            if (CreateDirectory(path, IntPtr.Zero)) return new StagingFolder(path);
            var error = Marshal.GetLastWin32Error();
            if (error != ERROR_ALREADY_EXISTS) throw new IOException(new Win32Exception(error).Message, error);
        }
        throw new IOException("準備フォルダを作れませんでした。");
    }

    /// <summary>
    /// 準備フォルダの中にファイルを新しく作って開く。<b>作れたときだけ</b>名前を記録する（片付けで消してよいのは記録したものだけ）。
    /// 同じ名前が既にあれば作成は失敗し、記録しない。先に記録すると、作れなかったのに片付けで既存のファイルを消してしまう。
    /// </summary>
    public FileStream CreateFile(string name)
    {
        var stream = new FileStream(Path.Combine(FullPath, name), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        _written.Add(name);
        return stream;
    }

    /// <summary>今回のファイルのうち残っているものを消し、準備フォルダを消す。消せなければ残す（利用者のファイルが入っている・使用中）。</summary>
    public void Cleanup()
    {
        // 準備フォルダそのものが差し替えられていたら（ジャンクション）、中をたどらない
        if (IsReparsePoint(FullPath)) return;

        foreach (var name in _written.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.Combine(FullPath, name);
            try
            {
                if (File.Exists(path) && !IsReparsePoint(path)) File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        try { Directory.Delete(FullPath, recursive: false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>インストール先に残っている準備フォルダの名前（今回のものを除く）。消さずに知らせるだけに使う。</summary>
    public static IReadOnlyList<string> FindLeftovers(string installFolder, StagingFolder? current)
    {
        try
        {
            return Directory.GetDirectories(installFolder, Prefix + "*")
                .Where(d => current is null || !string.Equals(d, current.FullPath, StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileName)
                .ToList()!;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }   // 分からなければ触れない
    }

    private const int ERROR_ALREADY_EXISTS = 183;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateDirectoryW")]
    private static extern bool CreateDirectory(string path, IntPtr security);
}
