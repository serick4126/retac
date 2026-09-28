using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ReTAC.Updater.Core;
using ReTAC.Updater.Net;

namespace ReTAC.Updater;

/// <summary>
/// R-109: アップデータの画面。今していることを 1 行で出し、処理中はマーキーを動かす。
/// R-109-6: 画面はいつでも応答し、閉じられる。外の相手（GitHub・ReTAC・利用者）を待つところには、上限か押せるボタンを付ける。
/// 文は短く（U13）。
/// </summary>
internal sealed class UpdaterForm : Form
{
    /// <summary>画面の題。二重に起動されたとき、先の画面を見つけて前面に出すのにも使う。</summary>
    public const string Title = "ReTAC の更新";

    private const string ReleasesPage = "https://github.com/serick4126/retac/releases";
    private static readonly TimeSpan QuitWait = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StallLimit = TimeSpan.FromSeconds(60);

    private readonly string _install;
    private readonly string _normalized;
    private readonly string _work;
    private readonly string _self;
    private readonly Options _options;

    /// <summary>画面の排他。画面を閉じて作業だけが残るときは手放す（次のアップデータが「入れ替えています」を出せるように）。</summary>
    private readonly NamedLock _uiLock;

    private readonly Label _status = new() { AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
    private readonly Label _detail = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, 8) };
    private readonly ProgressBar _bar = new() { Style = ProgressBarStyle.Marquee, Dock = DockStyle.Fill, Height = 14, MarqueeAnimationSpeed = 30 };
    private readonly LinkLabel _link = new() { AutoSize = true, Text = "変更内容を GitHub で見る", Visible = false, Margin = new Padding(0, 6, 0, 0) };
    private readonly FlowLayoutPanel _buttons = new()
    {
        FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 12, 0, 0),
    };

    private readonly CancellationTokenSource _cancel = new();
    private TaskCompletionSource<string>? _choice;

    /// <summary>ReTAC を 1 つ以上終了させたか（R-109-4: 最後に起動し直す条件の 1 つ）。</summary>
    private bool _stoppedAnyReTac;

    /// <summary>置き換えが動いている間に画面が閉じられた。作業は止めず、終わったらプロセスを終える（R-109-6）。</summary>
    private bool _exitWhenWorkDone;
    private bool _workRunning;
    private bool _replacingStarted;
    private bool _launched;

    /// <summary>結果を出し終えた。この後に画面を閉じても ReTAC は起動しない（起動するかは結果の前に決め済み）。</summary>
    private bool _finished;

    public UpdaterForm(string install, string normalized, string work, string self, Options options, NamedLock uiLock)
    {
        _uiLock = uiLock;
        _install = install;
        _normalized = normalized;
        _work = work;
        _self = self;
        _options = options;

        Text = Title;
        Icon = Icon.ExtractAssociatedIcon(self);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        Font = SystemFonts.MessageBoxFont;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 420));
        _status.MaximumSize = new Size(420, 0);
        _detail.MaximumSize = new Size(420, 0);
        layout.Controls.Add(_status);
        layout.Controls.Add(_detail);
        layout.Controls.Add(_bar);
        layout.Controls.Add(_link);
        layout.Controls.Add(_buttons);
        Controls.Add(layout);

        _link.LinkClicked += (_, _) => OpenReleasesPage();
        Shown += async (_, _) => await RunAsync();
    }

    // ---- 流れ ----

    private async Task RunAsync()
    {
        try
        {
            await FlowAsync();
        }
        catch (ClosedByUser)
        {
            // 画面を閉じた後に、待っていた読み取りが戻ってきた。続き（終了の依頼・置き換え）は行わない
        }
        catch (Exception ex)
        {
            // 画面を閉じた後に、待っていた処理が戻ってきた
            if (IsDisposed || _exitWhenWorkDone || _closing) return;
            // R-109-6: 想定外の例外も画面に出して終わる。置き換えより前なら何も変わっていない
            _stoppedAnyReTac |= await Background(AnyStopped);
            LaunchIfNeeded(replaced: false);
            _following ??= FollowQuitTargetsAsync();
            Finish(Outcome.Message(ReplaceResult.Unchanged, Reason.Unexpected, _launched, _launchOk, ""), ex.Message);
        }
    }

    private async Task FlowAsync()
    {
        var reTacPath = Path.Combine(_install, Protocol.ReTacExe);
        Busy("ReTAC を確かめています", NoButtons);

        // §3.3 の 4: OS から戻らない操作で止まったプロセスが置き換えの排他を持っていれば、ここで止まる
        if (!await Io(() => NamedLock.IsFree(InstallFolder.ReplaceLockName(_normalized))))
        {
            Finish(Outcome.ReasonText(Reason.OtherUpdater) + "。");
            return;
        }
        if (!await Io(() => File.Exists(reTacPath)))
        {
            Finish("ReTAC.exe が見つかりません。", _install);
            return;
        }

        var reTac = await Io(() => Versions.OfFile(reTacPath));
        var updater = await Io(() => Versions.OfFile(Path.Combine(_install, Protocol.UpdaterExe)));
        if (reTac is null)
        {
            Finish("ReTAC.exe のバージョンを読み取れません。");
            return;
        }

        // 版の確認
        Version latest;
        if (_options.LocalZip is not null)
        {
            latest = Version.Parse(_options.LocalZipVersion!);
        }
        else
        {
            Busy("新しいバージョンを確認しています", Cancelable);
            using var github = new GitHubReleases();
            var tag = await github.LatestTagAsync(_cancel.Token);
            if (!tag.Ok)
            {
                var failure = tag.Failure!;
                Finish(Outcome.Message(ReplaceResult.Unchanged, failure.Reason, false, false, "", failure.RetryAt), failure.Detail);
                return;
            }
            if (Versions.ParseTag(tag.Value) is not { } parsed)
            {
                // 利用者の側でできることは無いので、何が起きたかだけを伝え、受け取った値は詳細の行に出す
                Finish("新しいバージョンがあるか確認できませんでした。ReTAC は変更していません。",
                       $"GitHub のバージョン番号を読み取れません: {tag.Value}");
                return;
            }
            latest = parsed;
        }
        if (Versions.IsUpToDate(latest, reTac, updater))
        {
            Finish($"最新のバージョン（v{latest}）を使っています。");
            return;
        }

        // 確認
        var version = $"{latest.Major}.{latest.Minor}.{latest.Build}";
        Idle($"新しいバージョン v{version} があります。{Environment.NewLine}現在のバージョンは v{reTac} です。{Environment.NewLine}[更新] を押すと ReTAC が再起動します。");
        _link.Visible = true;
        if (await AskAsync(("update", "更新"), ("cancel", "中止")) != "update")
        {
            Close();
            return;
        }
        _link.Visible = false;

        // ReTAC の終了（§5.2）
        if (!await QuitReTacAsync())
        {
            LaunchIfNeeded(replaced: false);
            Finish(Outcome.Message(ReplaceResult.Unchanged, Reason.Cancelled, _launched, _launchOk, ""));
            // 受け付けた ReTAC が K-5 の確認を出していれば、利用者が後から「終了」を選ぶかもしれない。見届けて起動し直す
            _following ??= FollowQuitTargetsAsync();
            return;
        }

        // 置き換え（§6・§7）
        Busy("準備しています", NoButtons);
        var writable = await Io(CanWrite);
        var (report, leftovers) = writable ? await ReplaceHereAsync(version) : await ReplaceElevatedAsync(version, latest);
        if (_exitWhenWorkDone)
        {
            // 画面は閉じられている。作業が終わったので、ここでプロセスを終える
            Application.ExitThread();
            return;
        }

        LaunchIfNeeded(replaced: report.Replaced > 0 || report.Result != ReplaceResult.Unchanged);
        var message = Outcome.Message(report.Result, report.Reason, _launched, _launchOk, "v" + version, report.RetryAt);
        var detail = report.Detail;
        if (leftovers.Count > 0)
        {
            // フォルダ名は詳細の行に出す（文の後ろに括弧を付けて「。」で終えない）
            message += Environment.NewLine + "前回の更新で残ったフォルダがあります。不要なら消してください。";
            detail = string.Join(Environment.NewLine, new[] { detail, string.Join(Environment.NewLine, leftovers) }.Where(d => !string.IsNullOrEmpty(d)));
        }
        Finish(message, detail);
    }

    // ---- ReTAC の終了（R-109-3）----

    private sealed class Target
    {
        public Target(ReTacProcess process) => Process = process;
        public ReTacProcess Process { get; }
        public DateTime? AcceptedAt { get; set; }

        /// <summary>
        /// 終了依頼を送った（返事を待たずに立てる）。送っている最中に画面を閉じても、見届けの対象から漏らさないため。
        /// </summary>
        public bool Requested { get; set; }

        /// <summary>送っている最中の依頼。見届けは、これの返事が返ってから始める（問い合わせが依頼を追い越さないように）。</summary>
        public Task? InFlight { get; set; }
    }

    /// <summary>終了を頼んだ ReTAC。画面を閉じたときに、終了させたものがあれば起動し直すために持つ。</summary>
    private List<Target> _quitTargets = new();

    /// <summary>
    /// 終了を頼んだ ReTAC のうち、終わったものがあるか。頼んでいない ReTAC が自分で終わっても数えない。
    /// プロセスを照会するので作業側で呼ぶ（<see cref="Io{T}"/>）。
    /// </summary>
    private bool AnyStopped() => _quitTargets.Any(t => t.Requested && !ReTacProcesses.IsRunning(t.Process.Id));

    /// <summary>受け付けた ReTAC の見届け。始めていなければ null。</summary>
    private Task? _following;

    /// <summary>
    /// R-109-3 / R-109-6: 終了を頼んだ ReTAC を見届ける。K-5 の確認を出している間にアップデータを中止・閉じても、利用者が後から
    /// 「終了」を選べば ReTAC は終わる。終わったら起動し直す（ReTAC を使えない状態のまま残さない）。
    /// 取りやめたかどうかは ReTAC に問い合わせて決める（時間やウィンドウの様子からは決めない）。照会はすべて作業側で行う。
    /// </summary>
    private async Task FollowQuitTargetsAsync()
    {
        var targets = _quitTargets.ToList();
        var requested = targets.Where(t => t.Requested).ToList();
        if (requested.Count == 0) return;   // 頼んでいなければ、終わっても起動し直さない

        // 送っている最中の依頼の返事を先に待ってから問い合わせる（PendingQuitWatch.FollowAsync）。
        // 1 巡の問い合わせは ReTAC ごとに同時に行い、答えが無い状態だけが続けば実際に経った 60 秒で打ち切る（INV-UPDATER-NO-DEADLOCK）
        var ids = requested.Select(t => t.Process.Id).ToList();
        var end = await PendingQuitWatch.FollowAsync(
            requested.Select(t => t.InFlight).OfType<Task>(),
            ids.Count,
            i =>
            {
                _options.SlowIo();
                return ReTacProcesses.State(ids[i]);
            },
            () => Thread.Sleep(500));

        var states = await Background(() => targets.Select(t => (t.Requested, Exited: !ReTacProcesses.IsRunning(t.Process.Id))).ToList());
        if (PendingQuitWatch.ShouldRelaunch(end, states))
        {
            _stoppedAnyReTac = true;
            Launch();
        }
    }

    /// <summary>インストール先の ReTAC をすべて終了させる。やめたら false。強制終了はしない。</summary>
    private async Task<bool> QuitReTacAsync()
    {
        var targets = (await Io(() => ReTacProcesses.Find(_normalized))).Select(p => new Target(p)).ToList();
        _quitTargets = targets;
        if (targets.Count == 0) return true;

        var unknown = targets.Any(t => !t.Process.PathKnown) ? "状態を確かめられない ReTAC があります。" : null;
        var heading = targets.Count >= 2 ? $"ReTAC が {targets.Count} 個起動しています。すべて終了します。" : "ReTAC を終了しています。";

        while (true)
        {
            var alive = await Io(() => targets.Where(t => ReTacProcesses.IsRunning(t.Process.Id)).ToList());
            // 起動し直す条件の「ReTAC を終了させた」は、頼んだものが終わったときだけ（頼む前に自分で終わったものは数えない）
            _stoppedAnyReTac = targets.Any(t => t.Requested && !alive.Contains(t));
            if (alive.Count == 0) return true;

            // 受け付けた ReTAC には送り直さない（K-5 の確認で「やめる」を選んだ利用者に、確認を繰り返し出さないため）
            var refused = false;
            var silent = false;
            foreach (var target in alive.Where(t => t.AcceptedAt is null))
            {
                // [中止] の後に終了を頼まない（閉じたときは Io が打ち切っている）
                if (_cancel.IsCancellationRequested) return false;
                target.Requested = true;
                var sending = Task.Run(() => ReTacProcesses.RequestQuit(target.Process.Id));
                target.InFlight = sending;
                switch (await sending)
                {
                    case QuitReply.Accepted: target.AcceptedAt = DateTime.UtcNow; break;
                    case QuitReply.Refused: refused = true; break;
                    default: silent = true; break;
                }
            }

            var stuck = alive.Where(t => t.AcceptedAt is { } at && DateTime.UtcNow - at > QuitWait).ToList();
            if (stuck.Count > 0)
            {
                Idle("ReTAC が終了しません。", unknown);
                if (await AskAsync(("again", "再試行"), ("cancel", "中止")) != "again") return false;
                foreach (var target in stuck) target.AcceptedAt = null;   // 押したときだけ送り直す
                continue;
            }

            var line = refused ? "ReTAC のダイアログやファイル操作を終えてください。"
                     : silent ? "ReTAC が応答しません。"
                     : "ReTAC の終了を待っています。";
            Busy(heading + Environment.NewLine + line, Cancelable, unknown);
            try { await Task.Delay(TimeSpan.FromSeconds(5), _cancel.Token); }
            catch (TaskCanceledException) { return false; }
        }
    }

    // ---- 置き換え（R-109-4）----

    private bool CanWrite()
    {
        try
        {
            StagingFolder.Create(_install).Cleanup();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// 昇格していないとき。作業用のスレッドで置き換える。置き換えの排他はこのスレッドが取って手放す。
    /// 画面が閉じられてもこのスレッドは止めない（前景のスレッドなので、終わるまでプロセスが残る）。
    /// </summary>
    private async Task<(ReplaceReport, IReadOnlyList<string>)> ReplaceHereAsync(string version)
    {
        var lastProgress = DateTime.UtcNow;
        var lastText = "";
        void Progress(string stage, string? file)
        {
            lastProgress = DateTime.UtcNow;
            if (stage == "入れ替え") _replacingStarted = true;
            var text = StageText(stage, file);
            if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(() =>
            {
                if (_exitWhenWorkDone) return;
                lastText = text;
                // ダウンロードまでは中止できる。照合の後は入れ替えに入るので、[中止] を外す
                Busy(text, stage == "ダウンロード" ? Cancelable : NoButtons);
            }));
        }

        var fetch =
#if DEBUG
            _options.LocalZip is not null
                ? Fetchers.FromLocalZip(_options.LocalZip, _options.LocalZipSha256!, _work, Progress) :
#endif
            Fetchers.FromGitHub(version, _work, Progress, _cancel.Token);

        var request = new ReplaceRequest(_install, fetch)
        {
            LockName = InstallFolder.ReplaceLockName(_normalized),
            Progress = Progress,
        };
        _options.ApplyDebugHooks(request, _work);

        var done = new TaskCompletionSource<ReplaceReport>();
        var worker = new Thread(() =>
        {
            ReplaceReport report;
            try { report = Replacer.Run(request); }
            catch (Exception ex) { report = new ReplaceReport(ReplaceResult.Unchanged, Reason.Unexpected, 0, Array.Empty<string>(), ex.Message); }
            // 画面が閉じられていても、この続き（FlowAsync）は画面のスレッドで走り、そこでプロセスを終える
            done.TrySetResult(report);
        })
        { IsBackground = false, Name = "ReTAC.Updater replace" };
        _workRunning = true;
        worker.Start();

        Busy("準備しています", Cancelable);
        await WatchStallAsync(done.Task, () => lastProgress, () => lastText);
        var result = await done.Task;
        _workRunning = false;
        return (result, result.Leftovers);
    }

    /// <summary>
    /// 書き込めないとき。自分を runas で起動し、置き換え（取得・照合・展開を含む）だけを行わせる。
    /// 渡すのは版番号だけ。結果は終了コードで受け取り、読めなければインストール先の実物のハッシュで判定し直す（progress.txt は根拠にしない）。
    /// </summary>
    private async Task<(ReplaceReport, IReadOnlyList<string>)> ReplaceElevatedAsync(string version, Version target)
    {
        var progressFile = Path.Combine(_work, "progress.txt");
        var leftovers = await Io(() => StagingFolder.FindLeftovers(_install, null));
        var before = await Io(Hashes);
        await Io(() =>
        {
            try { File.Delete(progressFile); } catch (IOException) { }
            return true;
        });

        var args = new[] { Protocol.ReplaceSwitch, _install, version, _work }.Concat(_options.DebugArgs);
        Process child;
        try
        {
            Busy("管理者の許可を待っています", NoButtons);
            child = Process.Start(new ProcessStartInfo(_self, Options.Join(args)) { UseShellExecute = true, Verb = "runas" })!;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_CANCELLED)
        {
            return (new ReplaceReport(ReplaceResult.Unchanged, Reason.ElevationDenied, 0, leftovers, null), leftovers);
        }

        using (child)
        {
            _workRunning = true;
            _elevatedRunning = true;
            _replacingStarted = true;   // どこまで進んだか親からは分からない。作業中に閉じるときは ReTAC を起動し直す
            try
            {
                // 進み具合の読み取りも作業側で行う（画面のスレッドでファイルを読まない）。表示と「止まっていないか」にだけ使う
                var lastRaw = "";
                var lastProgress = DateTime.UtcNow;
                var exited = Task.Run(() => child.WaitForExit());
                var polling = Task.Run(async () =>
                {
                    while (!exited.IsCompleted)
                    {
                        var raw = ReadProgress(progressFile);
                        if (raw != lastRaw)
                        {
                            lastRaw = raw;
                            lastProgress = DateTime.UtcNow;
                            if (ProgressLine.Parse(raw) is { IsEnd: false } line && IsHandleCreated) BeginInvoke(new Action(() =>
                            {
                                if (!_exitWhenWorkDone) Busy(StageText(line.Stage, line.File), NoButtons);
                            }));
                        }
                        await Task.Delay(500).ConfigureAwait(false);
                    }
                });
                Busy("ファイルを入れ替えています", NoButtons);
                await WatchStallAsync(exited, () => lastProgress, () => lastRaw);
                await exited;
                await polling;
            }
            finally
            {
                // 昇格したプロセスは終わった。結果の画面を閉じるときに「作業中」として ReTAC を起動しないように戻す
                _workRunning = false;
                _elevatedRunning = false;
            }
            if (_exitWhenWorkDone) return (new ReplaceReport(ReplaceResult.Unchanged, Reason.None, 0, leftovers, null), leftovers);

            // 詳細とやり直せる時刻は、昇格したプロセスが最後に progress.txt に書いたもの（表示にだけ使う）
            var end = ProgressLine.Parse(await Io(() => ReadProgress(progressFile)));
            var detail = end is { IsEnd: true } ? end.File : null;
            var retryAt = end is { IsEnd: true } ? end.RetryAt : null;

            if (ExitCodes.Decode(child.ExitCode) is { } decoded)
                return (new ReplaceReport(decoded.Result, decoded.Reason, decoded.Result == ReplaceResult.Unchanged ? 0 : 1, leftovers,
                                          detail, retryAt), leftovers);

            var after = await Io(Hashes);
            var reTacAfter = await Io(() => Versions.OfFile(Path.Combine(_install, Protocol.ReTacExe)));
            var updaterAfter = await Io(() => Versions.OfFile(Path.Combine(_install, Protocol.UpdaterExe)));
            var result = Versions.Rejudge(before, after, target, reTacAfter, updaterAfter);
            return (new ReplaceReport(result, Reason.Unexpected, result == ReplaceResult.Unchanged ? 0 : 1, leftovers,
                                      $"終了コード {child.ExitCode}"), leftovers);
        }
    }

    /// <summary>
    /// R-109-6: 同じ段のまま 60 秒進まなければ、止まっていると出して [閉じる] を出す。
    /// [閉じる] は作業を止めない。画面を閉じ、作業が終わったらプロセスを終える。
    /// </summary>
    private async Task WatchStallAsync(Task work, Func<DateTime> lastProgress, Func<string> lastText)
    {
        var shown = false;
        while (!work.IsCompleted)
        {
            await Task.WhenAny(work, Task.Delay(1000));
            if (work.IsCompleted || _exitWhenWorkDone) break;
            if (DateTime.UtcNow - lastProgress() > StallLimit)
            {
                if (!shown)
                {
                    shown = true;
                    Idle("ファイルの書き込みが止まっています。", lastText());
                    SetButtons(("close", "閉じる"));
                    _choice = new TaskCompletionSource<string>();
                    _ = _choice.Task.ContinueWith(_ => BeginInvoke(new Action(Close)), TaskScheduler.Default);
                }
            }
            else if (shown)
            {
                shown = false;
                Busy(lastText(), NoButtons);
            }
        }
    }

    /// <summary>昇格したプロセスが置き換えている。親は画面を閉じたらそのまま終わってよい（ファイル操作は昇格したプロセスが最後まで行う）。</summary>
    private bool _elevatedRunning;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_exitWhenWorkDone)
        {
            base.OnFormClosing(e);
            return;
        }
        // ここではプロセスを照会しない（画面のスレッドで止まらないように）。ReTAC の終了の段は、そこで終わりを見届け済み
        var launch = Outcome.LaunchOnClose(_workRunning, _finished, _stoppedAnyReTac, _replacingStarted);

        if (_elevatedRunning)
        {
            // 昇格したプロセスが最後まで行う。親はそのまま終わってよい
            _exitWhenWorkDone = true;
            if (launch) Launch();
            base.OnFormClosing(e);
            return;
        }
        if (_workRunning)
        {
            // 作業を止めない。ダウンロードの最中なら中止させる（それ以降は取り消せない）。
            // ReTAC は起動し直しておく（入れ替えの途中でも正規の名前には旧版か新版の完全なファイルがある）
            _cancel.Cancel();
            _exitWhenWorkDone = true;
            if (launch) Launch();
            e.Cancel = true;
            Hide();
            // 画面はもう無い。次に起動したアップデータには、置き換えの排他（作業用のスレッドが持つ）で「入れ替えています」と出させる
            _uiLock.Dispose();
            return;
        }
        // 待っている処理（GitHub・ReTAC の終了待ち）を中止し、流れの続きを打ち切る（R-109-6）。
        // 作業が動いているとき（上の 2 つ）は印を立てない。作業の後の「プロセスを終える」まで打ち切ってしまうため
        _closing = true;
        _cancel.Cancel();
        if (_quitTargets.Count == 0)
        {
            base.OnFormClosing(e);
            return;
        }
        // 終了を頼んだ ReTAC がある。画面は閉じ、終わったものがあれば起動し直してから（受け付けたものは見届けてから）プロセスを終える
        _exitWhenWorkDone = true;
        e.Cancel = true;
        Hide();
        _uiLock.Dispose();
        _ = ExitAfterFollowingAsync();
    }

    private async Task ExitAfterFollowingAsync()
    {
        try { await (_following ??= FollowQuitTargetsAsync()); }
        finally { Application.ExitThread(); }
    }

    // ---- ReTAC の起動（R-109-4）----

    private bool _launchOk;

    /// <summary>ReTAC を終了させたか、入れ替えを行ったときだけ起動する。昇格していないこのプロセスから起動する（UIPI）。</summary>
    private void LaunchIfNeeded(bool replaced)
    {
        if (Outcome.ShouldLaunch(_stoppedAnyReTac, replaced ? 1 : 0)) Launch();
    }

    /// <summary>ReTAC を 1 回だけ起動する。</summary>
    private void Launch()
    {
        if (_launched) return;
        _launched = true;
        try
        {
            var path = Path.Combine(_install, Protocol.ReTacExe);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = _install })?.Dispose();
            _launchOk = true;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            _launchOk = false;
        }
    }

    // ---- 画面の部品 ----

    private static readonly (string, string)[] NoButtons = Array.Empty<(string, string)>();
    private static readonly (string, string)[] Cancelable = { ("cancel", "中止") };

    private void Busy(string text, (string Id, string Label)[] buttons, string? detail = null)
    {
        if (IsDisposed) return;
        _status.Text = text;
        _detail.Text = detail ?? "";
        _detail.Visible = detail is not null;
        _bar.Visible = true;
        SetButtons(buttons);
        // 処理中の [中止] は、待っている処理を取り消す
        _choice = new TaskCompletionSource<string>();
        _ = _choice.Task.ContinueWith(t => { if (t.Result == "cancel") _cancel.Cancel(); }, TaskScheduler.Default);
    }

    private void Idle(string text, string? detail = null)
    {
        if (IsDisposed) return;
        _status.Text = text;
        _detail.Text = detail ?? "";
        _detail.Visible = !string.IsNullOrEmpty(detail);
        _bar.Visible = false;
        SetButtons();
    }

    private void Finish(string text, string? detail = null)
    {
        if (IsDisposed) return;
        _finished = true;
        Idle(text, detail);
        _link.Visible = false;
        SetButtons(("close", "閉じる"));
        _choice = new TaskCompletionSource<string>();
        _ = _choice.Task.ContinueWith(_ => BeginInvoke(new Action(Close)), TaskScheduler.Default);
    }

    private Task<string> AskAsync(params (string Id, string Label)[] buttons)
    {
        SetButtons(buttons);
        _choice = new TaskCompletionSource<string>();
        return _choice.Task;
    }

    private void SetButtons(params (string Id, string Label)[] buttons)
    {
        _buttons.Controls.Clear();
        // RightToLeft なので、並べたい順の逆に足す（主のボタンを左に）
        foreach (var (id, label) in buttons.Reverse())
        {
            var button = new Button { Text = label, AutoSize = true, MinimumSize = new Size(88, 0) };
            button.Click += (_, _) => _choice?.TrySetResult(id);
            _buttons.Controls.Add(button);
        }
        if (_buttons.Controls.Count > 0)
        {
            AcceptButton = (Button)_buttons.Controls[_buttons.Controls.Count - 1];
            _buttons.Controls[_buttons.Controls.Count - 1].Focus();
        }
    }

    /// <summary>
    /// R-109-6: ファイルやプロセスを調べる処理は、画面のスレッドではなく作業側で行う。ストレージやフィルタードライバーの都合で
    /// 戻らないことがあっても、画面は応答し、閉じられる（閉じればこの読み取りは置き去りにする。背景のスレッドなので終了を妨げない）。
    /// </summary>
    /// <remarks>
    /// 読み取りの間に画面が閉じられたら、戻ってきた後に流れを打ち切る（<see cref="ClosedByUser"/> を投げる）。
    /// 閉じた後に ReTAC へ終了を頼んだり、置き換えに進んだりしないため。
    /// </remarks>
    private async Task<T> Io<T>(Func<T> read)
    {
        var result = await Background(read);
        if (_closing) throw new ClosedByUser();
        return result;
    }

    /// <summary>画面を閉じた後にも使う読み取り（見届け）。流れを打ち切らない。</summary>
    private Task<T> Background<T>(Func<T> read) => Task.Run(() =>
    {
        _options.SlowIo();
        return read();
    });

    /// <summary>画面が閉じられたので流れを打ち切る。</summary>
    private sealed class ClosedByUser : Exception
    {
    }

    /// <summary>利用者が画面を閉じた（[中止] とは違い、流れの続きは行わない）。</summary>
    private bool _closing;

    private static string StageText(string stage, string? file) => stage switch
    {
        "準備" => "準備しています",
        "ダウンロード" => file is null ? "ダウンロードしています" : $"ダウンロードしています（{file}）",
        "照合" => "ダウンロードしたファイルを確かめています",
        "展開" => "ファイルを取り出しています",
        "入れ替え" => $"ファイルを入れ替えています（{file}）",
        "片付け" => "片付けています",
        _ => "ファイルを入れ替えています",
    };

    private Dictionary<string, string?> Hashes()
    {
        var hashes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in Distribution.Names)
        {
            var path = Path.Combine(_install, name);
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                hashes[name] = ZipPackage.Sha256(stream);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { hashes[name] = null; }
        }
        return hashes;
    }

    private static string ReadProgress(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }

    private static void OpenReleasesPage()
    {
        try { Process.Start(new ProcessStartInfo(ReleasesPage) { UseShellExecute = true })?.Dispose(); }
        catch (Win32Exception) { }
    }

    private const int ERROR_CANCELLED = 1223;
}
