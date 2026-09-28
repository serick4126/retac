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
    /// 終了依頼への返事を決める。<paramref name="quitRequested"/> はプロセスで 1 つの「受け付け済み」の印。
    /// 受け付けた後に届いた依頼には <see cref="Accepted"/> を返すが、終了処理は始めない（同じ依頼で 2 回始めない）。
    /// 断るときは印を変えない。
    /// </summary>
    /// <param name="anyWindowDisabled">ReTAC のどれかのウィンドウが無効（モーダルのダイアログや MessageBox を開いている）</param>
    /// <param name="operationRunning">ファイル操作の最中</param>
    public static (int Reply, bool StartQuit) Decide(bool anyWindowDisabled, bool operationRunning, ref bool quitRequested)
    {
        if (anyWindowDisabled || operationRunning) return (Refused, false);
        if (quitRequested) return (Accepted, false);
        quitRequested = true;
        return (Accepted, true);
    }
}
