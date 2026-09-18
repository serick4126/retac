using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using ReTAC.App.Rendering;
using ReTAC.Domain.Listing;
using SortOrder = ReTAC.Domain.Listing.SortOrder;

namespace ReTAC.App;

internal static class Program
{
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
        Application.ThreadException += (_, e) =>
            MessageBox.Show(e.Exception.Message, "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        // 開発環境のスクリーンがロックされていると CopyFromScreen が使えない。
        // 画面の確認はライブのウィンドウを撮るのではなくオフスクリーン描画で行う（--shot）。
        if (args is ["--shot", var folder, var outPath, .. var options]) return Shot(folder, outPath, options);
        if (args is ["--bench", var benchPath, ..]) return Bench(benchPath);

        var settings = AppSettings.Load();
        settings.Normalize();   // Q10
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
        var computed = EntryMetrics.Layout(entries, measure, 700, 16, 4, 2, 4);
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
