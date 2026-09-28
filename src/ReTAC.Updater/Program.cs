using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ReTAC.Updater.Core;

namespace ReTAC.Updater;

/// <summary>
/// R-109-1: アップデータの起動。ReTAC のヘルプメニューから（引数はインストール先）、または単体でダブルクリックして（引数なし）。
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == Protocol.ReplaceSwitch) return ElevatedReplace.Run(args);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var options = Options.Parse(args);
        var self = Application.ExecutablePath;
        var selfFolder = Path.GetDirectoryName(self)!;

        // 1. インストール先。引数が無ければ（単体実行）自分のあるフォルダ
        var install = options.Install ?? selfFolder;
        var normalized = InstallFolder.Normalize(install);
        if (normalized is null)
        {
            Show("フォルダが見つかりません。" + Environment.NewLine + install);
            return 1;
        }

        // 2. インストール先の中で動いていれば、一時フォルダへ自分をコピーして起動し直す（インストール先の自分を置き換えるため）
        if (string.Equals(InstallFolder.Normalize(selfFolder), normalized, StringComparison.OrdinalIgnoreCase))
            return Relocate(self, install, options);

        var work = IsWorkFolder(selfFolder) ? selfFolder : CreateWorkFolder();

        // 3. 画面の排他。放棄されていても取れたものとして扱う（ファイル操作とは関係しない）
        using var ui = NamedLock.TryAcquire(InstallFolder.UiLockName(normalized));
        if (ui is null)
        {
            // 先に開いている画面があれば、それを前面に出すだけにする（メッセージを重ねない）
            if (!ActivateOtherWindow()) Show("アップデータがすでに動いています。");
            return 1;
        }

        Application.Run(new UpdaterForm(install, normalized, work, self, options, ui));
        return 0;
    }

    private static int Relocate(string self, string install, Options options)
    {
        try
        {
            var work = CreateWorkFolder();
            var copy = Path.Combine(work, Protocol.UpdaterExe);
            File.Copy(self, copy);
            var start = new ProcessStartInfo(copy, Options.Join(new[] { install }.Concat(options.DebugArgs)))
            {
                UseShellExecute = false,
                WorkingDirectory = work,
            };
            Process.Start(start)?.Dispose();
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Show("アップデータを一時フォルダへ移せませんでした。" + Environment.NewLine + ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// 一時フォルダの中に、今回の作業フォルダを新しく作る。名前は GUID で、既にあれば作り直す。
    /// 前回の作業フォルダは消さない（アップデータの持ち物だと証明できないため。残るのはアップデータの exe 1 つ分だけ）。
    /// </summary>
    private static string CreateWorkFolder()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var path = Path.Combine(Path.GetTempPath(), WorkPrefix + Guid.NewGuid().ToString("N"));
            if (CreateDirectory(path, IntPtr.Zero)) return path;
        }
        throw new IOException("一時フォルダを作れませんでした。");
    }

    private const string WorkPrefix = "ReTAC-update-";

    private static bool IsWorkFolder(string folder) =>
        Path.GetFileName(folder).StartsWith(WorkPrefix, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Path.GetDirectoryName(folder)?.TrimEnd('\\'), Path.GetTempPath().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// ほかのアップデータの画面（題が「ReTAC の更新」）が 1 つだけ見えていれば前面に出す。
    /// 見えない（画面を閉じた後も置き換えが続いている）・2 つ以上ある（別のフォルダの更新）ときは出さない。
    /// </summary>
    private static bool ActivateOtherWindow()
    {
        var self = Process.GetCurrentProcess().Id;
        var found = new System.Collections.Generic.List<IntPtr>();
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner != self && IsWindowVisible(window) && WindowText(window) == UpdaterForm.Title)
            {
                try
                {
                    using var process = Process.GetProcessById(owner);
                    if (string.Equals(process.ProcessName, Path.GetFileNameWithoutExtension(Protocol.UpdaterExe), StringComparison.OrdinalIgnoreCase))
                        found.Add(window);
                }
                catch (ArgumentException) { }
            }
            return true;
        }, IntPtr.Zero);

        if (found.Count != 1) return false;
        if (IsIconic(found[0])) ShowWindow(found[0], SW_RESTORE);
        SetForegroundWindow(found[0]);
        return true;
    }

    private static string WindowText(IntPtr window)
    {
        var text = new System.Text.StringBuilder(256);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    private const int SW_RESTORE = 9;

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int length);

    private static void Show(string message) =>
        MessageBox.Show(message, "ReTAC の更新", MessageBoxButtons.OK, MessageBoxIcon.Information);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateDirectoryW")]
    private static extern bool CreateDirectory(string path, IntPtr security);
}
