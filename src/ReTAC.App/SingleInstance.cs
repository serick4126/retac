using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using ReTAC.Domain.Startup;
using ReTAC.Updater.Core;

namespace ReTAC.App;

/// <summary>R-142: 新しく起動した側がどう進むか。Refused は書く先の実体を確かめられないとき（Decide の前に決まる）。</summary>
internal enum StartupDecision { Start, Forwarded, GiveUp, Refused }

/// <summary>R-142: 動いている側へ渡した結果。NoListener は待ち受けるパイプが見つからない（別のセッション・別の利用者が持っているとき）。</summary>
internal enum ForwardReply { Forwarded, Busy, NoAnswer, NoListener }

/// <summary>R-142: 諦めた理由。文言を分ける（パイプがあるのに返事が無い／パイプが見つからない）。</summary>
internal enum GiveUpReason { NoAnswer, NoListener }

internal interface IInstanceLock { bool TryAcquire(); }

internal interface IInstanceChannel { ForwardReply TryForward(StartupRequest request); }

/// <summary>
/// R-142: 動いている側へ渡す起動の要求。<see cref="Id"/> は送る側が起動のたびに 1 つ作り、再送しても同じ値を使う
/// （受けた側が処理した後で返事が届かなくても、再送で同じ窓が増えない）。存在の確認は送る側が済ませてあり、受けた側は触らない。
/// </summary>
internal sealed record StartupRequest(Guid Id, StartupPathKind Kind, string? Folder, string? FileName, string? Raw);

/// <summary>R-142: 設定ファイルの書く先の決め方と、排他・パイプの名前。</summary>
internal static class SettingsLocation
{
    /// <summary>実行ファイルの横に書けるなら primaryPath、書けなければ fallbackPath（R-55-3）。</summary>
    internal static string Decide(string primaryPath, string fallbackPath, Func<string, bool> canWriteDirectory) =>
        canWriteDirectory(Path.GetDirectoryName(Path.GetFullPath(primaryPath))!) ? primaryPath : fallbackPath;

    /// <summary>
    /// R-142: 起動時に書く先を決める。実体（最終パス）を確かめられない書く先は「使えない」とみなし、
    /// 確かめる前には何も書かない。両方とも使えなければ null（起動しない）。
    /// </summary>
    internal static string? Choose(string primaryPath, string fallbackPath, Func<string, bool> canWriteDirectory,
        Func<string, string?> identity)
    {
        var picked = Decide(primaryPath, fallbackPath, directory =>
            identity(Path.Combine(directory, AppSettings.FileName)) is not null && canWriteDirectory(directory));
        return identity(picked) is null ? null : picked;
    }

    /// <summary>そのフォルダに一時ファイルを作って消せるか。一時的な失敗のやり直しは書き込みと同じ（R-55-3）。</summary>
    internal static bool CanWriteDirectory(string directory)
    {
        var probe = Path.Combine(directory, "retac.write-test.tmp");
        try
        {
            AppSettings.WriteAtomic(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 設定ファイルの親フォルダの最終パス（ジャンクション・8.3 形式の名前・ドライブ文字の違いを解いたもの）＋ファイル名。
    /// 親フォルダが無ければ（初めての逃がし先）先に作る。開けなければ null。
    /// </summary>
    internal static string? SettingsIdentity(string settingsPath)
    {
        try
        {
            var full = Path.GetFullPath(settingsPath);
            var directory = Path.GetDirectoryName(full);
            if (directory is null) return null;
            Directory.CreateDirectory(directory);
            return InstallFolder.Normalize(directory) is { } normalized
                ? normalized.TrimEnd('\\') + "\\" + Path.GetFileName(full)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// 排他とパイプの名前の元。確かめられないときは null で、GetFullPath の大文字化などの代わりの名前は作らない
    /// （最終パスで作った名前と一致せず、同じ保存先で排他が 2 つに分かれる）。
    /// </summary>
    internal static string? InstanceKey(string settingsPath, Func<string, string?>? identity = null) =>
        (identity ?? SettingsIdentity)(settingsPath) is { } id ? InstallFolder.Hash(id) : null;

    /// <summary>排他は Global\ に置いてセッションをまたぐ（同じ設定ファイルを書くプロセスを 1 つにするため）。SID・セッション ID は入れない。</summary>
    internal static string MutexName(string key) => @"Global\ReTAC.Settings." + key;

    /// <summary>要求を渡すパイプは、今の利用者の今のセッションに限る。</summary>
    internal static string PipeName(string key, string sid, int sessionId) => $"ReTAC.Settings.{key}.{sid}.{sessionId}";

    internal static int CurrentSessionId() => System.Diagnostics.Process.GetCurrentProcess().SessionId;

    internal static string CurrentSid() => WindowsIdentity.GetCurrent().User?.Value ?? "";
}

/// <summary>R-142: 書く先ごとの排他を持つ集まり。持った排他はプロセスが終わるまで放さない。</summary>
internal interface IInstanceLocks
{
    /// <summary>その書く先の排他を持っていればそのまま true。持っていなければ取る。実体を確かめられない・取れないなら false。</summary>
    bool TryHold(string settingsPath);
}

internal sealed class InstanceLocks(Func<string, string?> identity, Func<string, IInstanceLock> create, Action<string> startListener)
    : IInstanceLocks
{
    private readonly Dictionary<string, IInstanceLock> _held = [];

    public bool TryHold(string settingsPath)
    {
        if (identity(settingsPath) is not { } id) return false;
        var key = InstallFolder.Hash(id);
        if (_held.ContainsKey(key)) return true;
        var lockObj = create(key);
        if (!lockObj.TryAcquire()) return false;
        _held[key] = lockObj;
        startListener(key);   // 排他を取った書く先ごとに、要求を待ち受ける
        return true;
    }
}

/// <summary>起動時に決めた書く先の排他を、<see cref="InstanceLocks"/> 経由で取る（保存時に取り直さず、同じものを使う）。</summary>
internal sealed class HeldLock(IInstanceLocks locks, string settingsPath) : IInstanceLock
{
    public bool TryAcquire() => locks.TryHold(settingsPath);
}

/// <summary>R-142: 本物の排他。取れたらプロセスが終わるまで持つ（明示的に放さない）。UI スレッドで取る。</summary>
internal sealed class MutexLock(string key) : IInstanceLock
{
    private Mutex? _mutex;

    public bool TryAcquire()
    {
        if (_mutex is not null) return true;
        Mutex mutex;
        try { mutex = new Mutex(false, SettingsLocation.MutexName(key)); }
        catch (UnauthorizedAccessException) { return false; }   // 別の利用者が作った名前を開けない: 持ち主がいる
        try
        {
            if (!mutex.WaitOne(0)) { mutex.Dispose(); return false; }
        }
        catch (AbandonedMutexException) { }   // 前のプロセスが落ちた。取れたとみなす
        _mutex = mutex;
        return true;
    }
}

internal static class SingleInstance
{
    /// <summary>
    /// R-142: 排他を取れたときだけ Start。取れなければ渡し、渡せなければ時間切れまで繰り返す。
    /// 返事が無いことだけでは Start にしない（返事が遅れただけの側・K-5 で終了を取り消した側が残り、同じ設定を書くプロセスが 2 つになる）。
    /// </summary>
    internal static StartupDecision Decide(IInstanceLock lockObj, IInstanceChannel channel, StartupRequest request,
        TimeSpan limit, TimeSpan interval, Func<TimeSpan> elapsed, Action<TimeSpan> wait, out GiveUpReason reason)
    {
        reason = GiveUpReason.NoListener;
        while (true)
        {
            if (lockObj.TryAcquire()) return StartupDecision.Start;
            var reply = channel.TryForward(request);
            if (reply == ForwardReply.Forwarded) return StartupDecision.Forwarded;
            if (reply != ForwardReply.NoListener) reason = GiveUpReason.NoAnswer;
            if (elapsed() >= limit) return StartupDecision.GiveUp;
            wait(interval);
        }
    }

    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(10), Interval = TimeSpan.FromMilliseconds(200);
}

internal enum ReceiveKind { OpenNew, BringLast }

/// <summary>R-142: 受けた側の振り分け。ファイルシステムには触らない（存在は送る側が確かめ済み）。</summary>
internal sealed record ReceivePlan(ReceiveKind Kind, string? Folder, string? SelectName, string? Message);

/// <summary>R-142: 受けた起動要求の受け付け。UI スレッドで呼ぶ（<see cref="PipeListener"/> が Control.Invoke で渡す）。</summary>
internal sealed class SingleInstanceHost(QuitForUpdateGate gate, Func<bool> isExiting, Action<StartupRequest> perform,
    Action<string, Func<StartupRequest, string>> listen, Action<Exception> log)
{
    // 処理済みの Id はプロセスが終わるまで全部覚える（1 件 16 バイト。件数に上限を付けると、返事を失った要求の再送が上限を越えた後に再実行される）
    private readonly HashSet<Guid> _done = [];
    private readonly List<string> _pending = [];
    private bool _attached;

    internal static ReceivePlan Plan(StartupRequest request) => request switch
    {
        { Kind: StartupPathKind.Folder, Folder: { Length: > 0 } folder } => new(ReceiveKind.OpenNew, folder, null, null),
        { Kind: StartupPathKind.File, Folder: { Length: > 0 } folder } => new(ReceiveKind.OpenNew, folder, request.FileName, null),
        { Kind: StartupPathKind.Invalid } => new(ReceiveKind.BringLast, null, null, $"開けません: {request.Raw}"),
        _ => new(ReceiveKind.BringLast, null, null, null),
    };

    /// <summary>要求を受けたときの返事を、UI スレッドで決める。"ok" を返す時点で、要求の処理（窓を作って開き始める）は済んでいる。</summary>
    internal string Accept(StartupRequest request)
    {
        if (gate.OnStateQuery() == UpdateProtocol.Quitting) return "busy";
        if (_done.Contains(request.Id)) return "ok";
        if (isExiting()) return "busy";   // 更新用のゲートが守らない終了の経路（最後の窓を閉じた後・完全終了）
        try { perform(request); }
        catch (Exception ex)
        {
            log(ex);
            return "busy";   // Id を記録しない。送る側の再送でもう一度試す
        }
        _done.Add(request.Id);
        return "ok";
    }

    /// <summary>排他を取った書く先のキーの待ち受けを始める。受け取る窓ができる前なら、<see cref="Attach"/> まで控える。</summary>
    internal void Listen(string key)
    {
        if (_attached) listen(key, Accept);
        else _pending.Add(key);
    }

    internal void Attach()
    {
        _attached = true;
        foreach (var key in _pending) listen(key, Accept);
        _pending.Clear();
    }
}

/// <summary>R-142: 本物の送り手。パイプで 1 行の JSON を送り、"ok" / "busy" の返事を受ける。</summary>
internal sealed class PipeChannel(string key) : IInstanceChannel
{
    private static readonly TimeSpan ReplyLimit = TimeSpan.FromSeconds(2);
    private const int ConnectMilliseconds = 500;

    public ForwardReply TryForward(StartupRequest request)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", SettingsLocation.PipeName(key, SettingsLocation.CurrentSid(), SettingsLocation.CurrentSessionId()),
                PipeDirection.InOut, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
            try { client.Connect(ConnectMilliseconds); }
            catch (TimeoutException) { return ForwardReply.NoListener; }

            // 受けた側が窓を前に出せるように、前面にする許可を渡す
            if (GetNamedPipeServerProcessId(client.SafePipeHandle.DangerousGetHandle(), out var pid)) AllowSetForegroundWindow(pid);

            var utf8 = new UTF8Encoding(false);
            using var writer = new StreamWriter(client, utf8, 1024, leaveOpen: true) { AutoFlush = true };
            writer.WriteLine(JsonSerializer.Serialize(request));
            using var reader = new StreamReader(client, utf8, false, 1024, leaveOpen: true);
            using var cts = new CancellationTokenSource(ReplyLimit);
            // 呼び出し元は同期コンテキストの無いスレッド（Main の最初）なので、ここで待ってもデッドロックしない
            var reply = reader.ReadLineAsync(cts.Token).AsTask().GetAwaiter().GetResult();
            return reply switch { "ok" => ForwardReply.Forwarded, "busy" => ForwardReply.Busy, _ => ForwardReply.NoAnswer };
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            return ForwardReply.NoAnswer;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}

/// <summary>R-142: 本物の受け手。キーごとに 1 本の背景のスレッドで、1 件ずつ順に処理する。</summary>
internal static class PipeListener
{
    private static readonly TimeSpan ReadLimit = TimeSpan.FromSeconds(2);

    /// <param name="accept">UI スレッドで呼ぶ（<c>Control.Invoke</c>）。返事をそのまま送る側へ返す。</param>
    internal static void Start(string key, Func<StartupRequest, string> accept, Control ui)
    {
        var name = SettingsLocation.PipeName(key, SettingsLocation.CurrentSid(), SettingsLocation.CurrentSessionId());
        new Thread(() => Loop(name, accept, ui)) { IsBackground = true, Name = "ReTAC.StartupPipe" }.Start();
    }

    private static void Loop(string name, Func<StartupRequest, string> accept, Control ui)
    {
        var utf8 = new UTF8Encoding(false);
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
                server.WaitForConnection();
                using var reader = new StreamReader(server, utf8, false, 1024, leaveOpen: true);
                using var cts = new CancellationTokenSource(ReadLimit);
                var line = reader.ReadLineAsync(cts.Token).AsTask().GetAwaiter().GetResult();

                var reply = "busy";
                try
                {
                    if (line is not null && JsonSerializer.Deserialize<StartupRequest>(line) is { } request)
                        reply = (string)ui.Invoke(accept, request);   // 同期。要求の処理が UI スレッドで済んでから返事を決める
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or ObjectDisposedException) { }

                using var writer = new StreamWriter(server, utf8, 1024, leaveOpen: true) { AutoFlush = true };
                writer.WriteLine(reply);
                server.WaitForPipeDrain();
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
            {
                Thread.Sleep(200);   // 切れた接続は捨てて、次を待つ
            }
        }
    }
}
