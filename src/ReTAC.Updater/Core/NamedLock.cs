using System;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace ReTAC.Updater.Core;

/// <summary>
/// R-109-1: 名前付きミューテックスによる排他。
/// <b>取ったスレッドで手放すこと</b>（ミューテックスはスレッドに属する）。置き換えの排他は、ファイル操作を行う作業用のスレッドが取る。
/// 排他はふだんの競合を防ぐためのもので、インストール先の安全の根拠ではない（INV-UPDATER-CONCURRENT-SAFE）。
/// </summary>
public sealed class NamedLock : IDisposable
{
    private readonly Mutex _mutex;
    private bool _released;

    private NamedLock(Mutex mutex) => _mutex = mutex;

    /// <summary>
    /// 待たずに取る。取れなければ null。前の持ち主が異常終了して放棄されたものは、取れたものとして扱う
    /// （強制終了された前のアップデータの I/O が残っていても、同時に書いて壊れないことで守る）。
    /// </summary>
    public static NamedLock? TryAcquire(string name)
    {
        var mutex = OpenOrCreate(name);
        if (mutex is null) return null;
        try
        {
            if (!mutex.WaitOne(0))
            {
                mutex.Dispose();
                return null;
            }
        }
        catch (AbandonedMutexException)
        {
            // 取れている（所有権はこちらに移った）
        }
        return new NamedLock(mutex);
    }

    /// <summary>ほかに持っている者がいなければ true。確かめに取った排他はすぐ手放し、ハンドルも破棄する。</summary>
    public static bool IsFree(string name)
    {
        var held = TryAcquire(name);
        if (held is null) return false;
        held.Dispose();
        return true;
    }

    public void Dispose()
    {
        if (_released) return;
        _released = true;
        try { _mutex.ReleaseMutex(); }
        catch (ApplicationException) { }   // 取ったスレッド以外から呼ばれた。破棄だけはする
        _mutex.Dispose();
    }

    /// <summary>
    /// 既にあるものは Synchronize | Modify だけを求めて開く（通常のコンストラクターで既存のものに接続すると FullControl を求め、
    /// 昇格したプロセスが作ったものを開けない）。無ければアクセス権を付けて作る。作ろうとしたら先を越されていたときは 1 回だけやり直す。
    /// 開けない（アクセス権が無い）ときは null で、「ほかが持っている」として扱う。
    /// </summary>
    private static Mutex? OpenOrCreate(string name)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try { return OpenExisting(name); }
            catch (WaitHandleCannotBeOpenedException) { }
            catch (UnauthorizedAccessException) { return null; }

            Mutex created;
            bool createdNew;
            try { created = Create(name, out createdNew); }
            catch (UnauthorizedAccessException) { return null; }
            if (createdNew) return created;
            created.Dispose();
        }
        return null;
    }

    private static Mutex OpenExisting(string name) =>
#if NETFRAMEWORK
        Mutex.OpenExisting(name, MutexRights.Synchronize | MutexRights.Modify);
#else
        MutexAcl.OpenExisting(name, MutexRights.Synchronize | MutexRights.Modify);
#endif

    /// <summary>対話ログオンした利用者と Administrators に Synchronize | Modify を与える。昇格の有無をまたいで開けるように。</summary>
    private static Mutex Create(string name, out bool createdNew)
    {
        var security = new MutexSecurity();
        foreach (var sid in new[] { WellKnownSidType.InteractiveSid, WellKnownSidType.BuiltinAdministratorsSid })
            security.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(sid, null),
                                                       MutexRights.Synchronize | MutexRights.Modify, AccessControlType.Allow));
#if NETFRAMEWORK
        return new Mutex(false, name, out createdNew, security);
#else
        return MutexAcl.Create(false, name, out createdNew, security);
#endif
    }
}
