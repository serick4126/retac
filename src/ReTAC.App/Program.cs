using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using ReTAC.App.Rendering;
using ReTAC.Domain.Listing;
using SortOrder = ReTAC.Domain.Listing.SortOrder;

namespace ReTAC.App;

internal static class Program
{
    /// <summary>R-108: 起動時の配色モード。設定で切り替えても、再起動するまではこれで描く。</summary>
    internal static Rendering.ColorMode StartupColorMode { get; private set; } = Rendering.ColorMode.System;

    /// <summary>R-108: 起動時の OS の配色の状態。テストなど Main を通らないときは、最初に読んだ時点の状態になる。</summary>
    internal static Rendering.OsTheme StartupOs { get; private set; } = Rendering.OsTheme.Capture();

    [STAThread]
    private static int Main(string[] args)
    {
        // F-03: 「終了後もウィンドウを閉じない」のコンソール。WinForms を初期化しない
        if (args is [ConsoleHold.Switch, var holdExe, .. var holdArgs]) return ConsoleHold.Run(holdExe, holdArgs);

        ApplicationConfiguration.Initialize();

        // 常駐して作業状態（とりわけマーク）を保つことが本システムの存在理由（R-40-2）。
        // 未処理例外でプロセスが消えると、それが失われる。表示の更新やフォルダを開く処理は
        // fire-and-forget（`_ = OpenFolderAsync(...)`）なので、想定外の例外型はそのまま
        // UI スレッドの未処理例外になる。6 章の方針どおり、提示して動き続ける（V-03）
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowUnexpected(null, e.Exception);
        // R-126: 画面以外のスレッドの例外は止められない（このあとプロセスが終わる）。終わる前に、同期で記録を書き切る
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception) ErrorLog.Write(exception, "background");
        };
        // R-126: 待たれずに捨てられたタスクの例外。画面には出さず、記録だけ残す
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ErrorLog.Write(e.Exception, "task");
            Volatile.Write(ref s_unobservedLogged, 1);
        };
        // R-126: サムネイルの背景のスレッドが受け止めた想定外の例外（画面には出さない）
        // 同じ例外を項目の数だけ記録しない（壊れたシェル拡張のあるフォルダでは、数千件が同じ例外を出す）
        ReTAC.Shell.ShellImageWorker.UnexpectedError = exception => ErrorLog.WriteOnce(exception, "thumbnail");

#if DEBUG
        // R-126: 記録を実機とテストで確かめるための、わざと例外を起こす入口（開発用のビルドだけ）
        if (args is ["--throw", var kind, .. var rest]) return Throw(kind, rest.FirstOrDefault());
#endif

        // 開発環境のスクリーンがロックされていると CopyFromScreen が使えない。
        // 画面の確認はライブのウィンドウを撮るのではなくオフスクリーン描画で行う（--shot）。
        if (args is ["--shot", var folder, var outPath, .. var options]) return Shot(folder, outPath, options);
        if (args is ["--bench", var benchPath, ..]) return Bench(benchPath);

        var settings = AppSettings.Load();
        // R-108: 最初のウィンドウより前に決める。後から変えても、作ったコントロールの色は変わらない。
        // 独自の配色でも呼ぶ。独自の配色が当たるのはファイルリストだけで、ほかは OS に従う（T2）。
        // ハイコントラストでは WinForms の既定（OS の色そのもの）のままにする（R-108-3）
        StartupColorMode = settings.ColorMode;
        if (!SystemInformation.HighContrast) Application.SetColorMode(SystemColorMode.System);
        StartupOs = Rendering.OsTheme.Capture();
        // Q10: 正規化の失敗で起動を止めない。null の補正は先に済むので、残るのは消えたツールの参照だけ
        try { settings.Normalize(); }
        catch (Exception ex) { Debug.WriteLine(ex); ErrorLog.Write(ex, "normalize"); }
        var form = new MainForm(settings: settings);

        // R-40-6: 起動時はウィンドウを表示しない設定
        if (settings.StartMinimized) form.WindowState = FormWindowState.Minimized;

        form.Shown += async (_, _) => await form.OpenFolderAsync(StartFolder(args, settings));

        // B-03: メッセージループを特定のウィンドウに紐づけない。Run(form) だと
        // その 1 枚を閉じた時点で他のウィンドウも道連れになる。
        // 終了は MainForm 側（最後の 1 枚が閉じたら ExitThread）に任せる
        form.Show();
        Application.Run(new ApplicationContext());
        return 0;
    }

    /// <summary>捨てられたタスクの例外を記録し終えた（開発用の --throw task が、記録が済むのを待つのに使う）。</summary>
    private static int s_unobservedLogged;

    /// <summary>R-126 / V-03: 想定外の例外を記録してから、メッセージで知らせて動き続ける。</summary>
    internal static void ShowUnexpected(IWin32Window? owner, Exception exception) =>
        MessageBox.Show(owner, ErrorLog.Message(exception, ErrorLog.Write(exception, "ui")), "ReTAC",
            MessageBoxButtons.OK, MessageBoxIcon.Warning);

#if DEBUG
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint mode);

    private static int Throw(string kind, string? logFolder)
    {
        if (logFolder is not null) ErrorLog.FolderOverride = logFolder;
        SetErrorMode(0x0002);   // SEM_NOGPFAULTERRORBOX: 異常終了の OS の画面で止まらない（テストが待ち続けないように）
        switch (kind)
        {
            case "background":
                var thread = new Thread(() => throw new InvalidOperationException("retac-throw-background"));
                thread.Start();
                thread.Join();
                return 0;   // ここへは来ない（プロセスが終わる）
            case "task":
                FaultedTask();
                // 捨てられたタスクの例外は、タスクが回収されて最終化されたときに知らされる。1 回の回収で済む保証は無いので、
                // 回数を決め打ちにせず、記録が済むまで（最長 10 秒）繰り返す
                for (var i = 0; i < 100 && Volatile.Read(ref s_unobservedLogged) == 0; i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Thread.Sleep(100);
                }
                return 0;
            default:   // "ui": 画面のスレッドの例外。メッセージを閉じ、ウィンドウを閉じると終わる
                var form = new Form { Text = "ReTAC --throw ui" };
                form.Shown += (_, _) => throw new InvalidOperationException("retac-throw-ui");
                Application.Run(form);
                return 0;
        }
    }

    /// <summary>別のメソッドにして、タスクへの参照を残さない（開発用のビルドでは、局所変数がメソッドの終わりまで生きる）。</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void FaultedTask()
    {
        var task = Task.Run(() => throw new InvalidOperationException("retac-throw-task"));
        ((IAsyncResult)task).AsyncWaitHandle.WaitOne();
    }
#endif

    /// <summary>
    /// R-26: 引数 → 前回終了時のフォルダ（保持する設定のとき） → 固定の起動フォルダ →
    /// 実行ファイルの所在フォルダ、の順に決める。
    /// </summary>
    private static string StartFolder(string[] args, AppSettings settings)
    {
        if (args.Length > 0 && Directory.Exists(args[0])) return args[0];
        if (settings.KeepLastFolder && Directory.Exists(settings.LastFolder)) return settings.LastFolder!;
        if (Directory.Exists(settings.StartFolder)) return settings.StartFolder!;
        return AppContext.BaseDirectory;
    }

    /// <summary>
    /// ウィンドウを画面に出さずに 1 枚描いて PNG に保存する。OnPaint と同じ経路を通る。
    /// 使い方: <c>ReTAC.App.exe --shot &lt;フォルダ&gt; &lt;出力.png&gt; [size=1200x700] [cursor=3] [mark=2,3,5]</c>
    /// </summary>
    private static int Shot(string folder, string outPath, string[] options)
    {
        using var form = new MainForm(ParseSize(Option(options, "size"))) { ShowInTaskbar = false };
        form.Show();
        form.OpenFolderSync(folder);

        if (Option(options, "mark") is { } marks)
            foreach (var index in ParseIndices(marks)) form.List.State.ToggleMark(index);
        if (Option(options, "cursor") is { } cursor && int.TryParse(cursor, out var cursorIndex))
            form.List.MoveCursorTo(cursorIndex);   // スクロールも追従させる

        form.RefreshStatus();   // State を直接いじったので明示的に更新する
        form.Refresh();
        Application.DoEvents();

        // DrawToBitmap は非クライアント領域（タイトルバー・枠）も描くので、
        // ClientSize ではなくウィンドウ全体の大きさで受ける。でないと下端が切れる
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
        bitmap.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);

        Console.WriteLine($"saved {outPath}  ({form.ClientSize.Width}x{form.ClientSize.Height} / {form.List.State.Count} 件)");
        foreach (Control c in form.Controls) Console.WriteLine($"  {c.GetType().Name,-14} dock={c.Dock,-6} bounds={c.Bounds}");
        return 0;
    }

    private static int Bench(string path)
    {
        var sort = Stopwatch.StartNew();
        var entries = FolderEnumerator.Enumerate(path, SortOrder.Default);
        sort.Stop();

        using var font = new Font(Theme.Default.FontFamily, Theme.Default.FontSize);
        using var measure = new TextMeasure(font, 96f);
        var layout = Stopwatch.StartNew();
        var computed = EntryMetrics.Layout(entries, measure, 1200, 700, 17, 16, 4, 2, 4, null);
        layout.Stop();

        Console.WriteLine($"{path}");
        Console.WriteLine($"  件数      : {entries.Count}");
        Console.WriteLine($"  列挙+ソート: {sort.ElapsedMilliseconds} ms");
        Console.WriteLine($"  列幅決定   : {layout.ElapsedMilliseconds} ms  (列幅 {computed.ColumnWidth}px / {computed.ColumnCount} 列)");
        return 0;
    }

    private static string? Option(string[] options, string name) =>
        options.FirstOrDefault(o => o.StartsWith(name + '=', StringComparison.OrdinalIgnoreCase))?[(name.Length + 1)..];

    private static Size ParseSize(string? text)
    {
        var parts = (text ?? "").Split('x', 'X');
        return parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h)
            ? new Size(w, h)
            : new Size(1200, 700);
    }

    private static IEnumerable<int> ParseIndices(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s, out var i) ? i : -1)
            .Where(i => i >= 0);
}
