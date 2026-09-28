namespace ReTAC.App;

/// <summary>
/// R-109 / R-109-3: アップデータ（ReTAC.Updater）との間の約束。
/// <b>版をまたいで変えない。</b>旧版の ReTAC を新版のアップデータが、新版の ReTAC を旧版のアップデータが終了させるため。
/// アップデータ側にも同じ値の定数があり、テストで両方がこの値と一致することを確かめる。
/// </summary>
public static class UpdateProtocol
{
    /// <summary>終了依頼のメッセージ名（RegisterWindowMessage）。</summary>
    public const string MessageName = "ReTAC.QuitForUpdate";

    /// <summary>送り先の目印（SetProp）。MainForm にだけ付け、ダイアログには付けない。</summary>
    public const string WindowPropName = "ReTAC.MainWindow";

    /// <summary>受け付けた。この後、プロセスのウィンドウをすべて閉じる（常駐の設定に関係なく）。</summary>
    public const int Accepted = 1;

    /// <summary>断った。ダイアログを開いている、またはファイル操作の最中。</summary>
    public const int Refused = 2;

    /// <summary>
    /// 終了依頼の様子の問い合わせ（RegisterWindowMessage）。アップデータを閉じた後も、受け付けた ReTAC が本当に終わるのか、
    /// K-5 の確認で取りやめたのかを見届けるために使う。ウィンドウの様子や時間からは取りやめを見分けられないため。
    /// </summary>
    public const string StateMessageName = "ReTAC.QuitForUpdateState";

    /// <summary>問い合わせの返事: 終了依頼を受け付けていて、終わる途中（K-5 の確認を出している間を含む）。</summary>
    public const int Quitting = 1;

    /// <summary>問い合わせの返事: 終了依頼を受け付けていない（受けていない・断った・K-5 の確認で取りやめた）。</summary>
    public const int NotQuitting = 2;

    /// <summary>
    /// 終了依頼への返事を決める。<paramref name="quitRequested"/> はプロセスで 1 つの「受け付け済み」の印。
    /// 受け付けた後に届いた依頼には、ダイアログを開いていても <see cref="Accepted"/> を返し、終了処理は始めない
    /// （同じ依頼で 2 回始めない）。受け付けた後は K-5 の確認でウィンドウが無効になるので、先に印を見ないと
    /// 受け付けたはずの依頼を断ることになる。断るときは印を変えない。
    /// </summary>
    /// <param name="anyWindowDisabled">ReTAC のどれかのウィンドウが無効（モーダルのダイアログや MessageBox を開いている）</param>
    /// <param name="operationRunning">ファイル操作の最中</param>
    public static (int Reply, bool StartQuit) Decide(bool anyWindowDisabled, bool operationRunning, ref bool quitRequested)
    {
        if (quitRequested) return (Accepted, false);
        if (anyWindowDisabled || operationRunning) return (Refused, false);
        quitRequested = true;
        return (Accepted, true);
    }
}

/// <summary>
/// R-109-3: 終了依頼の受け口。受け付けの印とファイル操作の数をプロセスで 1 つずつ持ち、
/// 返事を返した後で終了処理を予約する。MainForm はメッセージを受けたらここへ渡すだけにする
/// （MainForm はテストで組み立てられないので、配線をここに寄せてテストする）。
/// </summary>
public sealed class QuitForUpdateGate
{
    private bool _quitRequested;
    private int _operationsRunning;

    /// <summary>ファイル操作の間だけ持つ。持っている間は終了依頼を断る。</summary>
    public IDisposable BeginOperation()
    {
        _operationsRunning++;
        return new Operation(this);
    }

    /// <summary>
    /// 依頼を受けたときに呼ぶ。返事を返し、受け付けたときだけ <paramref name="scheduleQuit"/> で終了処理を予約する。
    /// 予約はメッセージの処理の外で走らせること（BeginInvoke）。中で K-5 の確認を出すと、返事が返らない。
    /// </summary>
    public int OnRequest(bool anyWindowDisabled, Action scheduleQuit)
    {
        var (reply, startQuit) = UpdateProtocol.Decide(anyWindowDisabled, _operationsRunning > 0, ref _quitRequested);
        if (startQuit) scheduleQuit();
        return reply;
    }

    /// <summary>終了処理の結果を伝える。K-5 の確認で取りやめたら印を戻し、次の依頼をまた受け付けられるようにする。</summary>
    public void OnQuitFinished(bool quit)
    {
        if (!quit) _quitRequested = false;
    }

    /// <summary>
    /// 終了依頼の様子の問い合わせへの返事。受け付けの印がそのまま答えになる（K-5 の確認の間は立ったまま、取りやめたら戻る。
    /// 終了を選んだ後は、プロセスが消えるまで立ったまま）。
    /// </summary>
    public int OnStateQuery() => _quitRequested ? UpdateProtocol.Quitting : UpdateProtocol.NotQuitting;

    private sealed class Operation(QuitForUpdateGate gate) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            gate._operationsRunning--;
        }
    }
}
