using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ReTAC.Updater.Core;

/// <summary>
/// R-109-1: インストール先の正規化と、排他の名前。
/// 同じフォルダには、表記（末尾の区切り・大文字小文字・8.3 形式の名前・ジャンクション・ドライブ文字）が違っても同じ名前を使う。
/// </summary>
public static class InstallFolder
{
    /// <summary>
    /// フォルダ（またはファイル）を開き、OS が返す最終的なパスにする。開けなければ null。
    /// ボリュームは GUID の形で表す（ドライブ文字の違いを消す）。GUID で表せないとき（ネットワークのフォルダ）は DOS の形にする。
    /// </summary>
    public static string? Normalize(string path)
    {
        using var handle = CreateFile(path, FILE_READ_ATTRIBUTES, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                                      IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (handle.IsInvalid) return null;
        return FinalPath(handle, VOLUME_NAME_GUID) ?? FinalPath(handle, VOLUME_NAME_DOS);
    }

    /// <summary>正規化したパスの SHA-256（16 進・大文字）。String.GetHashCode は実行ごとに値が変わりうるので使わない。</summary>
    public static string Hash(string normalized)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized.ToUpperInvariant()));
        var text = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) text.Append(b.ToString("X2"));
        return text.ToString();
    }

    /// <summary>画面の排他。Global\ に置いて、ほかのログオンセッションとも共有する。</summary>
    public static string UiLockName(string normalized) => @"Global\ReTAC.Updater.UI." + Hash(normalized);

    /// <summary>置き換えの排他。</summary>
    public static string ReplaceLockName(string normalized) => @"Global\ReTAC.Updater.Replace." + Hash(normalized);

    private static string? FinalPath(SafeFileHandle handle, uint volumeFlag)
    {
        var buffer = new StringBuilder(1024);
        while (true)
        {
            var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, FILE_NAME_NORMALIZED | volumeFlag);
            if (length == 0) return null;
            if (length < buffer.Capacity) return buffer.ToString();
            buffer.Capacity = (int)length + 1;
        }
    }

    private const uint FILE_READ_ATTRIBUTES = 0x80;
    private const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, FILE_SHARE_DELETE = 4;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FILE_NAME_NORMALIZED = 0, VOLUME_NAME_DOS = 0, VOLUME_NAME_GUID = 1;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
                                                    uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetFinalPathNameByHandleW")]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
}
