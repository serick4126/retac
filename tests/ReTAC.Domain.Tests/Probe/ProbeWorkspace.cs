using System.IO.Compression;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-146-1 の実機調査（vprobe）の作業場所。安全の原則（S4）に従い、<b>この処理が新しく作ったフォルダだけ</b>を使う。
/// ファイルは新規作成だけで書き（既にあれば失敗する）、消す・移す・上書きする操作を持たない
/// （DestructiveApiTests がこのフォルダの破壊的な呼び出しを 0 件に保つ）。片付けは利用者が手で行う。
/// </summary>
internal sealed class ProbeWorkspace
{
    /// <summary>plain.zip の項目の日時（ZIP は 2 秒単位・ローカル時刻。S2 の確認に使う）</summary>
    public static readonly DateTime PlainStamp = new(2020, 1, 2, 3, 4, 6);

    private int _runs;

    private ProbeWorkspace(string root) => Root = root;

    public string Root { get; }

    /// <summary>evil.zip の絶対パスの項目が指す所。この作業場所の中にあり、見張りを置く</summary>
    public string AbsTarget => Path.Combine(Root, "abs-target");

    public string EvilZip => Path.Combine(Root, "evil.zip");

    public string PlainZip => Path.Combine(Root, "plain.zip");

    /// <summary>tempRoot の下に vprobe-&lt;GUID&gt; を新しく作る。作れなければ null（既にあるものには触らない）</summary>
    public static ProbeWorkspace? Create(string tempRoot) => Create(tempRoot, "vprobe-" + Guid.NewGuid().ToString("N"));

    /// <summary>名前を決めて作る（テストの入口）。既にあれば null で、そのフォルダには何もしない</summary>
    internal static ProbeWorkspace? Create(string tempRoot, string name)
    {
        var root = Path.Combine(Path.GetFullPath(tempRoot), name);
        if (Path.Exists(root)) return null;
        Directory.CreateDirectory(root);
        if (Directory.EnumerateFileSystemEntries(root).Any()) return null;   // 同時にほかが作ったもの。使わない

        var workspace = new ProbeWorkspace(root);
        Directory.CreateDirectory(workspace.AbsTarget);
        foreach (var sentinel in workspace.AbsSentinels()) CreateNew(sentinel, "original");
        WriteZip(workspace.EvilZip, EvilEntries(workspace.AbsTarget), _ => DateTime.Now);
        WriteZip(workspace.PlainZip, ["x.txt", "sub/y.txt"], _ => PlainStamp);
        return workspace;
    }

    /// <summary>
    /// 不正な名前の項目（取り出し先が level1\out のとき）。絶対パスの 2 つは absTarget の中を指す本当の絶対パス
    /// （ドライブの直下などは指さない）。先頭の sub/ok.txt は陽性対照。
    /// </summary>
    public static string[] EvilEntries(string absTarget) =>
    [
        "sub/ok.txt",                                                      // 正しい項目（陽性対照）
        "sub/../../escape1.txt",                                           // dest\sub\..\..\ → level1
        "sub/..\\..\\escape2.txt",                                         // 区切りが \ の形 → level1
        "../escape3.txt",                                                  // 直下の項目の ..\ → level1
        "sub/../../../escape4.txt",                                        // → run
        Path.Combine(absTarget, "escape-abs1.txt").Replace('\\', '/'),    // 本当の絶対パス（/ 区切り）
        Path.Combine(absTarget, "escape-abs2.txt"),                        // 本当の絶対パス（\ 区切り）
    ];

    public string[] AbsSentinels() =>
        [Path.Combine(AbsTarget, "escape-abs1.txt"), Path.Combine(AbsTarget, "escape-abs2.txt")];

    /// <summary>1 回の取り出しの場所。毎回新しい run-NNN を作り、不正な名前の当たる所に見張りを新規作成する。作れなければ null</summary>
    public ProbeRun? NewRun()
    {
        var run = Path.Combine(Root, $"run-{++_runs:D3}");
        if (Path.Exists(run)) return null;
        var level1 = Path.Combine(run, "level1");
        var output = Path.Combine(level1, "out");
        Directory.CreateDirectory(output);
        string[] sentinels =
        [
            Path.Combine(level1, "escape1.txt"), Path.Combine(level1, "escape2.txt"), Path.Combine(level1, "escape3.txt"),
            Path.Combine(run, "escape4.txt"),
        ];
        foreach (var sentinel in sentinels) CreateNew(sentinel, "original");
        return new ProbeRun(run, output, [.. sentinels, .. AbsSentinels()]);
    }

    /// <summary>新規作成だけで書く。既にあれば IOException で、そのファイルは変えない</summary>
    internal static void CreateNew(string path, string text)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var writer = new StreamWriter(stream);
        writer.Write(text);
    }

    private static void WriteZip(string path, IReadOnlyList<string> entries, Func<string, DateTime> stamp)
    {
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach (var name in entries)
            {
                var entry = archive.CreateEntry(name);
                var time = stamp(name);
                entry.LastWriteTime = new DateTimeOffset(time, TimeZoneInfo.Local.GetUtcOffset(time));
                using var writer = new StreamWriter(entry.Open());
                writer.Write(name.EndsWith("ok.txt", StringComparison.Ordinal) ? "evil" : name);
            }
        }
        // 入力の検証: 書庫の中の名前が、意図したとおりに入っている（ライブラリが直していない）
        using var check = ZipFile.OpenRead(path);
        if (!check.Entries.Select(e => e.FullName).SequenceEqual(entries))
            throw new InvalidOperationException($"{path} の項目の名前が意図と違う");
    }
}

/// <summary>1 回の取り出しの場所。Output が取り出し先、Sentinels が見張り（すべて新規作成したもの）</summary>
internal sealed record ProbeRun(string Root, string Output, IReadOnlyList<string> Sentinels);
