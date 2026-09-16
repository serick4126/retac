using System.Runtime.InteropServices;
using System.Text;

namespace ReTAC.Shell;

/// <summary>
/// R-52: フォルダ参照ツリー。Windows 標準の <c>SHBrowseForFolder</c>（新 UI 版）を直接呼ぶ。
/// WinForms の FolderBrowserDialog を通さないのは、<b>コールバックを取るため</b>である。
///
/// B-10 / R-52-5: ツリーは <c>BFFM_SETSELECTION</c> のあと非同期に組み上がる。組み上がる途中を見せず、
/// 初期選択に届いたことを確かめてから <b>1 回だけ</b>整え（1 段展開・祖先を上に・下に 2 行の余白）、それから見せる。
/// 以前は表示したままタイマーで 6 回整え直しており、辿る様子とちらつきが見えていた。
///
/// V-16: 上部のパス入力欄（BIF_EDITBOX）は持たない。理由はコールバックの中に書いた。
/// </summary>
public static class ShellFolderBrowser
{
    /// <param name="initialPath">R-52-2: 初期選択。存在しなければ <paramref name="fallbackPath"/></param>
    /// <returns>選ばれたパス。取り消されたら null。</returns>
    public static string? Select(IntPtr owner, string? initialPath, string fallbackPath, string title)
    {
        var selection = Directory.Exists(initialPath) ? initialPath! : fallbackPath;
        var selectionPtr = Marshal.StringToCoTaskMemUni(selection);

        // BFFM_SELCHANGED が最後に知らせた選択。初期選択と一致すれば、ツリーがそこまで組み上がっている
        string? selected = null;
        var reachedOnce = false;
        var ticks = 0;

        TimerProc tick = (dialog, _, timerId, _) =>
        {
            var reached = selected is not null && SamePath(selected, selection);
            var giveUp = ++ticks >= ArrangeRetries;

            // 届いた直後のティックはまだ子の列挙が走っていることがある。1 ティックだけ待つ
            if (reached && !reachedOnce && !giveUp) { reachedOnce = true; return; }
            if (!reached && !giveUp) return;

            KillTimer(dialog, timerId);
            var tree = FindTree(dialog);
            if (reached && tree != IntPtr.Zero) Arrange(tree);

            // 届かなくても必ず見せる。透明のまま残ると、操作できない不可視のモーダルになる
            Reveal(dialog);

            // 既定では下部のボタンにフォーカスが当たっており、↑ や ← でツリーを辿れない。
            // R-52-3 は「キーボードだけで完結すること」。ツリーへ渡す
            if (tree != IntPtr.Zero) SetFocus(tree);
        };

        var callback = new BrowseCallback((dialog, message, parameter, data) =>
        {
            // 選択が変わっても入力欄へは書かない。
            // かつてはここでフルパスを入力欄へ書いていたが（R-52 の「上部にパス入力欄」）、
            // SELCHANGED はツリーの選択より必ず遅れて届くため、↓ を押してすぐ Enter すると
            // 入力欄が追いつく前に OK が走り、BIF_EDITBOX があるとダイアログは
            // ツリーの選択ではなく古い入力欄の値を返した（V-16・実機再現）。
            // 入力欄そのものを外してツリーの選択を返させる。パスの手入力は、
            // このツリーを開く元のパス入力欄（N-07: Shift+Enter）で足りる。
            switch (message)
            {
                case BFFM_INITIALIZED:
                    // B-10 / M-1: タイマーが取れた時だけ透明にする。取れなければ Reveal が来ないので
                    // 隠したまま操作できない不可視モーダルになってしまう
                    if (SetTimer(dialog, (IntPtr)1, ArrangeInterval, tick) != IntPtr.Zero) Conceal(dialog);
                    // wParam = TRUE はパス文字列で指定する意（PIDL ではなく）
                    SendMessage(dialog, BFFM_SETSELECTIONW, (IntPtr)1, data);
                    break;
                case BFFM_SELCHANGED:
                    selected = PathOf(parameter);
                    break;
            }
            return 0;
        });

        var info = new BROWSEINFO
        {
            hwndOwner = owner,
            lpszTitle = title,
            ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE,
            lpfn = callback,
            lParam = selectionPtr,
        };

        var pidl = IntPtr.Zero;
        try
        {
            pidl = SHBrowseForFolder(ref info);
            return PathOf(pidl);
        }
        finally
        {
            if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
            Marshal.FreeCoTaskMem(selectionPtr);
            GC.KeepAlive(callback);
            GC.KeepAlive(tick);
        }
    }

    /// <summary>
    /// 初期選択を整える。見せる前に 1 回だけ呼ぶ。
    /// 1 段展開 → 祖先を先頭に → 下に余白を持つ位置まで送る → 選択そのものが見えることを最後に保証する。
    /// </summary>
    private static void Arrange(IntPtr tree)
    {
        var item = SendMessage(tree, TVM_GETNEXTITEM, (IntPtr)TVGN_CARET, IntPtr.Zero);
        if (item == IntPtr.Zero) return;

        // サブフォルダへ移すことが多いので、選択の下を 1 段開く。子が無ければ何も起きない
        SendMessage(tree, TVM_EXPAND, (IntPtr)TVE_EXPAND, item);

        // 選択そのものを先頭に置くと親が画面外に出て現在地が掴めない。数階層さかのぼった祖先を先頭にする
        var top = item;
        for (var i = 0; i < AncestorsAbove; i++)
        {
            var parent = SendMessage(tree, TVM_GETNEXTITEM, (IntPtr)TVGN_PARENT, top);
            if (parent == IntPtr.Zero) break;
            top = parent;
        }
        SendMessage(tree, TVM_SELECTITEM, (IntPtr)TVGN_FIRSTVISIBLE, top);

        // 最下段は見づらい。表示順で 2 つ下の項目まで見えるようにする。
        // 展開後の表示順なので、子があれば子、無ければ同じ階層の次のフォルダが余白になる
        var below = item;
        for (var i = 0; i < RowsBelowSelection; i++)
        {
            var next = SendMessage(tree, TVM_GETNEXTITEM, (IntPtr)TVGN_NEXTVISIBLE, below);
            if (next == IntPtr.Zero) break;
            below = next;
        }
        SendMessage(tree, TVM_ENSUREVISIBLE, IntPtr.Zero, below);

        // ツリーが低くて祖先と余白が両立しないときは、選択が見えることを優先する
        SendMessage(tree, TVM_ENSUREVISIBLE, IntPtr.Zero, item);
    }

    private static void Conceal(IntPtr dialog)
    {
        var style = GetWindowLongPtr(dialog, GWL_EXSTYLE);
        SetWindowLongPtr(dialog, GWL_EXSTYLE, style | WS_EX_LAYERED);
        SetLayeredWindowAttributes(dialog, 0, 0, LWA_ALPHA);
    }

    /// <summary>レイヤードを外すと元の不透明なウィンドウに戻る。外した直後は描き直させる。</summary>
    private static void Reveal(IntPtr dialog)
    {
        var style = GetWindowLongPtr(dialog, GWL_EXSTYLE);
        SetWindowLongPtr(dialog, GWL_EXSTYLE, style & ~WS_EX_LAYERED);
        RedrawWindow(dialog, IntPtr.Zero, IntPtr.Zero, RDW_ERASE | RDW_INVALIDATE | RDW_FRAME | RDW_ALLCHILDREN);
    }

    private static string? PathOf(IntPtr pidl)
    {
        if (pidl == IntPtr.Zero) return null;
        var buffer = new StringBuilder(MaxPathLong);
        return SHGetPathFromIDListEx(pidl, buffer, buffer.Capacity, 0) ? buffer.ToString() : null;
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b),
                      StringComparison.OrdinalIgnoreCase);

    private static IntPtr FindTree(IntPtr dialog) => FindDescendant(dialog, "SysTreeView32");

    /// <summary>
    /// 新 UI 版では目的のコントロールがダイアログの直接の子とは限らない。
    /// FindWindowEx は直接の子しか見ないので、子孫まで辿る。
    /// </summary>
    private static IntPtr FindDescendant(IntPtr parent, string className)
    {
        var child = IntPtr.Zero;
        while ((child = FindWindowEx(parent, child, null, null)) != IntPtr.Zero)
        {
            var name = new StringBuilder(64);
            GetClassName(child, name, name.Capacity);
            if (name.ToString() == className) return child;

            var found = FindDescendant(child, className);
            if (found != IntPtr.Zero) return found;
        }
        return IntPtr.Zero;
    }

    /// <summary>SHGetPathFromIDListEx はバッファ長を取るので 260 文字に縛られない。</summary>
    private const int MaxPathLong = 32768;

    private const uint BIF_RETURNONLYFSDIRS = 0x0001;
    private const uint BIF_NEWDIALOGSTYLE = 0x0040;   // 新 UI 版。フォルダ作成ボタンとリサイズが付く

    private const uint BFFM_INITIALIZED = 1;
    private const uint BFFM_SELCHANGED = 2;
    private const uint BFFM_SETSELECTIONW = 0x0400 + 103;

    /// <summary>
    /// 初期選択への到達を確かめる間隔と回数。合計 1.2 秒で諦めて、その時点の状態で見せる。
    /// 隠している間は利用者を待たせるので、間隔を短くして届いたらすぐ見せる
    /// </summary>
    private const uint ArrangeInterval = 50;
    private const int ArrangeRetries = 24;

    /// <summary>選択の下に見せる行数（R-52-5）。実機で見て調整する。</summary>
    private const int RowsBelowSelection = 2;

    private const uint TVM_GETNEXTITEM = 0x1100 + 10;
    private const uint TVM_SELECTITEM = 0x1100 + 11;
    private const uint TVM_EXPAND = 0x1100 + 2;
    private const int TVGN_PARENT = 3;
    private const int TVGN_FIRSTVISIBLE = 5;
    private const int TVGN_NEXTVISIBLE = 6;
    private const int TVE_EXPAND = 2;

    /// <summary>選択の上に何階層見せるか。卓駆はこのくらい祖先が見えている。</summary>
    private const int AncestorsAbove = 3;
    private const uint TVM_ENSUREVISIBLE = 0x1100 + 20;
    private const int TVGN_CARET = 9;
    private const int GWL_EXSTYLE = -20;
    private const nint WS_EX_LAYERED = 0x00080000;
    private const uint LWA_ALPHA = 0x2;
    private const uint RDW_INVALIDATE = 0x1;
    private const uint RDW_ERASE = 0x4;
    private const uint RDW_ALLCHILDREN = 0x80;
    private const uint RDW_FRAME = 0x400;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int BrowseCallback(IntPtr dialog, uint message, IntPtr parameter, IntPtr data);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void TimerProc(IntPtr window, uint message, IntPtr timerId, uint time);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BROWSEINFO
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public IntPtr pszDisplayName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszTitle;
        public uint ulFlags;
        public BrowseCallback lpfn;
        public IntPtr lParam;
        public int iImage;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHBrowseForFolder(ref BROWSEINFO info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHGetPathFromIDListEx(IntPtr pidl, StringBuilder path, int length, uint options);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int length);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr SetTimer(IntPtr window, IntPtr timerId, uint interval, TimerProc callback);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool KillTimer(IntPtr window, IntPtr timerId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(IntPtr window, int index, nint value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr window, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(IntPtr window, IntPtr updateRect, IntPtr updateRegion, uint flags);
}
