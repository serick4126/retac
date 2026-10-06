using System.Diagnostics;
using System.IO;
using ReTAC.App;
using ReTAC.Domain.Startup;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-142: 同じ設定ファイルを書く ReTAC は 1 つのプロセスに保つ。書く先の決め方・排他の名前・受け渡しの繰り返し・
/// 受けた側の受け付けを、偽物の時計・錠・相手で確かめる（本物のパイプ・ミューテックスは使わない）。
/// </summary>
public class SingleInstanceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "retac-single-" + Guid.NewGuid().ToString("N"));
    private const string Sid = "S-1-5-21-1";

    public SingleInstanceTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Dir(string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Settings(string dir) => Path.Combine(dir, AppSettings.FileName);

    // ---- 排他の名前 ----------------------------------------------------

    [Fact]
    public void Key_SamePathSameKey()
    {
        var dir = Dir("a");
        var expected = SettingsLocation.InstanceKey(Settings(dir));
        Assert.NotNull(expected);
        Assert.Equal(expected, SettingsLocation.InstanceKey(Settings(dir.ToUpperInvariant())));
        Assert.Equal(expected, SettingsLocation.InstanceKey(Path.Combine(dir + @"\", AppSettings.FileName)));
        Assert.Equal(expected, SettingsLocation.InstanceKey(Path.Combine(dir, "sub", "..", AppSettings.FileName)));
    }

    [Fact]
    public void Key_JunctionSameKey()
    {
        var real = Dir("real");
        var junction = Path.Combine(_root, "junction");
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{real}\"")
        { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using (var process = Process.Start(psi)!) { process.WaitForExit(10000); }
        if (!Directory.Exists(junction)) return;   // ジャンクションを作れない環境ではここで終わる（検証を飛ばす）

        try { Assert.Equal(SettingsLocation.InstanceKey(Settings(real)), SettingsLocation.InstanceKey(Settings(junction))); }
        finally { Directory.Delete(junction); }   // リンクだけを消す（再帰で消すと実体に入ろうとして失敗する）
    }

    [Fact]
    public void Key_UnwritableLocationsShareFallback()
    {
        var fallback = Settings(Dir("fallback"));
        var one = SettingsLocation.Choose(Settings(Dir("p1")), fallback, _ => false, SettingsLocation.SettingsIdentity);
        var two = SettingsLocation.Choose(Settings(Dir("p2")), fallback, _ => false, SettingsLocation.SettingsIdentity);
        Assert.Equal(fallback, one);
        Assert.Equal(SettingsLocation.InstanceKey(one!), SettingsLocation.InstanceKey(two!));
    }

    [Fact]
    public void Key_WritableLocationsDiffer()
    {
        var fallback = Settings(Dir("fallback"));
        var one = SettingsLocation.Choose(Settings(Dir("p1")), fallback, _ => true, SettingsLocation.SettingsIdentity);
        var two = SettingsLocation.Choose(Settings(Dir("p2")), fallback, _ => true, SettingsLocation.SettingsIdentity);
        Assert.NotEqual(SettingsLocation.InstanceKey(one!), SettingsLocation.InstanceKey(two!));
    }

    [Fact]
    public void Key_UnresolvedIsNotUsed()
    {
        // 最終パスを取れないときに、GetFullPath の大文字化などの代わりの名前を作らない
        Assert.Null(SettingsLocation.InstanceKey(Settings(Dir("a")), _ => null));
    }

    [Fact]
    public void WriteProbe_NameIsProcessUnique()
    {
        // 同時起動した別プロセスと一時ファイルがぶつからない名前（ぶつかると保存先と鍵がずれる）
        Assert.Contains(Environment.ProcessId.ToString(), SettingsLocation.ProbeFileName);
        var dir = Dir("probe");
        Assert.True(SettingsLocation.CanWriteDirectory(dir));
        Assert.Empty(Directory.GetFileSystemEntries(dir));
    }

    [Fact]
    public void Startup_UnresolvedPrimaryUsesFallback()
    {
        var primaryDir = Dir("primary");
        var fallbackDir = Dir("fallback");
        var chosen = SettingsLocation.Choose(Settings(primaryDir), Settings(fallbackDir), SettingsLocation.CanWriteDirectory,
            path => path.StartsWith(primaryDir) ? null : SettingsLocation.SettingsIdentity(path));
        Assert.Equal(Settings(fallbackDir), chosen);
        Assert.Empty(Directory.GetFileSystemEntries(primaryDir));   // 確かめられない書く先には、一時ファイルも書かない
    }

    [Fact]
    public void Startup_UnresolvedBothRefuses()
    {
        var primaryDir = Dir("primary");
        var fallbackDir = Dir("fallback");
        Assert.Null(SettingsLocation.Choose(Settings(primaryDir), Settings(fallbackDir), SettingsLocation.CanWriteDirectory, _ => null));
        Assert.Empty(Directory.GetFileSystemEntries(primaryDir));
        Assert.Empty(Directory.GetFileSystemEntries(fallbackDir));
    }

    [Fact]
    public void PipeName_SessionScoped()
    {
        var name = SettingsLocation.PipeName("K", Sid, 1);
        Assert.Equal(name, SettingsLocation.PipeName("K", Sid, 1));
        Assert.NotEqual(name, SettingsLocation.PipeName("K", Sid, 2));
        Assert.NotEqual(name, SettingsLocation.PipeName("K", "S-1-5-21-2", 1));
        Assert.NotEqual(name, SettingsLocation.PipeName("L", Sid, 1));
    }

    [Fact]
    public void MutexName_CrossSession()
    {
        // 名前を作る関数が SID・セッション ID を受け取らない（セッションをまたいで同じ名前になる）
        var method = typeof(SettingsLocation).GetMethod(nameof(SettingsLocation.MutexName), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;
        Assert.Single(method.GetParameters());
        Assert.StartsWith(@"Global\", SettingsLocation.MutexName("K"));
        Assert.Equal(SettingsLocation.MutexName("K"), SettingsLocation.MutexName("K"));
        Assert.NotEqual(SettingsLocation.MutexName("K"), SettingsLocation.MutexName("L"));
    }

    // ---- 書く前の排他 --------------------------------------------------

    private sealed class FakeLocks(Func<string, bool> allow) : IInstanceLocks
    {
        public List<string> Asked { get; } = [];
        public bool TryHold(string settingsPath) { Asked.Add(settingsPath); return allow(settingsPath); }
    }

    private (string Primary, string Fallback) SavePaths()
    {
        var primary = Settings(Dir("primary"));
        var fallback = Settings(Dir("fallback"));
        File.WriteAllText(primary, "old-primary");
        File.WriteAllText(fallback, "old-fallback");
        return (primary, fallback);
    }

    [Fact]
    public void Save_UnresolvedLocationDoesNotWrite()
    {
        var (primary, fallback) = SavePaths();
        var settings = new AppSettings();
        string? actual = null;
        // 排他を持つ道は実物（InstanceLocks）。実体を確かめられない書く先は、排他を取れない書く先と同じに飛ばす
        var locks = new InstanceLocks(path => path == fallback ? path : null, _ => new FakeLock(true), _ => { });
        Assert.Equal(SaveResult.MovedToFallback, settings.Save(primary, fallback, locks, ref actual));
        Assert.Equal("old-primary", File.ReadAllText(primary));

        var none = new InstanceLocks(_ => null, _ => new FakeLock(true), _ => { });
        File.WriteAllText(fallback, "old-fallback");
        Assert.Equal(SaveResult.Blocked, settings.Save(primary, fallback, none, ref actual));
        Assert.Equal("old-primary", File.ReadAllText(primary));
        Assert.Equal("old-fallback", File.ReadAllText(fallback));
    }

    [Fact]
    public void Save_FallbackBlockedDoesNotWrite()
    {
        var (primary, fallback) = SavePaths();
        string? actual = null;
        // 横に書けない（ディレクトリにして書けなくする）うえで、逃がし先の排他も取れない
        File.Delete(primary);
        Directory.CreateDirectory(primary);
        var locks = new FakeLocks(path => path == primary);
        Assert.Equal(SaveResult.Blocked, new AppSettings().Save(primary, fallback, locks, ref actual));
        Assert.Equal("old-fallback", File.ReadAllText(fallback));
    }

    [Fact]
    public void Save_PrimaryRecoveredButLockedDoesNotWrite()
    {
        var (primary, fallback) = SavePaths();
        string? actual = fallback;   // 逃がし先で起動した後
        var locked = new FakeLocks(path => path == fallback);
        Assert.Equal(SaveResult.Saved, new AppSettings().Save(primary, fallback, locked, ref actual));
        Assert.Equal("old-primary", File.ReadAllText(primary));   // 横の排他が取れないので、横には書かない
        Assert.NotEqual("old-fallback", File.ReadAllText(fallback));
        Assert.Equal(fallback, actual);

        var free = new FakeLocks(_ => true);
        Assert.Equal(SaveResult.Saved, new AppSettings().Save(primary, fallback, free, ref actual));
        Assert.NotEqual("old-primary", File.ReadAllText(primary));
        Assert.Equal(primary, actual);
    }

    private sealed class FakeLock(bool result) : IInstanceLock
    {
        public bool TryAcquire() => result;
    }

    [Fact]
    public void Save_NewLockStartsListener()
    {
        var started = new List<string>();
        var locks = new InstanceLocks(path => path, _ => new FakeLock(true), started.Add);
        Assert.True(locks.TryHold("one"));
        Assert.True(locks.TryHold("one"));   // 持っているものは、取り直さず待ち受けも増やさない
        Assert.True(locks.TryHold("two"));
        Assert.Equal(2, started.Count);
        Assert.NotEqual(started[0], started[1]);

        var denied = new InstanceLocks(path => path, _ => new FakeLock(false), started.Add);
        Assert.False(denied.TryHold("three"));
        Assert.Equal(2, started.Count);   // 取れなかったものは待ち受けない
    }

    // ---- 受け渡しの繰り返し ----------------------------------------------

    private sealed class FakeChannel(params ForwardReply[] replies) : IInstanceChannel
    {
        private int _next;
        public List<StartupRequest> Sent { get; } = [];

        public ForwardReply TryForward(StartupRequest request)
        {
            Sent.Add(request);
            return replies[Math.Min(_next++, replies.Length - 1)];
        }
    }

    private sealed class ScriptedLock(Func<int, bool> result) : IInstanceLock
    {
        public int Calls { get; private set; }
        public bool TryAcquire() => result(Calls++);
    }

    private static readonly StartupRequest Request = new(Guid.NewGuid(), StartupPathKind.Folder, @"C:\a", null, @"C:\a");

    private static (StartupDecision Decision, GiveUpReason Reason) Run(IInstanceLock l, IInstanceChannel c)
    {
        var clock = TimeSpan.Zero;
        var decision = SingleInstance.Decide(l, c, Request, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200),
            () => clock, wait => clock += wait, out var reason);
        return (decision, reason);
    }

    [Fact]
    public void Startup_FreeLockStarts()
    {
        var channel = new FakeChannel(ForwardReply.Forwarded);
        Assert.Equal(StartupDecision.Start, Run(new FakeLock(true), channel).Decision);
        Assert.Empty(channel.Sent);
    }

    [Fact]
    public void Startup_RunningForwards()
    {
        Assert.Equal(StartupDecision.Forwarded, Run(new FakeLock(false), new FakeChannel(ForwardReply.Forwarded)).Decision);
    }

    [Fact]
    public void Startup_SlowReplyDoesNotStart()
    {
        var slow = new FakeChannel(ForwardReply.NoAnswer, ForwardReply.NoAnswer, ForwardReply.NoAnswer, ForwardReply.Forwarded);
        Assert.Equal(StartupDecision.Forwarded, Run(new FakeLock(false), slow).Decision);
        Assert.Equal(4, slow.Sent.Count);

        var (decision, reason) = Run(new FakeLock(false), new FakeChannel(ForwardReply.NoAnswer));
        Assert.Equal(StartupDecision.GiveUp, decision);
        Assert.Equal(GiveUpReason.NoAnswer, reason);
    }

    [Fact]
    public void Startup_ResendsSameId()
    {
        var channel = new FakeChannel(ForwardReply.NoAnswer, ForwardReply.Busy, ForwardReply.Forwarded);
        Run(new FakeLock(false), channel);
        Assert.Equal(3, channel.Sent.Count);
        Assert.All(channel.Sent, r => Assert.Equal(Request.Id, r.Id));
    }

    [Fact]
    public void Startup_CancelledQuitForwards()
    {
        var channel = new FakeChannel(ForwardReply.Busy, ForwardReply.Busy, ForwardReply.Busy, ForwardReply.Forwarded);
        Assert.Equal(StartupDecision.Forwarded, Run(new FakeLock(false), channel).Decision);
    }

    [Fact]
    public void Startup_PreviousExitedStarts()
    {
        var channel = new FakeChannel(ForwardReply.Busy);
        Assert.Equal(StartupDecision.Start, Run(new ScriptedLock(call => call >= 2), channel).Decision);
        Assert.Equal(2, channel.Sent.Count);   // 2 回目の試みで錠が取れるまでに、渡す試みは 2 回
    }

    [Fact]
    public void Startup_MixedRepliesLatchNoAnswer()
    {
        // 一度でも受け手に届いた（NoAnswer）なら、その後 NoListener が続いても「応答しない」の側
        var channel = new FakeChannel(ForwardReply.NoListener, ForwardReply.NoAnswer, ForwardReply.NoListener);
        var (decision, reason) = Run(new FakeLock(false), channel);
        Assert.Equal(StartupDecision.GiveUp, decision);
        Assert.Equal(GiveUpReason.NoAnswer, reason);
    }

    [Fact]
    public void Startup_OtherSessionOwnerGivesUp()
    {
        var (decision, reason) = Run(new FakeLock(false), new FakeChannel(ForwardReply.NoListener));
        Assert.Equal(StartupDecision.GiveUp, decision);   // 排他なしでは Start にならない
        Assert.Equal(GiveUpReason.NoListener, reason);
    }

    // ---- 受けた側 --------------------------------------------------------

    private sealed class HostFixture
    {
        public QuitForUpdateGate Gate { get; } = new();
        public bool Exiting { get; set; }
        public int Runs { get; private set; }
        public Func<StartupRequest, bool> Fails { get; set; } = _ => false;
        public List<StartupRequest> Performed { get; } = [];
        public List<(string Key, Func<StartupRequest, string> Accept)> Listening { get; } = [];
        public SingleInstanceHost Host { get; }

        public HostFixture()
        {
            Host = new SingleInstanceHost(Gate, () => Exiting, request =>
            {
                if (Fails(request)) throw new InvalidOperationException("boom");
                Runs++;
                Performed.Add(request);
            }, (key, accept) => Listening.Add((key, accept)), _ => { });
            Host.Attach();
        }
    }

    private static StartupRequest Req(string? folder = @"C:\x") => new(Guid.NewGuid(), StartupPathKind.Folder, folder, null, folder);

    [Fact]
    public void Accept_BusyWhileQuitting()
    {
        var f = new HostFixture();
        f.Gate.OnRequest(false, () => { });
        Assert.Equal("busy", f.Host.Accept(Req()));
        Assert.Equal(0, f.Runs);
    }

    [Fact]
    public void Accept_OkMeansAlreadyExecuted()
    {
        var f = new HostFixture();
        Assert.Equal("ok", f.Host.Accept(Req()));
        Assert.Equal(1, f.Runs);   // 返した時点で済んでいる（後に回していない）
        f.Gate.OnRequest(false, () => { });
        f.Exiting = true;
        Assert.Equal(1, f.Runs);
    }

    [Fact]
    public void Accept_BusyWhenExiting()
    {
        var f = new HostFixture { Exiting = true };
        Assert.Equal("busy", f.Host.Accept(Req()));
        Assert.Equal(0, f.Runs);
    }

    [Fact]
    public void Accept_FailureIsNotRecorded()
    {
        var f = new HostFixture();
        var request = Req();
        f.Fails = _ => true;
        Assert.Equal("busy", f.Host.Accept(request));
        f.Fails = _ => false;
        Assert.Equal("ok", f.Host.Accept(request));   // 同じ Id の再送で、もう一度試す
        Assert.Equal(1, f.Runs);
    }

    [Fact]
    public void Accept_DuplicateIdRunsOnce()
    {
        var f = new HostFixture();
        var a = Req();
        Assert.Equal("ok", f.Host.Accept(a));
        Assert.Equal("ok", f.Host.Accept(a));
        Assert.Equal(1, f.Runs);

        for (var i = 0; i < 100; i++) f.Host.Accept(Req());
        Assert.Equal(101, f.Runs);
        Assert.Equal("ok", f.Host.Accept(a));   // 100 件のあとでも、A は 1 回しか実行しない
        Assert.Equal(101, f.Runs);

        f.Host.Accept(a with { Id = Guid.NewGuid() });   // 別の Id なら同じパスでも実行する
        Assert.Equal(102, f.Runs);
    }

    [Fact]
    public void Accept_SecondKeyReceives()
    {
        var f = new HostFixture();
        var locks = new InstanceLocks(path => path, _ => new FakeLock(true), f.Host.Listen);
        locks.TryHold("primary");
        locks.TryHold("fallback");
        Assert.Equal(2, f.Listening.Count);
        Assert.NotEqual(f.Listening[0].Key, f.Listening[1].Key);
        Assert.Equal("ok", f.Listening[1].Accept(Req()));   // 2 つ目のキーの待ち受けに来た要求が Accept に届く
        Assert.Equal(1, f.Runs);
    }

    [Fact]
    public void Accept_PendingListenersStartOnAttach()
    {
        var gate = new QuitForUpdateGate();
        var started = new List<string>();
        var host = new SingleInstanceHost(gate, () => false, _ => { }, (key, _) => started.Add(key), _ => { });
        host.Listen("k1");   // 窓（受け取る側）ができる前に取った排他
        Assert.Empty(started);
        host.Attach();
        host.Listen("k2");
        Assert.Equal(["k1", "k2"], started);
    }

    [Fact]
    public void Receive_Table()
    {
        var folder = SingleInstanceHost.Plan(new(Guid.NewGuid(), StartupPathKind.Folder, @"C:\a", null, @"C:\a"));
        Assert.Equal(new ReceivePlan(ReceiveKind.OpenNew, @"C:\a", null, null), folder);

        var file = SingleInstanceHost.Plan(new(Guid.NewGuid(), StartupPathKind.File, @"C:\a", "b.txt", @"C:\a\b.txt"));
        Assert.Equal(new ReceivePlan(ReceiveKind.OpenNew, @"C:\a", "b.txt", null), file);

        var invalid = SingleInstanceHost.Plan(new(Guid.NewGuid(), StartupPathKind.Invalid, null, null, @"Z:\nowhere"));
        Assert.Equal(new ReceivePlan(ReceiveKind.BringLast, null, null, @"開けません: Z:\nowhere"), invalid);

        var none = SingleInstanceHost.Plan(new(Guid.NewGuid(), StartupPathKind.None, null, null, ""));
        Assert.Equal(new ReceivePlan(ReceiveKind.BringLast, null, null, null), none);
    }
}
