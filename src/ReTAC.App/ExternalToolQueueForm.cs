using System.Drawing;
using System.Windows.Forms;
using ReTAC.Domain.Tools;

namespace ReTAC.App;

/// <summary>
/// 外部ツールキューの進行状況（F-05）。<b>利用者が開いたときだけ出す</b>（開始時・失敗時に自動で開かない）。
/// モードレスで、開いたままでも一覧を操作できる。プロセス全体で 1 枚。
/// Esc はこのウィンドウを閉じるだけで、キューは止めない（R-18）。
/// </summary>
public sealed class ExternalToolQueueForm : Form
{
    private static ExternalToolQueueForm? _instance;

    private readonly Label _summary = new() { AutoSize = true, Location = new Point(14, 14) };
    private readonly ListView _items = new()
    {
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
        Bounds = new Rectangle(14, 40, 600, 300),
    };
    private readonly Button _stop = new() { Text = "停止(&S)", Bounds = new Rectangle(14, 352, 130, 28) };
    private readonly Button _kill = new() { Text = "この 1 件を強制終了(&K)", Bounds = new Rectangle(152, 352, 190, 28) };

    public static void ShowFor(IWin32Window owner)
    {
        if (_instance is { IsDisposed: false })
        {
            _instance.Activate();
            return;
        }
        _instance = new ExternalToolQueueForm();
        _instance.Show(owner);
    }

    private ExternalToolQueueForm()
    {
        Text = "外部ツールキュー";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(628, 394);

        _items.Columns.Add("ツール", Scaled(150));
        _items.Columns.Add("対象", Scaled(250));
        _items.Columns.Add("状態", Scaled(170));

        var close = new Button { Text = "閉じる", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(524, 352, 90, 28) };
        close.Click += (_, _) => Close();
        Controls.AddRange([_summary, _items, _stop, _kill, close]);
        CancelButton = close;

        _stop.Click += (_, _) => ToolQueueHost.Queue.CancelWaiting();
        _kill.Click += (_, _) => KillRunning();

        ToolQueueHost.Queue.Changed += OnChanged;
        FormClosed += (_, _) => ToolQueueHost.Queue.Changed -= OnChanged;
        Refill();

        // C-1: ClientSize と Controls が揃ってから
        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    private int Scaled(int logical) => logical * DeviceDpi / 96;

    /// <summary>キューの通知は裏のスレッドから来る（S-07）。</summary>
    private void OnChanged()
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke((Action)Refill);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void Refill()
    {
        if (IsDisposed) return;
        var entries = ToolQueueHost.Queue.Snapshot();

        // ponytail: 変わるたびに作り直す。数十件なら十分速い。数百件で重ければ差分更新にする
        _items.BeginUpdate();
        _items.Items.Clear();
        foreach (var entry in entries)
            _items.Items.Add(new ListViewItem([entry.Request.Tool.Name, entry.Request.TargetLabel, Describe(entry)]));
        _items.EndUpdate();

        var status = ToolQueueHost.Queue.StatusText();
        _summary.Text = status.Length > 0 ? status : "外部ツールキューは空です。";
        _stop.Enabled = entries.Any(e => e.State == QueueItemState.Waiting);
        _kill.Enabled = entries.Any(e => e.State == QueueItemState.Running);
    }

    private static string Describe(QueueEntry entry) => entry.State switch
    {
        QueueItemState.Waiting => "待ち",
        QueueItemState.Running => "実行中",
        QueueItemState.Succeeded => "完了",
        QueueItemState.Failed when entry.Killed => "強制終了",
        QueueItemState.Failed when entry.Error is { } error => $"失敗: {error}",
        QueueItemState.Failed => $"失敗（終了コード {entry.ExitCode}）",
        QueueItemState.Cancelled => "取りやめ",
        _ => "",
    };

    private void KillRunning()
    {
        // レビュー f1: 確認の対象を先に確定する。ダイアログが開いている間に次の 1 件へ進んでも、
        // 確認していない別の 1 件を巻き込んで終了させない
        var request = ToolQueueHost.Queue.Snapshot().FirstOrDefault(e => e.State == QueueItemState.Running)?.Request;
        if (request is null) return;

        var answer = MessageBox.Show(this,
            "動いている 1 件を強制終了します。書きかけのファイルなどが残ることがあります。よろしいですか。",
            "ReTAC", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
        if (answer != DialogResult.OK) return;

        _ = KillRunningAsync(request);
    }

    /// <summary>m3: Process.Kill は子プロセスをたどるため待たされうる。UI スレッドを塞がない（R-23）。</summary>
    private async Task KillRunningAsync(LaunchRequest request)
    {
        var reason = await Task.Run(() => ToolQueueHost.KillRunning(request));
        if (IsDisposed) return;
        if (reason is { } message)
            MessageBox.Show(this,
                $"終了させられませんでした。ツールが管理者として動いている可能性があります。{Environment.NewLine}{message}",
                "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
