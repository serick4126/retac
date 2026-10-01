using System.Runtime.InteropServices;

namespace ReTAC.Shell;

/// <summary>
/// R-131: ファイル参照。<c>GetOpenFileNameW</c> を持ち主のハンドルで直接呼ぶ。
/// WinForms の OpenFileDialog を通さないのは、ShowDialog が同じ画面スレッドの全トップレベルウィンドウを無効にし、
/// ほかの ReTAC ウィンドウまで操作できなくなるため。ネイティブの持ち主の無効化は持ち主だけに効く（ShellFolderBrowser と同じ）。
/// </summary>
public static class ShellFileBrowser
{
    /// <returns>選ばれたパス。取り消されたら null。</returns>
    public static string? Select(IntPtr owner, string initialDirectory, string fileName, string title)
    {
        // 長いパスでも切れないよう、バッファは 32768 文字（MAX_PATH に縛られない）
        var buffer = Marshal.AllocHGlobal(MaxPathLong * sizeof(char));
        try
        {
            // 初期のファイル名を入れる。確保したままの領域は中身が不定なので、全体を 0 で埋めて終端を保証する
            var chars = new char[MaxPathLong];
            fileName.AsSpan(0, Math.Min(fileName.Length, MaxPathLong - 1)).CopyTo(chars);
            Marshal.Copy(chars, 0, buffer, MaxPathLong);

            var info = new OPENFILENAME
            {
                lStructSize = Marshal.SizeOf<OPENFILENAME>(),
                hwndOwner = owner,
                lpstrFile = buffer,
                nMaxFile = MaxPathLong,
                lpstrInitialDir = initialDirectory,
                lpstrTitle = title,
                // OFN_NOCHANGEDIR: 選んだフォルダへカレントディレクトリを移さない（ReTAC のカレントは別に持つ）
                Flags = OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_LONGNAMES,
            };
            // 取り消し（と失敗）は false。どちらも null で返す
            return GetOpenFileNameW(ref info) ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private const int MaxPathLong = 32768;

    private const uint OFN_FILEMUSTEXIST = 0x00001000;
    private const uint OFN_PATHMUSTEXIST = 0x00000800;
    private const uint OFN_NOCHANGEDIR = 0x00000008;
    private const uint OFN_EXPLORER = 0x00080000;
    private const uint OFN_LONGNAMES = 0x00200000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAME
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrFilter;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrFileTitle;
        public int nMaxFileTitle;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrInitialDir;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrTitle;
        public uint Flags;
        public short nFileOffset;
        public short nFileExtension;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public uint FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileNameW(ref OPENFILENAME info);
}
