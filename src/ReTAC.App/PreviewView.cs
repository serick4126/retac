using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ReTAC.Shell;
using Timer = System.Windows.Forms.Timer;

namespace ReTAC.App;

/// <summary>
/// R-99: 左パネルのプレビュービュー。Windows に登録されたプレビューハンドラーで、カーソル位置のファイル 1 件を出す。
/// COM は PreviewSession（要求ごとの STA）が持ち、ここは待ち時間・世代・状態の文言・対象名・子ホストを受け持つ。
/// 失敗してもモーダルは出さず、領域の中に文言を出す（Q30）。
/// </summary>
public sealed class PreviewView : UserControl
{
    private const string NoTargetText = "プレビューするファイルを選んでください";
    private const string NoHandlerText = "このファイルはプレビューできません";
    private const string LoadingText = "読み込んでいます…";
    private const string FailedText = "プレビューを読み込めませんでした";

    private readonly Panel _area = new() { Dock = DockStyle.Fill };
    private readonly TableLayoutPanel _statusPanel = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
    private readonly Label _status = new() { AutoSize = true, Anchor = AnchorStyles.None, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Button _retry = new() { Text = "再試行", AutoSize = true, Anchor = AnchorStyles.None, TabStop = false, Visible = false };
    /// <summary>Q36: 対象名はプレビューと重ねず、最下段の独立した 1 行に出す。</summary>
    private readonly Label _name = new() { Dock = DockStyle.Bottom, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolTip _tip = new();
    private readonly Timer _delay = new() { Interval = PreviewTarget.KeyboardDelayMs };
    private readonly Timer _loading = new() { Interval = PreviewTarget.LoadingNoticeMs };

    /// <summary>次に読む対象（キーボードの待機中はまだ読んでいない）。</summary>
    private string? _path;
    /// <summary>今表示している・読んでいる対象。同じ対象での再表示（自動更新）では読み直さない。</summary>
    private string? _loadedPath;
    private int _generation;
    private (PreviewSession Session, Panel Host)? _shown;
    private (PreviewSession Session, Panel Host, int Generation)? _pending;

    public event EventHandler? FocusFileViewRequested;

    public PreviewView()
    {
        SetStyle(ControlStyles.Selectable, true);   // Tab で入れるように（Q12）
        TabStop = true;
        _statusPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _statusPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _statusPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _statusPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _statusPanel.Controls.Add(_status, 0, 1);
        _statusPanel.Controls.Add(_retry, 0, 2);
        _area.Controls.Add(_statusPanel);
        Controls.Add(_area);
        Controls.Add(_name);
        UpdateNameHeight();
        _name.FontChanged += (_, _) => UpdateNameHeight();

        _delay.Tick += (_, _) => LoadTarget();
        _loading.Tick += (_, _) =>
        {
            _loading.Stop();
            SetStatus(LoadingText);
        };
        _retry.Click += (_, _) =>
        {
            _loadedPath = null;
            LoadTarget();
        };
        _area.Resize += (_, _) =>
        {
            _shown?.Session.SetSize(_area.ClientSize);
            _pending?.Session.SetSize(_area.ClientSize);
        };
        SetStatus(NoTargetText);
        Disposed += (_, _) =>
        {
            _delay.Dispose();
            _loading.Dispose();
            _tip.Dispose();
        };
    }

    /// <summary>
    /// 対象を知らせる。immediate（クリック・再表示）ならすぐ読み、そうでなければ 250ms 止まってから読む（Q31）。
    /// 待っている間は前のプレビューと対象名を残す（Q38）。
    /// </summary>
    public void SetTarget(string? path, bool immediate)
    {
        _path = path;
        _delay.Stop();
        if (path is not null && string.Equals(path, _loadedPath, StringComparison.OrdinalIgnoreCase)) return;
        if (immediate) LoadTarget();
        else _delay.Start();
    }

    /// <summary>
    /// R-99 / Q46: 見えなくなった（別ビュー・左パネル非表示・最小化）。ハンドラーを解放してファイルを離す。
    /// 次に SetTarget されたときに今のカーソルから読み直す。
    /// </summary>
    public void Unload()
    {
        _delay.Stop();
        _loading.Stop();
        ReleasePending();
        ReleaseShown();
        _loadedPath = null;
        SetStatus("");
    }

    /// <summary>ウィンドウを閉じる。完了済みのワーカーだけを待ち、応答しないワーカーは待たない。</summary>
    public void Shutdown()
    {
        var sessions = new[] { _shown?.Session, _pending?.Session }.OfType<PreviewSession>().ToList();
        Unload();
        foreach (var session in sessions) session.WaitIfIdle(PreviewTarget.ShutdownWaitMs);
    }

    private void LoadTarget()
    {
        _delay.Stop();
        _loading.Stop();
        var path = _path;
        SetName(path);
        ReleasePending();
        ReleaseShown();   // 新しい対象を読み始めるときに前のハンドラーを終える（§6）
        _loadedPath = path;
        if (path is null)
        {
            SetStatus(NoTargetText);
            return;
        }
        if (PreviewSession.FindHandler(path) is not { } clsid)
        {
            SetStatus(NoHandlerText);   // ハンドラーが無いのは一時的な失敗ではないので、再試行は出さない（Q58）
            return;
        }

        SetStatus("");
        var generation = ++_generation;
        // 要求ごとに子ホストを分ける。止まった古いハンドラーが、新しい表示の上に描かないように
        var host = new Panel { Dock = DockStyle.Fill, Visible = false };
        _area.Controls.Add(host);
        var session = PreviewSession.Start(clsid, path, host.Handle, _area.ClientSize,
            completed: ok => Post(() => Completed(generation, ok)),
            tabPressed: () => Post(() => FocusFileViewRequested?.Invoke(this, EventArgs.Empty)));
        _pending = (session, host, generation);
        _loading.Start();   // 読込中の表示は要求の処理開始から数える（技術ゲート）
    }

    private void Completed(int generation, bool ok)
    {
        // 古い要求の結果は捨てる（その要求は、新しい要求を始めたときに解放を頼んである）
        if (_pending is not var (session, host, current) || current != generation) return;
        _pending = null;
        _loading.Stop();
        if (!ok)
        {
            Release(session, host);
            SetStatus(FailedText, retry: true);
            return;
        }
        SetStatus("");
        host.Visible = true;
        host.BringToFront();
        _shown = (session, host);
    }

    private void ReleaseShown()
    {
        if (_shown is not var (session, host)) return;
        _shown = null;
        Release(session, host);
    }

    private void ReleasePending()
    {
        if (_pending is not var (session, host, _)) return;
        _pending = null;
        Release(session, host);
    }

    /// <summary>子ホストは、ハンドラーが Unload を済ませてから捨てる（先に捨てると、ハンドラーの窓が親を失う）。</summary>
    private void Release(PreviewSession session, Panel host)
    {
        host.Visible = false;
        session.Release(released: () => Post(host.Dispose));
    }

    /// <summary>ワーカーの STA から UI スレッドへ戻す。閉じた後なら捨てる。</summary>
    private void Post(Action action)
    {
        try
        {
            if (!IsDisposed && IsHandleCreated) BeginInvoke(action);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) { }
    }

    private void SetStatus(string text, bool retry = false)
    {
        _status.Text = text;
        _retry.Visible = retry;
        _statusPanel.BringToFront();
    }

    /// <summary>Q54: 読もうとした名前を、成功・非対応・失敗のどれでも出す。フォルダ・親フォルダ行では空欄。</summary>
    private void SetName(string? path)
    {
        _name.Text = path is null ? "" : Path.GetFileName(path);
        _tip.SetToolTip(_name, path ?? "");
    }

    private void UpdateNameHeight() => _name.Height = _name.Font.Height + LogicalToDeviceUnits(8);

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        _shown?.Session.Focus();   // 利用者がクリック・Tab で入ったときだけハンドラーへ渡す（Q12）
    }

    /// <summary>ハンドラーが無い・フォーカスを取らないときは、ビュー自身の Tab でファイルリストへ戻す。</summary>
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) == Keys.Tab || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyData is Keys.Tab or (Keys.Tab | Keys.Shift))
        {
            FocusFileViewRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        base.OnKeyDown(e);
    }
}
