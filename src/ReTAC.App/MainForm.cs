using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Entries;
using ReTAC.Domain.FileOps;
using ReTAC.Domain.Formatting;
using ReTAC.Domain.Keys;
using ReTAC.Domain.Listing;
using ReTAC.Domain.Navigation;
using Selection = ReTAC.Domain.Selection;
using ReTAC.Domain.Tools;
using ReTAC.Shell;
using SortOrder = ReTAC.Domain.Listing.SortOrder;
using Keys = System.Windows.Forms.Keys;

namespace ReTAC.App;

public sealed class MainForm : Form
{
    private readonly FileListView _list = new() { Dock = DockStyle.Fill };
    private readonly DriveBar _driveBar = new();
    private readonly StatusBar _statusBar = new();
    private KeyMap _keyMap;
    private MenuStrip _menu;
    private readonly AppSettings _settings;
    private readonly FolderHistory _history;
    private readonly QuickAccessList _quickAccess;
    private readonly FileTypeFilter _fileTypes;
    /// <summary>`X` の入力欄が持つ実行履歴（16.10 節）。フォルダ履歴とは別物。</summary>
    private readonly List<string> _commandHistory = [];
    // R-67 / N-01: カレントフォルダ直下だけを監視する。属性とサイズの変化も更新の契機
    private readonly FileSystemWatcher _watcher = new()
    {
        IncludeSubdirectories = false,
        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                     | NotifyFilters.Attributes | NotifyFilters.Size | NotifyFilters.LastWrite,
    };
    // 連続した通知をまとめる。1 件ごとに開き直すと大量コピー中に描画が追いつかない
    private readonly System.Windows.Forms.Timer _autoRefresh = new() { Interval = 300 };
    private SortOrder _sortOrder;
    private string _currentFolder = "";
    /// <summary>
    /// R-77: このウィンドウでドライブバーを出しているか。_driveBar.Visible は親のフォームが
    /// 表示されていないと false を返すので、状態の判断には使わない。
    /// </summary>
    private bool _driveBarShown;
    // RebuildMenu（キー割り当ての変更）がメニュー全体を作り直すたびに差し替える。readonly にはできない
    private ToolStripMenuItem _driveBarMenuItem;
    /// <summary>R-40: 常駐中は終了操作で最小化するだけにする。完全終了だけがプロセスを終わらせる。</summary>
    private bool _fullExit;
    /// <summary>R-74: マウスボタン3/4/5 を窓全体で受ける。解除は FormClosed で行う。</summary>
    private readonly MouseButtonFilter _mouseButtons;
    /// <summary>R-80: ステータスバーの一段上。検索中だけ出す。</summary>
    private readonly IncrementalSearchBar _search;

    /// <summary>通常表示だったときのクライアント領域。最小化中に保存しても潰れないようにするため。</summary>
    private Size _normalClientSize;

    public MainForm(Size? clientSize = null, AppSettings? settings = null)
    {
        _settings = settings ?? new AppSettings();
        _search = new IncrementalSearchBar(_list);
        _keyMap = _settings.ToKeyMap();
        _history = _settings.ToFolderHistory();
        _quickAccess = _settings.ToQuickAccess();
        _fileTypes = _settings.ToFileTypeFilter();
        _sortOrder = _settings.ToSortOrder();

        Text = "ReTAC";
        // H-14: Form の既定アイコンは exe のアイコンではないので、埋め込んだ .ico を明示的に渡す
        using (var s = typeof(MainForm).Assembly.GetManifestResourceStream("ReTAC.App.retac.ico"))
        {
            if (s is not null) Icon = new Icon(s);
        }
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = clientSize ?? new Size(_settings.WindowWidth, _settings.WindowHeight);
        if (clientSize is null && _settings.WindowX >= 0 && _settings.WindowY >= 0)
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(_settings.WindowX, _settings.WindowY);
        }
        // Fill を先に足す（後から足した Dock の方が先に領域を取る）。
        // ステータスバーが一番下、検索バーはその一段上に来るよう、検索バーを先に足す
        Controls.Add(_list);
        Controls.Add(_search);
        Controls.Add(_driveBar);
        Controls.Add(_statusBar);
        _statusBar.QueueClicked += (_, _) => ShowToolQueue();
        // Dock.Top は後から足した方が上に来る。メニューはドライブバーより上
        _menu = MenuBar.Create(target => Execute(target, Keys.None), _keyMap, _settings.ExternalTools, out _driveBarMenuItem);
        Controls.Add(_menu);
        MainMenuStrip = _menu;
        _driveBarShown = _settings.ShowDriveBar;
        _driveBar.Visible = _driveBarShown;
        _driveBarMenuItem.Checked = _driveBarShown;

        _list.EntryActivated += (_, entry) => OnActivated(entry);
        _list.ParentRequested += (_, _) => GoParent();
        _list.CursorMoved += (_, _) => RefreshStatus();
        _list.MarksChanged += (_, _) => RefreshStatus();
        _list.Theme = _settings.ToTheme();                       // 5-1 節の配色とフォント
        _driveBar.SetVisibility(_settings.ToHiddenDrives(), _settings.ShowDesktopButton);   // 16.7 節
        _list.CommandKey += (_, e) => OnCommandKey(e);
        _list.RightClicked += (_, click) => OnRightClick(click);
        // R-39-3 の「明示的なドライブ変更」。相対移動とは別経路
        _driveBar.PathSelected += (_, path) => OnDriveChosen(path);
        _driveBar.Cancelled += (_, _) => _list.Focus();
        // ドライブのボタンの右クリックはリストの項目と同じ扱い。移動はしない
        _driveBar.RightClicked += (_, click) => ShowShellContextMenu([click.Path], click.ScreenPoint);
        // R-65 ②③: 落とされたファイルの転送はどちらも同じ経路を通す
        _list.FilesDropped += (_, drop) => DropInto(_currentFolder, drop.Files, drop.Allowed);
        _driveBar.FilesDropped += (_, drop) => DropInto(drop.Path, drop.Files, drop.Allowed);

        _watcher.SynchronizingObject = this;   // R-23: 通知を UI スレッドで受ける
        _watcher.Created += (_, _) => ScheduleAutoRefresh();
        _watcher.Deleted += (_, _) => ScheduleAutoRefresh();
        _watcher.Renamed += (_, _) => ScheduleAutoRefresh();
        _watcher.Changed += (_, _) => ScheduleAutoRefresh();
        _watcher.Error += (_, _) => ScheduleAutoRefresh();

        _autoRefresh.Tick += (_, _) =>
        {
            // モーダルダイアログの表示中は触らない。
            // R-8: ShowDialog は Win32 の EnableWindow で無効化するだけで、
            // Control.Enabled は true のまま。ウィンドウの状態を直接見る。
            // ここで Stop しないのが要点。待たせるだけにして、閉じた後の Tick で反映する
            if (!IsWindowEnabled(Handle)) return;

            _autoRefresh.Stop();
            Reload();
        };

        _normalClientSize = ClientSize;
        Resize += (_, _) => { if (WindowState == FormWindowState.Normal) _normalClientSize = ClientSize; };

        // R-74: マウスボタン3/4/5 は一覧・ドライブバー・ステータスバーのどこで押しても効かせる
        _mouseButtons = new MouseButtonFilter(this, PressMouseButton);
        Application.AddMessageFilter(_mouseButtons);

        // F-05: キューはプロセスで 1 本。どのウィンドウのステータスバーとタスクバーにも出す
        ToolQueueHost.Queue.Changed += OnQueueChanged;
        // タスクバーのボタンはウィンドウを見せた後にできる。HandleCreated の時点では進行バーを付けられない
        Shown += (_, _) => RefreshQueueStatus();

        FormClosed += (_, _) =>
        {
            ToolQueueHost.Queue.Changed -= OnQueueChanged;
            Application.RemoveMessageFilter(_mouseButtons);
            _watcher.Dispose();
            _autoRefresh.Dispose();

            // B-03: ループはどのウィンドウにも紐づいていない（Program.cs）。
            // 最後の 1 枚が閉じたらここでプロセスを終わらせる。
            // 自分自身が OpenForms から外れる順序に依存しないよう「他に居るか」で見る
            if (!Application.OpenForms.OfType<MainForm>().Any(f => f != this)) Application.ExitThread();
        };

        FormClosing += (_, e) =>
        {
            // R-40: 常駐中は閉じずに最小化する。作業状態（とりわけマーク）を失わないため。
            // ただし最小化した状態でもう一度閉じられたら、それは本当に終わらせたいということ
            // （卓駆と同じ。タスクバーアイコンから「ウィンドウを閉じる」を 2 回）
            //
            // R-40-7 / B-03: 常駐して残るのは最後の 1 つだけ。ウィンドウが 2 つ以上あるうちは
            // 本当に閉じる。これが無いと「2 つになった後で 1 つに戻る経路」が
            // 「完全に終了」しか無くなる
            if (_settings.Resident && !_fullExit && e.CloseReason == CloseReason.UserClosing
                && WindowState != FormWindowState.Minimized
                && Application.OpenForms.OfType<MainForm>().Count() == 1)
            {
                e.Cancel = true;
                WindowState = FormWindowState.Minimized;   // R-40-5: タスクバーには残す
                return;
            }

            // K-5: 最後の 1 枚が本当に閉じる（プロセスが終わる）ときだけ確かめる。
            // 完全終了は QuitAllCommand で確認済み。Windows の終了は止めない
            if (!_fullExit && e.CloseReason != CloseReason.WindowsShutDown
                && Application.OpenForms.OfType<MainForm>().Count() == 1
                && !ConfirmQueueBeforeExit())
            {
                e.Cancel = true;
                return;
            }
            SaveSettings();
        };
    }

    /// <summary>6 章: 応答しないドライブを待つ上限。</summary>
    public static TimeSpan EnumerationTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public FileListView List => _list;
    public DriveBar DriveBar => _driveBar;
    public string CurrentFolder => _currentFolder;

    private void OnActivated(Entry entry)
    {
        // R-33-2 / R-39-3: 相対移動。ドライブをまたがない
        if (entry.IsParent) { GoParent(); return; }
        if (entry.Kind == EntryKind.Folder) { _ = OpenFolderAsync(entry.FullPath); return; }
        OpenWithAssociation(entry);
    }

    /// <summary>ファイルを開く（0x82DC）。シェルの関連付けに委ねる。</summary>
    private void OpenWithAssociation(Entry entry)
    {
        try
        {
            // 秀丸などは 260 超のパスを開けない。8.3 名に落として渡す
            Process.Start(new ProcessStartInfo(ToolLauncher.TargetPath(entry.FullPath))
            {
                UseShellExecute = true,
                // R-54-2: 常にカレントフォルダ。260 超では 8.3 名に落ちる（ToolLauncher の注記）
                WorkingDirectory = ToolLauncher.WorkingDirectoryFor(_currentFolder),
            });
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            // 6 章: エラーを提示し、ReTAC 本体は継続動作する
            MessageBox.Show(this, ex.Message, "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ---- キーマップ経由のコマンド（R-12: 解決の経路はここ 1 つ） -----------

    private void OnCommandKey(KeyEventArgs e)
    {
        // 5-3 節の固定キー。キーマップでは変更できない
        switch (e.KeyCode)
        {
            // テンキー 1〜9 が A: 〜 I:（R-32。フォーカス位置に関係なく効く）
            case >= Keys.NumPad1 and <= Keys.NumPad9:
                e.Handled = GoDrive(e.KeyCode - Keys.NumPad0);
                return;
            // ¥（JIS では VK_OEM_5）でルートへ
            case Keys.Oem5 or Keys.OemBackslash:
                e.Handled = GoRoot();
                return;
            // アプリケーションキー。マークがあればマーク集合、無ければカーソルの 1 件が対象
            case Keys.Apps:
                e.Handled = ShowShellContextMenu(_list.PointToScreen(_list.PopupAnchor()));
                return;
        }

        if (e.Control)
        {
            // R-25: Ctrl+C / X / V は標準の切り取り・コピー・貼り付け（T6-16）。キー割り当てでは変えられない
            switch (e.KeyCode)
            {
                case Keys.C: e.Handled = ClipboardPut(cut: false); return;
                case Keys.X: e.Handled = ClipboardPut(cut: true); return;
                case Keys.V: e.Handled = ClipboardPaste(); return;
            }
            // Ctrl+Shift・Ctrl+Alt の枠は無い（F-06）
            if (e.Shift || e.Alt) return;
        }
        else if (e.Alt)
        {
            return;
        }

        var binding = new KeyBinding((ushort)e.KeyCode, Shift: e.Shift && !e.Control, Ctrl: e.Control);
        if (_keyMap.Resolve(binding) is not { } target)
        {
            // 卓駆: Shift+英字でその頭文字の項目へ。キーマップに無いときだけ効く
            if (!e.Control && e.Shift && e.KeyCode is >= Keys.A and <= Keys.Z) e.Handled = JumpToInitial((char)e.KeyCode);
            return;
        }
        e.Handled = Execute(target, e.KeyCode);
    }

    /// <summary>
    /// R-74: マウスボタンの押下。キーと同じく <see cref="KeyMap.Resolve"/> の 1 経路で解決する（R-12）。
    /// R-75: カーソルは動かさない。対象はキーを押したときと同じ（マークがあればマーク集合、
    /// 無ければカーソル位置の 1 件）。クリックした場所は対象の決定に使わない。
    /// </summary>
    /// <returns>割り当てがあって実行したら true。false ならメッセージをそのまま流す</returns>
    private bool PressMouseButton(ushort virtualKey)
    {
        if (_keyMap.Resolve(new KeyBinding(virtualKey)) is not { } target) return false;
        return Execute(target, (Keys)virtualKey);
    }

    /// <summary>F-06: キーやメニューが指す先を実行する。種類を足したらここに 1 行足す。</summary>
    private bool Execute(CommandTarget target, Keys key) => target switch
    {
        BuiltinTarget builtin => Execute(builtin.Command, key),
        ToolTarget tool => LaunchTool(tool.ToolId),
        _ => false,
    };

    private bool Execute(CommandId command, Keys key) => command switch
    {
        CommandId.GoRoot => GoRoot(),
        // 数字キー 1〜9 が A: 〜 I:（0x831F）
        CommandId.DriveByNumberKey => GoDrive(key - Keys.D0),
        // ドライブの選択（0x82F3）。表示中はバーにフォーカスを移し、非表示ならモーダルで選ばせる（R-77）
        CommandId.SelectDrive => _driveBarShown ? _driveBar.EnterKeyboardSelection(_currentFolder) : SelectDriveInModal(),
        CommandId.ToggleDriveBar => ToggleDriveBar(),
        CommandId.FolderHistory => ShowFolderHistory(),
        CommandId.QuickAccess => ShowQuickAccess(),
        CommandId.DirectJump => DirectJump(),
        CommandId.SortSettings => ShowSortSettings(),
        CommandId.FileTypeSettings => ShowFileTypeSettings(),
        CommandId.ShowPopupMenu => ShowCommandPopup(),
        CommandId.ExternalToolQueue => ShowToolQueue(),
        CommandId.ExternalToolSettings => ShowExternalToolSettings(),
        CommandId.EnvironmentSettings => ShowEnvironmentSettings(),
        CommandId.About => ShowAbout(),
        CommandId.ColorAndFontSettings => ShowColorFontSettings(),
        CommandId.KeyAssignSettings => ShowKeyAssignSettings(),
        CommandId.VisibleDriveSettings => ShowDriveVisibilitySettings(),
        CommandId.RunCommandLine => RunCommandLine(),
        CommandId.CopyToFolder => Transfer(moving: false),
        CommandId.MoveToFolder => Transfer(moving: true),
        CommandId.Delete => DeleteTargets(),
        CommandId.Rename => RenameTargets(),
        CommandId.ChangeAttributes => ChangeAttributes(),
        CommandId.CreateFolder => CreateFolder(),
        CommandId.CreateShortcut => CreateShortcuts(),
        CommandId.CopyFileName => ShowNameFormatPopup(),
        // R-15: ポップアップを経由せず 1 形式だけをキーに割り当てるための 3 つ
        CommandId.CopyFileNameWithPath => CopyNamesCommand(NameFormat.PathAndName),
        CommandId.CopyFileNameOnly => CopyNamesCommand(NameFormat.NameOnly),
        CommandId.CopyFileNameWithPathSlash => CopyNamesCommand(NameFormat.SlashPath),
        CommandId.MarkByWildcard => MarkByWildcard(),
        CommandId.ShowProperties => ShowProperties(),
        CommandId.ConcatFiles => ConcatFiles(),
        CommandId.ClipboardCopy => ClipboardPut(cut: false),
        CommandId.ClipboardCut => ClipboardPut(cut: true),
        CommandId.ClipboardPaste => ClipboardPaste(),
        CommandId.ToggleAllMarks => MarkCommand(state => state.ToggleAllMarks()),
        CommandId.InvertMarks => MarkCommand(state => state.InvertMarks()),
        CommandId.MarkBySameExtension => MarkCommand(state => state.MarkBySameExtension()),
        CommandId.GoParent => GoParentCommand(),
        CommandId.GoDesktop => GoDesktop(),
        CommandId.OpenFile => OpenCursor(),
        CommandId.Quit => QuitCommand(),
        CommandId.QuitAll => QuitAllCommand(),
        CommandId.NewWindow => NewWindowCommand(),
        CommandId.ShowContextMenu => ShowShellContextMenu(_list.PointToScreen(_list.PopupAnchor())),
        CommandId.ShowFolderBackgroundMenu => ShowFolderBackgroundMenu(_list.PointToScreen(_list.PopupAnchor())),
        CommandId.Refresh => Reload(),
        CommandId.QuickAccessSettings => ShowQuickAccessSettings(),
        CommandId.QuickAccessAdd => AddCurrentToQuickAccess(),
        CommandId.GoBack => GoHistory(_history.Back(_currentFolder), record: false),
        CommandId.GoForward => GoHistory(_history.Forward(_currentFolder), record: false),
        CommandId.IncrementalSearch => OpenIncrementalSearch(),
        _ => false,
    };

    /// <summary>
    /// R-80: 検索バーを出す。Dock の外側・内側は追加した順ではなく、その時点の子の添字で決まる
    /// （添字が大きいほど外側）。検索バーは隠したまま作るので、WinForms が表示のときに並びを詰め替えて
    /// ステータスバーより外側へ移し、検索バーが最下段に出ていた。出す直前にステータスバーを末尾へ戻す
    /// </summary>
    private bool OpenIncrementalSearch()
    {
        var opened = _search.Open();
        Controls.SetChildIndex(_statusBar, Controls.Count - 1);
        return opened;
    }

    /// <summary>`H`（0x82FD）。過去 16 回分をカーソル位置のポップアップに出す（N-06）。</summary>
    private bool ShowFolderHistory()
    {
        // 履歴には出ていったフォルダが積まれる。先頭は「さっきまでいた場所」
        var items = _history.Recent
            .Select(path => (path, (Action)(() => GoHistory(path))))
            .ToList();

        // 卓駆はこのポップアップにだけ「履歴のクリア」を置いている
        NumberedPopup.Show(_list, _list.PopupAnchor(), items, "（履歴がありません）",
            footer: [("履歴のクリア(&E)", () => _history.Clear())]);
        return true;
    }

    /// <summary>ソートの設定（`S` / 0x8300）。選び直したら並べ直して即座に反映する。</summary>
    private bool ShowSortSettings()
    {
        using var dialog = new SortDialog(_sortOrder);
        if (dialog.ShowDialog(this) != DialogResult.OK) return true;
        _sortOrder = dialog.Result;
        SaveSettings();   // V-13: 設定画面の変更はその場で JSON に落とす（異常終了で失わない）
        return Reload();
    }

    /// <summary>
    /// 外部ツールの起動（F-03）。入力を集める → 値を求める → 確認する → 起動する。
    /// R-59: 種別を問わず渡す。判定して拒否したり警告したりしない。
    /// </summary>
    private bool LaunchTool(int toolId)
    {
        _ = LaunchToolAsync(toolId);
        return true;
    }

    private async Task LaunchToolAsync(int toolId)
    {
        // fire-and-forget なので、想定外の例外は Application.ThreadException に届かない。ここで知らせる（V-03）
        try
        {
            await RunToolAsync(toolId);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!IsDisposed) MessageBox.Show(this, ex.Message, "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task RunToolAsync(int toolId)
    {
        // B-05: 未設定のツールは何もしない。案内もエラーも出さない
        if (_settings.ExternalTools.FirstOrDefault(t => t.Id == toolId) is not { } tool
            || string.IsNullOrWhiteSpace(tool.Path)) return;

        var template = ArgumentTemplate.Parse(tool.Arguments);
        if (!template.IsValid)
        {
            // 設定ダイアログは誤りを保存させないが、設定ファイルは手で直せる（R-55）
            MessageBox.Show(this,
                $"「{tool.Name}」の引数に誤りがあります。{Environment.NewLine}{string.Join(Environment.NewLine, template.Errors.Select(e => e.Message))}",
                "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // 押した時点の対象で起動する。入力の間にカーソルやマークが動いても変わらない
        var suppress = _settings.SuppressMultipleToolLaunch;
        var targets = LaunchPlanner.TargetsFor(_list.State, suppress);
        var cursor = _list.State.Cursor;
        var folder = _currentFolder;

        var answers = new List<string>();
        foreach (var prompt in template.Prompts)
        {
            // このウィンドウだけを止める。他の ReTAC ウィンドウは入力の間も操作できる（Step 15b）
            using var dialog = new TextInputDialog(tool.Name, prompt.Title.Length > 0 ? prompt.Title : tool.Name, prompt.Default);
            if (await OwnerModal.ShowAsync(this, dialog) != DialogResult.OK) return;   // 1 つでもキャンセルしたら起動しない
            answers.Add(dialog.Value);
        }

        // 予定 §7 C-9: 値を求めるのは「待つことがある処理」として扱う（将来 git の値を取る）。UI を止めない
        var requests = await Task.Run(() =>
            LaunchPlanner.Plan(tool, template, targets, cursor, folder, answers, suppress, ToolLauncher.TargetPath));
        if (requests.Count == 0 || IsDisposed) return;
        if (!await ConfirmLaunchAsync(tool, requests, suppress)) return;

        if (LaunchPlanner.RunsPerItem(tool, suppress))
        {
            // F-05: 前の 1 件の終了を待ってから次を起動する（変換系の CPU・git の index.lock の奪い合いを避ける）
            ToolQueueHost.Queue.Enqueue(requests);
            return;
        }
        foreach (var request in requests) await ToolLauncher.StartAsync(this, request);
    }

    /// <summary>F-03: 「実行前に確認する」と、実際に 10 回以上起動するときの確認（R-56-3）を 1 回にまとめる。</summary>
    private async Task<bool> ConfirmLaunchAsync(ExternalTool tool, IReadOnlyList<LaunchRequest> requests, bool suppress)
    {
        var many = LaunchPlanner.NeedsManyConfirmation(tool, suppress, requests.Count);
        if (!tool.ConfirmBeforeRun && !many) return true;

        var nl = Environment.NewLine;
        var message = LaunchPlanner.RunsPerItem(tool, suppress)
            ? $"「{tool.Name}」を {requests.Count} 回起動します。よろしいですか。{nl}{nl}1 件目:{nl}{requests[0].DisplayCommandLine()}"
            : $"「{tool.Name}」を起動します。よろしいですか。{nl}{nl}{requests[0].DisplayCommandLine()}";
        using var dialog = new ConfirmDialog(message);
        return await OwnerModal.ShowAsync(this, dialog) == DialogResult.OK;
    }

    // ---- ファイル操作（段6） ----------------------------------------------

    /// <summary>
    /// `C`（コピー・0x82DE）と `M`（移動・0x82E0）。§9.2 のダイアログ。
    /// R-41: 宛先の確定と複写条件の判定は自前、転送は OS（IFileOperation）に委ねる。
    /// </summary>
    private bool Transfer(bool moving)
    {
        var targets = _list.State.EffectiveTarget();   // R-10
        if (targets.Count == 0) return true;

        var what = targets.Count == 1 ? targets[0].Name : $"{targets.Count} 個の項目";
        var verb = moving ? "移動" : "コピー";

        using var dialog = new PathInputDialog(
            $"ファイルの{verb}",
            heading: $"{what} の{verb}先は？",
            preset: "",                                  // R-46-4: 宛先はプリセットしない
            _history, _quickAccess, _currentFolder,
            acceptText: "OK",
            hints: ["Shift+Enter:フォルダ参照   ↑:フォルダ履歴   ↓:クイックアクセス"],
            // R-41-6: 差分の主経路。衝突のたびに問う方式より優先する。
            // R-51: 移動のダイアログはコピーと同一仕様（文言だけが違う）
            optionText: "複写先にある同名の古いファイルのみ置き換える(&X)");

        if (dialog.ShowDialog(this) != DialogResult.OK) return true;

        if (dialog.Path.Length == 0)
        {
            // R-63 / R-63-2: 空欄はコピーならリネームコピー、移動ならエラー
            if (moving)
            {
                MessageBox.Show(this, "移動先を指定してください。", "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return Transfer(moving);
            }
            return RenameCopy(targets);
        }

        // R-61: 相対パスはカレントフォルダ基準で解決する
        if (PathResolver.Resolve(_currentFolder, dialog.Path) is not { } destination)
        {
            MessageBox.Show(this, $"{dialog.Path} は宛先として解決できません。", "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return Transfer(moving);
        }

        if (!Directory.Exists(destination))
        {
            // R-62: 存在しない宛先はエラーにせず対処を選ばせる
            switch (AskMissingDestination(destination, moving))
            {
                case MissingDestination.CreateFolder:
                    try { Directory.CreateDirectory(destination); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        MessageBox.Show(this, ex.Message, "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return true;
                    }
                    break;
                case MissingDestination.RenameCopy:
                    return RenameCopy(targets, Path.GetFileName(destination));
                default:
                    return true;
            }
        }

        // N-02: 移動の履歴とコピー先の履歴は共通のひとつ。
        // 次に同じ宛先へ送るとき、↑ の先頭で拾えることに価値がある
        _history.Remember(destination);

        var completed = ExecuteTransfer([.. targets.Select(t => t.FullPath)], destination,
                                        moving, differentialOnly: dialog.OptionChecked);

        // R-42: コピーはマークを保持し、移動は元が消えるのでマークも消える。
        // ただし中断・失敗したときは元が残っているので、やり直せるようマークも残す
        if (moving && completed) { _list.State.ClearMarks(); RefreshStatus(); }
        Reload();
        return true;
    }

    private enum MissingDestination { CreateFolder, RenameCopy, Cancel }

    /// <summary>
    /// R-62: 「フォルダを新しく作成する」（既定）か「別の名前に変えて複写する」か。
    /// 移動に別名複写は無い（R-63-2）ので選択肢自体を出さない。
    /// 出しておいて選ばれても何も起きない、という状態にはしない
    /// </summary>
    private MissingDestination AskMissingDestination(string destination, bool moving)
    {
        var choices = moving
            ? new[] { "フォルダを新しく作成する(&C)" }
            : ["フォルダを新しく作成する(&C)", "別の名前に変えて複写する(&R)"];

        using var dialog = new ChoiceDialog(
            "指定のパスが見つかりません",
            $"{destination} は存在しません。どうしますか。",
            choices);
        return dialog.ShowDialog(this) != DialogResult.OK
            ? MissingDestination.Cancel
            : dialog.SelectedIndex == 0 ? MissingDestination.CreateFolder : MissingDestination.RenameCopy;
    }

    /// <summary>R-63: 同一フォルダ内での別名複製。元の名前をプリセットして全選択する（R-46）。</summary>
    private bool RenameCopy(IReadOnlyList<Entry> targets, string? presetName = null)
    {
        foreach (var target in targets)
        {
            using var dialog = new TextInputDialog(
                "名前を変えて複写", $"{target.Name} の新しい名前は？", presetName ?? target.Name,
                validate: value =>
                {
                    if (FileNameRules.Validate(value) is { } invalid) return invalid;
                    var candidate = Path.Combine(_currentFolder, value);
                    return File.Exists(candidate) || Directory.Exists(candidate)
                        ? $"{value} は既に存在します。別の名前を入力してください。"
                        : null;
                });
            if (dialog.ShowDialog(this) != DialogResult.OK) return true;

            RunOperation(silentOverwrite: false,
                operation => operation.Copy(target.FullPath, _currentFolder, dialog.Value));
        }
        Reload();
        return true;
    }

    /// <summary>
    /// R-41-4: 転送すべき対象だけを OS に渡す。
    /// <b>コピー・移動・ドロップ・貼り付けのすべてがここを通る。</b>
    /// 経路によって衝突の扱いが変わると利用者が予測できないため、入口は 1 つにする。
    /// </summary>
    /// <returns>すべて転送できたら true。中断・失敗なら false。</returns>
    private bool ExecuteTransfer(IReadOnlyList<string> sources, string destination, bool moving, bool differentialOnly)
    {
        // 宛先が転送元そのもの、あるいはその配下なら送らない。
        // 規則は DropRules が持っていたがドロップ経路にしか効いておらず、
        // C / M の宛先欄と Ctrl+V は素通りしていた。入口はここ 1 つなので全経路に効く（V-05）
        var usable = sources.Where(s => !TransferGuards.IsInsideOrSame(destination, s)).ToList();
        if (usable.Count < sources.Count)
            MessageBox.Show(this, "転送先が転送元の中にあるため、その項目は送りません。",
                "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        if (usable.Count == 0) return false;
        sources = usable;

        // R-41-4: 衝突の判定は必ず自前でやる。OS の衝突解決 UI に丸投げしない。
        // 移動もコピーと同じ道を通す（R-51: 差異は文言のみ）
        CopyPlan plan;
        if (differentialOnly)
        {
            plan = CopyPlanner.Build(sources, destination, CopyCondition.NewerOnly);
        }
        else
        {
            // 衝突のたびに問う（R-41-5）。「以降全て」を押されたらそれ以降は問わない（R-50）
            CopyCondition? applyToAll = null;
            var cancelled = false;
            plan = CopyPlanner.Build(sources, destination, conflict =>
            {
                if (cancelled) return CopyCondition.Skip;
                if (applyToAll is { } fixedCondition) return fixedCondition;

                using var conflictDialog = new ConflictDialog(conflict, ConflictResolver.Default);
                if (conflictDialog.ShowDialog(this) != DialogResult.OK) { cancelled = true; return CopyCondition.Skip; }
                if (conflictDialog.ApplyToAll) applyToAll = conflictDialog.Condition;
                return conflictDialog.Condition;
            });
            if (cancelled) return false;
        }

        // 複写条件で全件が対象外になるのは差分更新では普通のこと。いちいち知らせない
        if (plan.Items.Count == 0) return true;

        // 宛先に重なるものが一つも無い移動は、フォルダごと OS に渡してよい。
        // 同一ドライブなら 1 回のリネームで終わるので、数千件でも一瞬で済む。
        // 重なりがあるときだけファイル単位に落とす（判定はすでに自前で済ませている）
        var wholesale = moving && plan.Conflicts == 0 && !plan.Folders.Any(Path.Exists);

        // 判定済みなので OS には衝突を問わせない（R-41-4）
        var completed = RunOperation(silentOverwrite: true, operation =>
        {
            if (wholesale)
            {
                foreach (var source in sources) operation.Move(source, destination);
                return;
            }

            foreach (var folder in plan.Folders) Directory.CreateDirectory(folder);
            foreach (var item in plan.Items)
            {
                if (moving) operation.Move(item.Source, item.DestinationFolder, item.NewName);
                else operation.Copy(item.Source, item.DestinationFolder, item.NewName);
            }
        });

        // ファイル単位で動かしたときだけ、空になった転送元のフォルダが残る。
        // フォルダごと渡したときは元ごと消えているので触らない
        if (moving && !wholesale) RemoveEmptySourceFolders(sources);

        return completed;
    }

    /// <summary>
    /// 差分移動のあと片付け。<b>中身が残っているフォルダには触らない。</b>
    /// 転送元として指定されたフォルダとその配下だけを見る。
    /// </summary>
    private static void RemoveEmptySourceFolders(IEnumerable<string> sources)
    {
        foreach (var source in sources.Where(Directory.Exists))
        {
            // 深い方から順に、空になったものだけを消す
            foreach (var folder in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)
                         .OrderByDescending(f => f.Length))
                TryRemoveEmpty(folder);
            TryRemoveEmpty(source);
        }

        static void TryRemoveEmpty(string folder)
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 消せないなら残しておくだけでよい
            }
        }
    }

    /// <summary>`D`（削除・0x82DF）。R-19: ごみ箱経由。確認は Windows 標準（R-44）。</summary>
    private bool DeleteTargets()
    {
        var targets = _list.State.EffectiveTarget();
        if (targets.Count == 0) return true;

        var deleted = RunOperation(silentOverwrite: false, operation =>
        {
            foreach (var target in targets) operation.Delete(target.FullPath);
        });

        // 中断・失敗したときは消えていないので、マークを残してやり直せるようにする
        if (deleted) { _list.State.ClearMarks(); RefreshStatus(); }
        Reload();
        return true;
    }

    /// <summary>
    /// シェルのファイル操作を組み立てて実行する。
    /// <b>生成・組み立て・実行・エラー表示をすべてここに閉じる。</b>
    ///
    /// 呼び出し元ごとに try/catch を書く形にしていたところ、4 経路のうち 2 つ
    /// （改名・別名複写）で書き漏らしており、対象が消えていると
    /// <see cref="ShellFileOperation"/> の <c>IOException</c> が未処理例外になって
    /// プロセスごと落ちていた。常駐が保っているマークが消えるので、経路を 1 本にする（V-02）。
    /// </summary>
    /// <param name="silentOverwrite">複写条件を自前で判定済みか（R-41-4）</param>
    /// <param name="build">操作を積む。ここで投げた例外もまとめて受ける</param>
    /// <returns>すべて実行できたら true。中断・失敗なら false（呼び出し側はマークを外さない）。</returns>
    private bool RunOperation(bool silentOverwrite, Action<ShellFileOperation> build)
    {
        // 転送中に自動更新が何度も走らないよう、終わってから 1 回だけ開き直す
        _watcher.EnableRaisingEvents = false;
        try
        {
            using var operation = new ShellFileOperation(Handle, silentOverwrite);
            build(operation);
            return operation.Execute();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // 6 章: エラーを提示し、ReTAC 本体は継続動作する
            MessageBox.Show(this, ex.Message, "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        finally { WatchCurrentFolder(); }
    }

    /// <summary>
    /// `N`（名前の変更・0x82E1）。R-45: 複数対象は 1 件ずつ。キャンセルで以降を打ち切り、
    /// <b>確定済みの分は元に戻さない</b>。R-46-2: 拡張子を含めた名前全体をプリセットして全選択。
    /// </summary>
    private bool RenameTargets()
    {
        string? renamed = null;
        // A-01: 改名でマークを失わない。付け替えた名前へマークを移す
        var state = _list.State;
        var marks = state.Marks.Select(i => state.Entries[i].Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var target in _list.State.EffectiveTarget())
        {
            using var dialog = new TextInputDialog(
                target.Kind == EntryKind.Folder ? "フォルダ名の変更" : "ファイル名の変更",
                $"{target.Name} の新しい名前は？",
                target.Name,
                validate: value =>
                {
                    if (FileNameRules.Validate(value) is { } invalid) return invalid;
                    // 変わっていないなら差し戻す。黙って進むと変更したつもりのまま次へ行ってしまう
                    if (string.Equals(value, target.Name, StringComparison.Ordinal))
                        return "名前が変わっていません。別の名前を入力してください。";
                    // 大文字小文字だけの変更は同じファイルなので衝突ではない
                    if (string.Equals(value, target.Name, StringComparison.OrdinalIgnoreCase)) return null;
                    var candidate = Path.Combine(_currentFolder, value);
                    return File.Exists(candidate) || Directory.Exists(candidate)
                        ? $"{value} は既に存在します。別の名前を入力してください。"
                        : null;
                });

            if (dialog.ShowDialog(this) != DialogResult.OK) break;   // R-45: 以降も打ち切る

            // 失敗した分は元の名前のまま
            if (!RunOperation(silentOverwrite: false,
                              operation => operation.Rename(target.FullPath, dialog.Value))) continue;
            if (marks.Remove(target.Name)) marks.Add(dialog.Value);
            renamed = dialog.Value;
        }

        // 名前が変わるとソート順の位置も変わる。カーソルは新しい名前に付いていく（卓駆と同じ）
        Reload(renamed, marks);
        return true;
    }

    /// <summary>`A`（属性変更・0x82E2）。R-49: 属性とタイムスタンプは独立。R-50: 「以降全て」。</summary>
    private bool ChangeAttributes()
    {
        var targets = _list.State.EffectiveTarget();
        if (targets.Count == 0) return true;

        var containsFolder = targets.Any(t => t.Kind == EntryKind.Folder);
        AttributeDialog? shared = null;

        try
        {
            foreach (var target in targets)
            {
                var dialog = shared;
                if (dialog is null)
                {
                    dialog = new AttributeDialog(target.Name, target.Attributes, target.LastWriteTime,
                        containsFolder, allowApplyToAll: targets.Count > 1);
                    if (dialog.ShowDialog(this) != DialogResult.OK) { dialog.Dispose(); break; }
                    if (dialog.ApplyToAll) shared = dialog;
                }

                try
                {
                    ApplyAttributes(target.FullPath, dialog, Directory.Exists(target.FullPath));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // R-41-3: その場で提示する。まとめて最後に報告はしない
                    MessageBox.Show(this, ex.Message, "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                if (!ReferenceEquals(dialog, shared)) dialog.Dispose();
            }
        }
        finally
        {
            shared?.Dispose();
        }

        Reload();
        return true;
    }

    private static void ApplyAttributes(string path, AttributeDialog dialog, bool isFolder)
    {
        if (dialog.ChangeAttributes)
            File.SetAttributes(path, isFolder ? dialog.Attributes | FileAttributes.Directory : dialog.Attributes);

        if (dialog.ChangeTimestamp)
        {
            if (isFolder) Directory.SetLastWriteTime(path, dialog.Timestamp);
            else File.SetLastWriteTime(path, dialog.Timestamp);
        }

        if (!isFolder || !dialog.IncludeSubfolders) return;

        // ジャンクションの先は「サブフォルダ」ではない。辿ると選んでいないフォルダの
        // ファイルまで書き換え、上位を指すリンクなら StackOverflowException で落ちる。
        // CopyPlanner が同じ判定を通るのと揃える（V-01）
        if (!TransferGuards.CanDescend(path)) return;

        // R-49-2: サブフォルダのファイルにも同じ設定を適用する
        foreach (var child in Directory.EnumerateFileSystemEntries(path))
            ApplyAttributes(child, dialog, Directory.Exists(child));
    }

    /// <summary>`K`（フォルダ作成・0x82F8）。R-46-3 でプリセット、R-48 で入力欄に戻す。</summary>
    private bool CreateFolder()
    {
        var preset = _list.State.Cursor is { IsParent: false } cursor ? cursor.Name : "";

        using var dialog = new TextInputDialog(
            "フォルダの作成", "新しく作成するフォルダの名前は？", preset,
            validate: value =>
            {
                if (FileNameRules.Validate(value) is { } invalid) return invalid;
                var candidate = Path.Combine(_currentFolder, value);
                return File.Exists(candidate) || Directory.Exists(candidate)
                    ? $"{value} は既に存在します。別の名前を入力してください。"
                    : null;
            });

        if (dialog.ShowDialog(this) != DialogResult.OK) return true;

        var created = Path.Combine(_currentFolder, dialog.Value);
        try
        {
            Directory.CreateDirectory(created);
            // 作ったフォルダを履歴に入れる（卓駆と同じ）。作った直後は
            // そこへ移るかコピー先に指定することが多く、N-02 で履歴は宛先欄と共通
            _history.Remember(created);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        Reload();
        return true;
    }

    /// <summary>`O`（ショートカットの作成・0x82F1）。R-58 の 3 オプション。複数対象は連続作成。</summary>
    private bool CreateShortcuts()
    {
        var targets = _list.State.EffectiveTarget();
        if (targets.Count == 0) return true;

        using var dialog = new ShortcutDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK) return true;

        var folder = dialog.OnDesktop
            ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            : _currentFolder;

        foreach (var target in targets)
        {
            try
            {
                ShellObjects.CreateShortcut(
                    Path.Combine(folder, dialog.NameFor(target.Name)), target.FullPath, _currentFolder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, ex.Message, "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                break;
            }
        }

        Reload();
        return true;
    }

    /// <summary>`I`（ファイル名をコピー・0x831B）。R-57: カーソル位置に形式選択のポップアップ。</summary>
    private bool ShowNameFormatPopup()
    {
        var targets = _list.State.EffectiveTarget();
        if (targets.Count == 0) return true;

        List<(string, Action)> items =
        [
            ("パス＋名前", () => CopyNames(targets, NameFormat.PathAndName)),
            ("名前のみ", () => CopyNames(targets, NameFormat.NameOnly)),
            ("/ 区切りのパス", () => CopyNames(targets, NameFormat.SlashPath)),
        ];
        NumberedPopup.Show(_list, _list.PopupAnchor(), items);
        return true;
    }

    /// <summary>
    /// クリップボードは他プロセスが掴んでいると CLIPBRD_E_CANT_OPEN を投げる。
    /// Windows では日常的に起きるので、落とさず知らせるだけにする。
    /// </summary>
    private bool TryClipboard(Action action)
    {
        try { action(); return true; }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(this, "クリップボードを他のアプリが使用中です。少し待ってからやり直してください。",
                "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    /// <summary>形式を選ばせずに直接コピーする（キー割り当て用）。</summary>
    private bool CopyNamesCommand(NameFormat format)
    {
        CopyNames(_list.State.EffectiveTarget(), format);
        return true;
    }

    /// <summary>ワイルドカードで選択（0x8326）。今あるマークは解除せずに足す。</summary>
    private bool MarkByWildcard()
    {
        var preset = _list.State.Cursor is { IsParent: false, Extension.Length: > 0 } cursor ? $"*{cursor.Extension}" : "*.*";
        using var dialog = new TextInputDialog(
            "ワイルドカードで選択", "選択する名前のパターンは？", preset,
            validate: value => value.Length == 0 ? "パターンを入力してください。" : null);

        if (dialog.ShowDialog(this) != DialogResult.OK) return true;
        return MarkCommand(state => state.MarkByWildcard(dialog.Value));
    }

    /// <summary>R-57-2: 複数対象は改行区切りでまとめてクリップボードへ。</summary>
    private void CopyNames(IReadOnlyList<Entry> targets, NameFormat format)
    {
        var text = NameFormats.Format(targets, format);
        if (text.Length > 0) TryClipboard(() => Clipboard.SetText(text));
    }

    /// <summary>`R`（プロパティ・0x82F2）。R-56-2: 複数時の扱いは外部ツールと同じ規則。</summary>
    private bool ShowProperties()
    {
        var targets = LaunchTargets.For(_list.State, _settings.SuppressMultipleToolLaunch);
        if (targets.Count == 0 || !ConfirmManyWindows(targets.Count)) return true;

        foreach (var target in targets)
        {
            try { ShellObjects.ShowProperties(Handle, target.FullPath); }
            catch (IOException ex)
            {
                MessageBox.Show(this, ex.Message, "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                break;
            }
        }
        return true;
    }

    /// <summary>ファイルの連結（0x82E4）。R-35: 連結順を並べ替えられること。</summary>
    private bool ConcatFiles()
    {
        var targets = _list.State.EffectiveTarget().Where(t => t.Kind == EntryKind.File).ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "連結するファイルがありません。", "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }

        using var dialog = new ConcatDialog(targets, _history, _currentFolder);
        if (dialog.ShowDialog(this) != DialogResult.OK) return true;

        if (PathResolver.Resolve(_currentFolder, dialog.Destination) is not { } destination)
        {
            MessageBox.Show(this, "連結先を解決できません。", "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return ConcatFiles();
        }

        try
        {
            FileConcat.Concat([.. dialog.Sources.Select(e => e.FullPath)], destination,
                dialog.CutEof, dialog.AppendNewLine);
            _history.Remember(Path.GetDirectoryName(destination) ?? _currentFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        Reload();
        return true;
    }

    /// <summary>マーク集合を変える操作。メニューからも `G` からも同じ経路を通す。</summary>
    /// <summary>Shift+英字の頭出し。該当が無ければカーソルは動かない（エラーも出さない）。</summary>
    private bool JumpToInitial(char letter)
    {
        var index = _list.State.IndexOfNextStartingWith(letter);
        if (index >= 0) _list.MoveCursorTo(index);
        return true;
    }

    private bool MarkCommand(Action<Selection.ListState> change)
    {
        change(_list.State);
        _list.Invalidate();
        RefreshStatus();
        return true;
    }

    private bool GoParentCommand()
    {
        GoParent();
        return true;
    }

    /// <summary>R-32-2: デスクトップフォルダへ移動する。</summary>
    private bool GoDesktop()
    {
        _ = OpenFolderAsync(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        return true;
    }

    /// <summary>メニューの「開く」。カーソル位置の項目を Enter と同じ規則で開く。</summary>
    private bool OpenCursor()
    {
        if (_list.State.Cursor is { } cursor) OnActivated(cursor);
        return true;
    }

    /// <summary>
    /// `Q`（ReTAC の終了・0x831A）。R-40: 常駐が有効ならプロセスを終わらせず最小化する。
    /// <b>目的は起動速度ではなく作業状態の保持</b>であり、`Q` の誤爆でマークを失わないこと（R-40-2）。
    /// </summary>
    private bool QuitCommand()
    {
        Close();
        return true;
    }

    /// <summary>R-40-4: 常駐していても明示的に終了する手段（全ての ReTAC を終了・0x814E）。</summary>
    private bool QuitAllCommand()
    {
        if (!ConfirmQueueBeforeExit()) return true;

        // 確認は出さない。卓駆も出さず、常駐の状態は次の起動で復元されるので失うものがない
        // R-40-7 の「最後の 1 つだけ残す」は完全終了には適用しない。全部閉じる
        foreach (var form in Application.OpenForms.OfType<MainForm>().ToList()) form._fullExit = true;
        Application.Exit();
        return true;
    }

    /// <summary>
    /// K-5: 外部ツールキューに待ちが残ったままプロセスを終えるときの確認。動いている 1 件は止めない
    /// （書きかけのファイルが残らないように。「終了後もウィンドウを閉じない」のコンソールもそのまま残る）。
    /// </summary>
    private bool ConfirmQueueBeforeExit()
    {
        var waiting = ToolQueueHost.Queue.WaitingCount;
        if (waiting == 0) return true;

        var answer = MessageBox.Show(this,
            $"外部ツールキューに残り {waiting} 件あります。取りやめて終了しますか。{Environment.NewLine}（動いている 1 件は止めません）",
            "ReTAC", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK) return false;

        ToolQueueHost.Queue.CancelWaiting();
        return true;
    }

    /// <summary>R-36 / R-60: 新しいウィンドウ。各ウィンドウが独立した状態を持つ。</summary>
    private bool NewWindowCommand()
    {
        var window = new MainForm(settings: _settings);
        window.Show();
        _ = window.OpenFolderAsync(_currentFolder);
        return true;
    }

    /// <summary>
    /// R-60-3: 完全終了時に保存するのはカレントフォルダ・ソート・表示ファイルタイプ・
    /// ウィンドウの位置とサイズまで。<b>カーソル位置とマークは保存しない</b>
    /// （それらは常駐によって保たれるものであって、ファイルに残すものではない）。
    /// </summary>
    private void SaveSettings()
    {
        _settings.LastFolder = _currentFolder;

        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.WindowX = bounds.X;
        _settings.WindowY = bounds.Y;

        // 最小化中の ClientSize はほぼ 0。そのまま保存すると次の起動で潰れたウィンドウが出る。
        // 通常表示だったときの大きさを控えておいて、それを書く
        _settings.WindowWidth = _normalClientSize.Width;
        _settings.WindowHeight = _normalClientSize.Height;

        _settings.SortKey = _sortOrder.Key;
        _settings.SortDirection = _sortOrder.Direction;
        _settings.SortMode = _sortOrder.Mode;

        _settings.ShowFolders = _fileTypes.Folders;
        _settings.ShowPrograms = _fileTypes.Programs;
        _settings.ShowAssociated = _fileTypes.Associated;
        _settings.ShowArchives = _fileTypes.Archives;
        _settings.ShowOthers = _fileTypes.Others;
        _settings.ShowSystemFiles = _fileTypes.SystemFiles;
        _settings.ShowHiddenFiles = _fileTypes.HiddenFiles;

        _settings.QuickAccess = [.. _quickAccess.Items];
        _settings.QuickAccessShowTitles = _quickAccess.ShowTitles;
        _settings.QuickAccessFixMissing = _quickAccess.FixMissingAutomatically;
        _settings.FolderHistory = [.. _history.Recent];

        try
        {
            // R-55-3: 実行ディレクトリに書けない場合は逃がし、初回に一度だけ知らせる
            if (_settings.Save())
                MessageBox.Show(this,
                    $"実行ディレクトリに書き込めないため、設定を次の場所に保存しました。{Environment.NewLine}{AppSettings.FallbackPath}",
                    "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 保存できなくても終了は妨げない
        }
    }

    /// <summary>R-39-3 の「明示的なドライブ変更」。ドライブバーと、非表示時のモーダル（R-77）の共通の出口。</summary>
    private void OnDriveChosen(string path)
    {
        _list.Focus();
        // ドライブのボタンはルートを指す。数字キーと同じく、前にいた場所へ戻す
        _ = OpenFolderAsync(FolderEnumerator.IsDriveRoot(path) ? LastFolderOn(path) : path);
    }

    /// <summary>
    /// R-77: ドライブバーの表示切り替え。効くのはこのウィンドウだけ（NewWindow のウィンドウは設定を共有するが、
    /// 表示するドライブの設定と同じく、ほかのウィンドウには及ぼさない）。
    /// 設定の値ではなく、このウィンドウの表示を反転する。設定を反転すると、ウィンドウごとの表示と
    /// 食い違ったときに押しても表示が変わらない。
    /// </summary>
    private bool ToggleDriveBar()
    {
        _driveBarShown = !_driveBarShown;
        _driveBar.Visible = _driveBarShown;
        _driveBarMenuItem.Checked = _driveBarShown;
        _settings.ShowDriveBar = _driveBarShown;
        SaveSettings();   // V-13
        return true;
    }

    private bool SelectDriveInModal()
    {
        var path = DriveSelectForm.Pick(this, _list, _settings.ToHiddenDrives(), _settings.ShowDesktopButton, _currentFolder);
        _list.Focus();
        if (path is not null) OnDriveChosen(path);
        return true;
    }

    /// <summary>
    /// ドロップされたファイルをフォルダへ入れる（T8-2 / T8-3）。
    /// コピーか移動かは Windows の作法に合わせて <see cref="DropRules"/> が決める。
    /// </summary>
    private void DropInto(string destinationFolder, string[] files, DragDropEffects allowed)
    {
        // 衝突すると確認ダイアログを出す。ドロップ元（エクスプローラー）が前面のままだと
        // ダイアログがその後ろに隠れて、固まったように見える
        Activate();

        var ctrl = ModifierKeys.HasFlag(Keys.Control);
        var shift = ModifierKeys.HasFlag(Keys.Shift);

        var copies = new List<string>();
        var moves = new List<string>();
        foreach (var file in files)
        {
            // R-78: 表示（DropFeedback）と同じく、ドラッグ元が許す効果に合わせる
            var action = DropRules.Allow(DropRules.Decide(file, destinationFolder, ctrl, shift),
                copyAllowed: allowed.HasFlag(DragDropEffects.Copy),
                moveAllowed: allowed.HasFlag(DragDropEffects.Move));
            switch (action)
            {
                case DropAction.Copy: copies.Add(file); break;
                case DropAction.Move: moves.Add(file); break;
            }
        }
        if (copies.Count == 0 && moves.Count == 0) return;

        // 衝突の扱いは C / M と同じにする（R-41-4）。ドロップだからと OS の
        // 置換確認に落ちると、同じ「移動」なのに経路で挙動が変わってしまう。
        // A-01: ドロップしてもマークは動かさない
        if (copies.Count > 0) ExecuteTransfer(copies, destinationFolder, moving: false, differentialOnly: false);
        if (moves.Count > 0) ExecuteTransfer(moves, destinationFolder, moving: true, differentialOnly: false);

        Reload();
    }

    /// <summary>
    /// クリップボードへ（`Ctrl+C` / `Ctrl+X`・0xE122 / 0xE123）。
    /// エクスプローラーと相互にやり取りできるよう、シェルと同じ形式で入れる。
    /// </summary>
    private bool ClipboardPut(bool cut)
    {
        var targets = _list.State.EffectiveTarget();
        if (targets.Count == 0) return true;

        var paths = new System.Collections.Specialized.StringCollection();
        foreach (var target in targets) paths.Add(target.FullPath);

        var data = new DataObject();
        data.SetFileDropList(paths);
        // 切り取りか複写かは "Preferred DropEffect" で伝えるのがシェルの作法
        var effect = BitConverter.GetBytes((int)(cut ? DragDropEffects.Move : DragDropEffects.Copy));
        data.SetData("Preferred DropEffect", new MemoryStream(effect));
        TryClipboard(() => Clipboard.SetDataObject(data, copy: true));
        return true;
    }

    /// <summary>クリップボードから貼り付け（`Ctrl+V`・0xE125）。貼り付け先は常にカレントフォルダ。</summary>
    private bool ClipboardPaste()
    {
        IDataObject? data = null;
        if (!TryClipboard(() => data = Clipboard.GetDataObject())) return true;
        if (data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return true;

        var cut = data.GetData("Preferred DropEffect") is MemoryStream stream
                  && stream.Length >= 4
                  && (BitConverter.ToInt32(stream.ToArray(), 0) & (int)DragDropEffects.Move) != 0;

        // ReTAC の中で貼り付ける以上、C / M と同じ動きを期待される（R-41-4）
        ExecuteTransfer(paths, _currentFolder, moving: cut, differentialOnly: false);

        Reload();
        return true;
    }

    /// <summary>
    /// 右クリックの振り分け。
    /// 選択済みの項目の上ならシェルのメニュー、選択されていない名前の上なら `G` のポップアップ、
    /// 行頭アイコンの上なら選択してからシェルのメニュー。
    /// </summary>
    private void OnRightClick(FileListView.RightClick click)
    {
        var state = _list.State;

        // 項目の上でない（親フォルダ項目・余白）ときだけ ReTAC のコマンド一覧を出す
        if (click.Index < 0 || state.Entries[click.Index].IsParent)
        {
            // R-79: マウスで開いたので、右クリックした位置に出す
            // R-81: Shift 付きならエクスプローラーの背景のメニュー（「新規作成」を含む）
            if (click.Shift) ShowFolderBackgroundMenu(click.ScreenPoint);
            else ShowCommandPopup(_list.PointToClient(click.ScreenPoint));
            return;
        }

        // エクスプローラーと同じく、右クリックした項目にカーソルを移す。
        // 何に対するメニューなのかが目で分かる。マークは動かさない（左クリックだけ）
        _list.MoveCursorTo(click.Index);
        RefreshStatus();

        // 項目の上なら、マークの有無に関わらずシェルのメニュー
        var targets = state.Marks.Count > 0
            ? state.EffectiveTarget().Select(e => e.FullPath).ToList()   // R-10
            : [state.Entries[click.Index].FullPath];

        ShowShellContextMenu(targets, click.ScreenPoint);
    }

    /// <summary>シェルのコンテキストメニュー（0x8328）。書庫の圧縮・解凍はここから WinRAR へ委譲する。</summary>
    private bool ShowShellContextMenu(Point screenPoint) =>
        ShowShellContextMenu(_list.State.EffectiveTarget().Select(e => e.FullPath).ToList(), screenPoint);

    private bool ShowShellContextMenu(IReadOnlyList<string> targets, Point screenPoint)
    {
        if (targets.Count == 0) return false;

        try
        {
            // ここはサードパーティのシェル拡張（WinRAR など）が ReTAC のプロセス内で走る
            // 唯一の場所。拡張が投げてきたものを ReTAC の落ちる理由にはしない（V-04）
            ShellContextMenu.Show(Handle, targets, screenPoint.X, screenPoint.Y);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException
                                      or IOException or UnauthorizedAccessException)
        {
            // 6 章: エラーを提示し、ReTAC 本体は継続動作する
            MessageBox.Show(this, ex.Message, "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // メニューから削除・改名が行われることがあるので開き直す（自動更新でも拾えるが確実に）
        Reload();
        return true;
    }

    /// <summary>
    /// R-81: 今いるフォルダの背景のメニュー。作成で項目がちょうど 1 つ増えたら、そこにカーソルを合わせる。
    /// 名前の変更は開かない（作った直後は OS の既定の名前。変えたいときは N）。
    /// マークは名前で引き継ぐ（OpenFolderAsync に名前だけを渡すとマークが消える）。
    /// </summary>
    private bool ShowFolderBackgroundMenu(Point screenPoint)
    {
        if (_currentFolder.Length == 0) return false;
        var folder = _currentFolder;
        var before = NamesIn(folder);
        var marks = _list.State.Marks
            .Select(i => _list.State.Entries[i].Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            // シェル拡張が ReTAC のプロセス内で走る。投げてきたものを落ちる理由にしない（V-04）
            ShellContextMenu.ShowFolderBackground(Handle, folder, screenPoint.X, screenPoint.Y);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException
                                      or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // 作成が非同期で、まだ項目ができていなければ増えた名前は見つからない。
        // そのときは自動更新（R-23）で後から現れるので、カーソルは合わせない
        var after = before is null || !PathEquals(folder, _currentFolder) ? null : NamesIn(folder);
        List<string> added = after is null ? [] : [.. after.Except(before!, StringComparer.OrdinalIgnoreCase)];
        return added.Count == 1 ? Reload(added[0], marks) : Reload();
    }

    /// <summary>フォルダ直下の名前（隠し・システム属性を含む）。読めなければ null。</summary>
    private static HashSet<string>? NamesIn(string folder)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(folder)
                .Select(path => Path.GetFileName(path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>名前を指定し実行（`X` / 0x82E9）。作業ディレクトリはカレントフォルダ（R-54-2）。</summary>
    private bool RunCommandLine()
    {
        // R-46-4: カーソル位置のエントリ名をプリセット。親フォルダ項目なら空のまま
        var preset = _list.State.Cursor is { IsParent: false } cursor ? cursor.Name : "";

        using var dialog = new RunDialog(preset, _commandHistory);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.CommandLine.Length == 0) return true;

        var commandLine = dialog.CommandLine;
        try
        {
            // UseShellExecute: パスの通ったコマンド名も、拡張子つきのファイル名も同じように扱える
            Process.Start(new ProcessStartInfo(commandLine)
            {
                UseShellExecute = true,
                WorkingDirectory = _currentFolder,
                WindowStyle = dialog.WindowStyle,
            });
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            MessageBox.Show(this, ex.Message, "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return true;
        }

        _commandHistory.Remove(commandLine);
        _commandHistory.Insert(0, commandLine);
        return true;
    }

    /// <summary>R-56-3: 10 件以上を一度に開くときは実行前に確認する。</summary>
    private bool ConfirmManyWindows(int count)
    {
        if (!LaunchTargets.NeedsConfirmation(count)) return true;
        return MessageBox.Show(this, $"{count} 個のウィンドウが開きます。よろしいですか。",
            "ReTAC", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK;
    }

    /// <summary>外部ツールの設定（0x815A / F-04）。</summary>
    private bool ShowExternalToolSettings()
    {
        using var dialog = new ExternalToolDialog(_settings.ExternalTools, _settings.NextExternalToolId, _settings.SuppressMultipleToolLaunch);
        if (dialog.ShowDialog(this) != DialogResult.OK) return true;

        _settings.ExternalTools = [.. dialog.Tools];
        _settings.NextExternalToolId = dialog.NextId;
        // R-36: 複数ウィンドウが同じ AppSettings を共有している。この窓の _keyMap は
        // 他の窓での割り当て変更を反映していない古いものかもしれない。設定を元に作り直してから
        // 削除したツールの割り当てだけを外し、それを書き戻す（F-01）
        var map = _settings.ToKeyMap();
        foreach (var id in dialog.RemovedToolIds) map.ReleaseTool(id);
        _settings.FromKeyMap(map);

        SaveSettings();   // V-13
        RebuildMenus();
        return true;
    }

    /// <summary>キー割り当ての設定（0x8155）。5-2 節。</summary>
    private bool ShowKeyAssignSettings()
    {
        using var dialog = new KeyAssignDialog(_keyMap, _settings.ExternalTools);
        if (dialog.ShowDialog(this) != DialogResult.OK) return true;

        _keyMap = dialog.Result;
        _settings.FromKeyMap(_keyMap);
        SaveSettings();
        // R-36: 設定は全ウィンドウ共有。この窓の変更を他の窓にも反映する（RebuildMenus が各窓の _keyMap を読み直す）
        RebuildMenus();
        return true;
    }

    /// <summary>
    /// 外部ツールの一覧はウィンドウ間で共有している（同じ AppSettings）。全ウィンドウのメニューを作り直す。
    /// キーマップはウィンドウごとに持つので、保存済みの設定から読み直す。読み直さないと、削除したツールの割り当てが
    /// 他のウィンドウに残り、そのウィンドウでキー割り当てを OK したときに設定ファイルへ書き戻される（F-01）。
    /// </summary>
    private static void RebuildMenus()
    {
        foreach (var window in Application.OpenForms.OfType<MainForm>().ToList())
        {
            window._keyMap = window._settings.ToKeyMap();
            window.RebuildMenu();
        }
    }

    private void RebuildMenu()
    {
        Controls.Remove(_menu);
        _menu.Dispose();
        _menu = MenuBar.Create(target => Execute(target, Keys.None), _keyMap, _settings.ExternalTools, out _driveBarMenuItem);
        _driveBarMenuItem.Checked = _driveBarShown;
        Controls.Add(_menu);
        MainMenuStrip = _menu;
    }

    /// <summary>配色・フォントの設定（0x8151）。5-1 節。</summary>
    private bool ShowColorFontSettings()
    {
        using var dialog = new ColorFontDialog(_list.Theme);
        if (dialog.ShowDialog(this) != DialogResult.OK) return true;

        _list.Theme = dialog.Result;   // 行の高さと列幅はフォントから再計算される（R-66-3）
        _settings.FromTheme(dialog.Result);
        SaveSettings();
        return true;
    }

    /// <summary>表示するドライブの設定（0x814C）。16.7 節。</summary>
    private bool ShowDriveVisibilitySettings()
    {
        using var dialog = new DriveVisibilityDialog(_settings.ToHiddenDrives(), _settings.ShowDesktopButton);
        if (dialog.ShowDialog(this) != DialogResult.OK) return true;

        _settings.HiddenDrives = [.. dialog.Hidden.Select(c => c.ToString())];
        _settings.ShowDesktopButton = dialog.ShowDesktop;
        _driveBar.SetVisibility(dialog.Hidden, dialog.ShowDesktop);
        SaveSettings();
        return true;
    }

    /// <summary>動作環境の設定（0x8318）。変更はその場で JSON に落とす。</summary>
    /// <summary>バージョン情報（0xE140）。アイコンと版を出すだけ。</summary>
    private bool ShowAbout()
    {
        using var dialog = new AboutDialog();
        dialog.ShowDialog(this);
        return true;
    }

    private bool ShowEnvironmentSettings()
    {
        using var dialog = new EnvironmentDialog(_settings);
        if (dialog.ShowDialog(this) == DialogResult.OK) SaveSettings();
        return true;
    }

    /// <summary>表示するファイルタイプの設定（0x82FF）。16.4 節の 7 項目。</summary>
    private bool ShowFileTypeSettings()
    {
        using var dialog = new FileTypeDialog(_fileTypes);
        if (dialog.ShowDialog(this) != DialogResult.OK) return true;
        SaveSettings();   // V-13
        return Reload();
    }

    /// <summary>
    /// `G`（ポップアップメニュー・0x831C / R-38）。既定構成は 5-5 節。
    /// 書庫 2 項目・同じ拡張子で絞込み（S-19）・ファイル情報（スコープ外）を除く。
    /// よく使う設定はここからも開けるようにする（メニューバーの「設定(&amp;O)」が正規の入口）。
    /// </summary>
    /// <param name="anchor">
    /// R-79: 出す位置（リストのクライアント座標）。右クリックで開いたときはその位置、
    /// 省略したら（キー・メニューバー・マウスボタンの割り当て）カーソル行の直下（N-06）
    /// </param>
    private bool ShowCommandPopup(Point? anchor = null)
    {
        var at = anchor ?? _list.PopupAnchor();
        List<(string, Action)> items =
        [
            ("全選択＆選択解除", () => { _list.State.ToggleAllMarks(); _list.Invalidate(); RefreshStatus(); }),
            ("同じ拡張子を選択", () => { _list.State.MarkBySameExtension(); _list.Invalidate(); RefreshStatus(); }),
            ("", () => { }),
            // F-08: 固定の項目の番号がツールの数でずれないよう、ツールはサブメニューにまとめる
            // R-79: 2 段目も 1 段目と同じ位置に出す。後から開くので、位置は引数で引き継ぐ
            ("外部ツール ▶", () => BeginInvoke(() => ShowToolPopup(at))),
            ("", () => { }),
            ("ファイルの連結...", () => ConcatFiles()),
            ("", () => { }),
            ("ソートの設定...", () => ShowSortSettings()),
            ("表示するファイルタイプの設定...", () => ShowFileTypeSettings()),
            ("外部ツールの設定...", () => ShowExternalToolSettings()),
        ];
        NumberedPopup.Show(_list, at, items);
        return true;
    }

    /// <summary>
    /// F-08: `G` の「外部ツール ▶」。「ポップアップに表示する」が ON のツールを登録順に、1〜9 の番号付きで並べる。
    /// 呼び出し元のポップアップが閉じ切ってから開くので BeginInvoke で呼ぶ。
    /// </summary>
    private void ShowToolPopup(Point at)
    {
        var items = _settings.ExternalTools
            .Where(t => t.ShowInPopup)
            .Select(t => (t.Name, (Action)(() => LaunchTool(t.Id))))
            .ToList();
        NumberedPopup.Show(_list, at, items);
    }

    /// <summary>F-05: 外部ツールキューの進行状況のウィンドウ（確実な入口）。</summary>
    private bool ShowToolQueue()
    {
        ExternalToolQueueForm.ShowFor(this);
        return true;
    }

    /// <summary>キューの通知は裏のスレッドから来る（S-07）。UI スレッドへ移す。</summary>
    private void OnQueueChanged()
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke((Action)RefreshQueueStatus);
        }
        catch (InvalidOperationException)
        {
            // 閉じる途中でハンドルが無くなった
        }
    }

    private void RefreshQueueStatus()
    {
        if (IsDisposed) return;
        var queue = ToolQueueHost.Queue;
        _statusBar.QueueText = queue.StatusText();
        if (queue.Progress() is { } progress) TaskbarProgress.Set(Handle, progress.Done, progress.Total);
        else TaskbarProgress.Clear(Handle);
    }

    /// <summary>
    /// 再表示（`W` / `F5` / ソート変更 / 自動更新）。
    /// <b>カーソルとマークは名前で引き継ぐ。</b>並べ替えや外からの変更でマークが消えてはならない（R-11）。
    /// </summary>
    /// <param name="cursorName">
    /// 再表示後にカーソルを合わせる名前。改名の直後のように、
    /// 今のカーソルが指す名前が消えている場合だけ渡す
    /// </param>
    /// <param name="marks">付け直すマーク。改名のように名前が変わる操作でだけ渡す</param>
    private bool Reload(string? cursorName = null, IReadOnlySet<string>? marks = null)
    {
        if (_currentFolder.Length == 0) return false;

        // 6 章: カレントフォルダが消えたら、表示できる階層まで遡る（上限はドライブルート）
        var folder = _currentFolder;
        while (!Directory.Exists(folder) && FolderEnumerator.ParentOf(folder) is { } parent)
        {
            cursorName = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
            folder = parent;
        }
        if (!Directory.Exists(folder)) return false;   // ドライブごと消えた場合の起動フォルダ復帰は段7

        // その場の再表示なら、カーソルとマークは列挙後に読み直す（keepCursor）。
        // 遡った場合は消えたフォルダの名前に合わせ、マークは持ち越さない
        _ = cursorName is null
            ? OpenFolderAsync(folder, record: false, keepCursor: true)
            : OpenFolderAsync(folder, cursorName, record: false, restoreMarks: marks);
        return true;
    }

    /// <summary>
    /// ダイレクトジャンプ（`T` / 0x8327）。16.3 節のダイアログ。
    /// `Shift+Enter` でフォルダ参照（N-07）、`Ctrl+Enter`（「開く」）はエクスプローラーで開く（D-08）。
    /// </summary>
    private bool DirectJump()
    {
        using var dialog = new PathInputDialog(
            "ダイレクトジャンプ",
            heading: "",
            preset: _currentFolder,
            _history,
            _quickAccess,
            _currentFolder,
            acceptText: "ジャンプ(&J)",
            secondaryText: "開く(&O)",
            hints:
            [
                "※ Shift + Enter でフォルダを参照できます",
                "※ Ctrl + Enter で [開く] を実行します",
                "※ ジャンプは、指定のフォルダへ移動します",
            ]);

        if (dialog.ShowDialog(this) != DialogResult.OK) return true;

        // R-61: 相対パスはカレントフォルダ基準で解決する
        var target = PathResolver.Resolve(_currentFolder, dialog.Path);
        if (target is null || !Directory.Exists(target))
        {
            // R-48: 誤りでも操作は中止せず、入力しなおせるように出し直す
            MessageBox.Show(this, $"{dialog.Path} は見つかりません。", "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return DirectJump();
        }

        if (dialog.SecondaryChosen) OpenInExplorer(target);
        else _ = OpenFolderAsync(target);
        return true;
    }

    /// <summary>D-08:「開く」＝ 指定フォルダをエクスプローラーで開く。</summary>
    private void OpenInExplorer(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            MessageBox.Show(this, ex.Message, "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>`J`（0x82FC）。カーソル位置に番号付きポップアップを出す（N-06）。</summary>
    private bool ShowQuickAccess()
    {
        var items = _quickAccess.Items
            .Select(entry => (_quickAccess.LabelOf(entry), (Action)(() => GoQuickAccess(entry))))
            .ToList();

        // 設定・追加はキーに割り当てがなく、ReTAC はメニューバーを持たないのでここを入口にする。
        // 番号を振らない footer に置くのは、登録が 9 件を超えても番号を失わないようにするため。
        // 直接選ぶ手段はニーモニック（S / A）が受け持つ
        NumberedPopup.Show(_list, _list.PopupAnchor(), items, footer:
        [
            ("クイックアクセスの設定(&S)...", () => ShowQuickAccessSettings()),
            ("このフォルダを追加(&A)", () => AddCurrentToQuickAccess()),
        ]);
        return true;
    }

    private void GoQuickAccess(QuickAccessEntry entry)
    {
        if (Directory.Exists(entry.Path)) { _ = OpenFolderAsync(entry.Path); return; }

        // 16.2 節の実行時オプション。既定は OFF なので黙って消さずに知らせる
        if (_quickAccess.FixMissingAutomatically)
        {
            _quickAccess.RemoveAt(_quickAccess.Items.ToList().IndexOf(entry));
            return;
        }
        MessageBox.Show(this, $"{entry.Path} は見つかりません。", "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    /// <summary>クイックアクセスの設定・編集（0x82FA）。</summary>
    private bool ShowQuickAccessSettings()
    {
        using var dialog = new QuickAccessDialog(_quickAccess, _currentFolder);
        var result = dialog.ShowDialog(this);

        // V-13: このダイアログはキャンセルを持たない。追加・変更・削除・並べ替えはその場で
        // _quickAccess を書き換えるので、閉じ方によらず保存する。OK 限定にすると
        // 「閉じる」(Cancel) で抜けたときにアプリ内だけ消えて JSON に残る
        SaveSettings();
        if (result != DialogResult.OK) return true;

        if (dialog.ChosenPath is { } path) _ = OpenFolderAsync(path);
        return true;
    }

    /// <summary>クイックアクセスに追加（0x832C）。カーソルがフォルダならそれを、でなければカレントフォルダを。</summary>
    private bool AddCurrentToQuickAccess()
    {
        var target = _list.State.Cursor is { IsParent: false, Kind: EntryKind.Folder } folder
            ? folder.FullPath
            : _currentFolder;

        using var dialog = new QuickAccessEntryDialog(new QuickAccessEntry("", target), _currentFolder);
        if (dialog.ShowDialog(this) != DialogResult.OK) return true;
        if (!_quickAccess.Add(dialog.Entry))
        {
            MessageBox.Show(this, "このフォルダは登録済みです。", "ReTAC",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }
        SaveSettings();   // V-13
        return true;
    }

    /// <param name="record">F8 / F9 は false。前後の関係を壊さないよう履歴に積み直さない</param>
    private bool GoHistory(string? folder, bool record = true)
    {
        if (folder is null) return false;
        if (!Directory.Exists(folder))
        {
            // 存在しないフォルダは履歴から外す
            _history.Forget(folder);
            MessageBox.Show(this, $"{folder} は見つかりません。履歴から削除しました。",
                "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return true;
        }
        _ = OpenFolderAsync(folder, record: record);
        return true;
    }

    /// <summary>R-39-4: カレントドライブのルートへ。他のドライブへは移動しない。</summary>
    private bool GoRoot()
    {
        if (Path.GetPathRoot(_currentFolder) is not { Length: > 0 } root) return false;
        if (!FolderEnumerator.IsDriveRoot(_currentFolder)) _ = OpenFolderAsync(root);
        return true;
    }

    /// <param name="number">1 = A:、9 = I:</param>
    private bool GoDrive(int number)
    {
        if (number is < 1 or > 9) return false;
        // 準備されていないドライブは OpenFolderAsync がエラーを提示する（6 章）
        _ = OpenFolderAsync(LastFolderOn($"{(char)('A' + number - 1)}:\\"));
        return true;
    }

    /// <summary>
    /// そのドライブで最後にいたフォルダ。無ければ、あるいは消えていればルート。
    /// 卓駆はドライブを行き来しても前にいた場所へ戻る。作業の続きがそこにあるため。
    /// </summary>
    private string LastFolderOn(string root) =>
        _settings.DriveFolders.TryGetValue(root, out var last) && Directory.Exists(last) ? last : root;

    /// <summary>R-7: 最後に出した表示要求の番号。古い列挙の結果を捨てるために使う。</summary>
    private int _openGeneration;

    private static bool PathEquals(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b),
                      StringComparison.OrdinalIgnoreCase);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr window);

    private void GoParent()
    {
        // R-39: ドライブルートでは何も起きない。エラーも警告も出さない
        if (FolderEnumerator.ParentOf(_currentFolder) is not { } parent) return;
        var cameFrom = Path.GetFileName(Path.TrimEndingDirectorySeparator(_currentFolder));
        _ = OpenFolderAsync(parent, cameFrom);
    }

    /// <param name="selectName">R-33: 親へ戻ったとき、直前にいたフォルダの行にカーソルを合わせる。</param>
    /// <param name="record">履歴に積むか。F8 / F9 による移動だけ false（前後の関係を壊さない）</param>
    /// <param name="keepCursor">
    /// 再表示。カーソルとマークは列挙が終わった時点の状態から取り直す。
    /// 呼び出し時点で控えると、待っている間に動いたカーソルを古い位置へ書き戻してしまう
    /// （コンテキストメニューを開いたまま別の項目を右クリックしたときの戻り・ちらつき）
    /// </param>
    public async Task OpenFolderAsync(string folder, string? selectName = null, bool record = true,
                                      IReadOnlySet<string>? restoreMarks = null, bool keepCursor = false)
    {
        // ドライブの切り替えは、開けなければ移動しないことで分かる。エラーは出さない（R-32 の運用）
        var quiet = FolderEnumerator.IsDriveRoot(folder);
        // R-7: 遅いドライブの列挙が終わる前に次の要求が来ることがある。
        // 古い方の結果を反映すると、表示とカレントフォルダ・監視先が食い違う
        var generation = ++_openGeneration;
        IReadOnlyList<Entry> entries;
        try
        {
            // R-23: 列挙とソートで UI スレッドを占有しない
            var enumeration = Task.Run(() => FolderEnumerator.Enumerate(folder, _sortOrder, Include));

            // 6 章 / N-05: 応答しないネットワークドライブで待ち続けない。
            // ponytail: ファイルシステム I/O は中断できないので、待つのをやめるだけで
            // 走っているタスクは放置する。取り消しが要るなら列挙側を分割するしかない
            if (await Task.WhenAny(enumeration, Task.Delay(EnumerationTimeout)) != enumeration)
            {
                if (!quiet)
                    MessageBox.Show(this, $"{folder} が応答しません。", "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            entries = await enumeration;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 6 章: エラーを提示し、カレントフォルダは変更しない
            if (!quiet) MessageBox.Show(this, ex.Message, "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (generation != _openGeneration) return;   // R-7: 待っている間に新しい要求が来た

        if (keepCursor)
        {
            var current = _list.State;
            // B-09 / R-70: カーソルの項目が消えていたら、その上で残っている最も近い項目へ。
            // 名前だけで探すと、見つからないときに先頭（親フォルダ項目）へ飛んでいた
            var index = Selection.CursorRestore.IndexAfterReload(Selection.CursorRestore.NamesFromCursorUpward(current), entries);
            selectName = index < entries.Count && !entries[index].IsParent ? entries[index].Name : null;
            restoreMarks = current.Marks.Select(i => current.Entries[i].Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        Apply(folder, entries, selectName, record, restoreMarks);

        // その場の再表示（keepCursor）ではフォーカスに触らない。
        // 自動更新もこの経路を通るので、`L` でドライブバーへ移った直後に
        // カレントフォルダが変化すると、300ms 後にフォーカスがリストへ戻され、
        // 続けて押したドライブ名の文字（`LI` で I:）が届かなくなっていた（V-06）
        if (!keepCursor) _list.Focus();
    }

    /// <summary>オフスクリーン描画（--shot）用。列挙を同期で行う。</summary>
    public void OpenFolderSync(string folder) =>
        Apply(folder, FolderEnumerator.Enumerate(folder, _sortOrder, Include), null, record: true, restoreMarks: null);

    private void Apply(string folder, IReadOnlyList<Entry> entries, string? selectName, bool record,
                       IReadOnlySet<string>? restoreMarks)
    {
        // N-02: 移動の履歴とコピー先の履歴は共通のひとつ
        var previous = _currentFolder;
        if (record) _history.Visit(_currentFolder.Length > 0 ? _currentFolder : null, folder);
        _currentFolder = folder;
        Text = $"{folder} - ReTAC";   // X-02: 表示ワイルドカードは持たない

        // そのドライブへ戻ってきたときの着地点として覚えておく
        if (Path.GetPathRoot(folder) is { Length: > 0 } root) _settings.DriveFolders[root] = folder;

        _driveBar.SetCurrentPath(folder);   // 今いるドライブのボタンを凹ませる

        // R-33-2: 下へ移動したときは先頭（親フォルダ項目）
        var cursor = 0;
        if (selectName is not null)
        {
            var found = entries.ToList().FindIndex(e =>
                !e.IsParent && string.Equals(e.Name, selectName, StringComparison.OrdinalIgnoreCase));
            if (found >= 0) cursor = found;
        }

        _list.DropFolder = folder;   // R-78: ドロップの説明に使う
        var sameFolder = PathEquals(previous, folder);
        _list.SetEntries(entries, cursor, keepScroll: sameFolder);
        // R-80: 検索中の再表示は一致を求め直す。別のフォルダへ移ったら、元の位置は意味を失うので確定として閉じる
        if (sameFolder) _search.Rematch();
        else _search.Close(restore: false);
        WatchCurrentFolder();

        // 再表示ではマークを名前で戻す（R-11-4 の「フォルダ移動で解除」とは別）
        if (restoreMarks is { Count: > 0 })
        {
            for (var i = 0; i < entries.Count; i++)
                if (restoreMarks.Contains(entries[i].Name)) _list.State.ToggleMark(i);
            RefreshStatus();
        }
    }

    private void ScheduleAutoRefresh()
    {
        _autoRefresh.Stop();
        _autoRefresh.Start();
    }

    /// <summary>カレントフォルダの監視先を移す。監視できないドライブでも動作は続ける。</summary>
    private void WatchCurrentFolder()
    {
        try
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Path = _currentFolder;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or IOException)
        {
            // 監視できないだけで表示は成立する。`W` / `F5` の手動更新が残っている
            _watcher.EnableRaisingEvents = false;
        }
    }

    /// <summary>16.4 節の絞り込み。すべて ON なら判定そのものを省く。</summary>
    private bool Include(Entry entry) =>
        _fileTypes.AcceptsEverything || _fileTypes.Accepts(entry, ShellFileType.HasAssociation(entry.Extension));

    /// <summary>State を直接いじった後（--shot など）に呼ぶ。通常はイベント経由で自動更新される。</summary>
    public void RefreshStatus() => _statusBar.Update(_list.State, _currentFolder);
}
