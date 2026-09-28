using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ReTAC.Updater.Core;

namespace ReTAC.Updater;

/// <summary>終了依頼への返事。</summary>
internal enum QuitReply
{
    Accepted,
    Refused,
    NoResponse,
}

/// <summary>インストール先の ReTAC.exe から起動したプロセス。</summary>
internal sealed class ReTacProcess
{
    public ReTacProcess(int id, bool pathKnown)
    {
        Id = id;
        PathKnown = pathKnown;
    }

    public int Id { get; }

    /// <summary>パスを読めたか。読めない（権限が足りない等）ものも名前が ReTAC なら数える。</summary>
    public bool PathKnown { get; }
}

/// <summary>
/// R-109-3: インストール先の ReTAC を数え、終了を頼む。<b>強制終了はしない。</b>
/// 送り先は目印のプロパティ（Protocol.WindowPropName）のあるトップレベルのウィンドウ 1 枚。ReTAC はどのウィンドウが受けても
/// プロセス全体への依頼として扱う。SendMessageTimeout（5 秒・SMTO_ABORTIFHUNG）で送り、固まった ReTAC に待たされない。
/// </summary>
internal static class ReTacProcesses
{
    private static readonly uint QuitMessage = RegisterWindowMessage(Protocol.MessageName);

    /// <summary>インストール先の ReTAC.exe から起動したプロセス。パスは正規化して比べ、別の場所の ReTAC は数えない。</summary>
    public static List<ReTacProcess> Find(string normalizedInstall)
    {
        var found = new List<ReTacProcess>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Protocol.ReTacExe)))
        {
            using (process)
            {
                var path = ImagePath(process.Id);
                if (path is null)
                {
                    found.Add(new ReTacProcess(process.Id, pathKnown: false));
                    continue;
                }
                var folder = InstallFolder.Normalize(Path.GetDirectoryName(path)!);
                if (string.Equals(folder, normalizedInstall, StringComparison.OrdinalIgnoreCase))
                    found.Add(new ReTacProcess(process.Id, pathKnown: true));
            }
        }
        return found;
    }

    public static bool IsRunning(int id)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return true; }   // 状態を読めないが、居る
    }

    public static QuitReply RequestQuit(int id)
    {
        if (QuitMessage == 0) return QuitReply.NoResponse;
        var window = MarkedWindow(id);
        if (window == IntPtr.Zero) return QuitReply.NoResponse;

        var sent = SendMessageTimeout(window, QuitMessage, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 5000, out var result);
        if (sent == IntPtr.Zero) return QuitReply.NoResponse;
        return (long)result switch
        {
            Protocol.Accepted => QuitReply.Accepted,
            Protocol.Refused => QuitReply.Refused,
            _ => QuitReply.NoResponse,
        };
    }

    private static IntPtr MarkedWindow(int id)
    {
        var target = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner == id && GetProp(window, Protocol.WindowPropName) != IntPtr.Zero)
            {
                target = window;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return target;
    }

    private static string? ImagePath(int id)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, id);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(1024);
            var length = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref length) ? buffer.ToString() : null;
        }
        finally { CloseHandle(handle); }
    }

    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetPropW")]
    private static extern IntPtr GetProp(IntPtr window, string name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam,
                                                    uint flags, uint timeout, out IntPtr result);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int id);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "QueryFullProcessImageNameW")]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
