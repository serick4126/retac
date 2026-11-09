namespace ReTAC.Domain.Tests;

/// <summary>
/// R-147 / S8: 環境変数の式の実 OS のテストの作業場所。%TEMP% の下に retac-p32-&lt;GUID&gt; を<b>新しく作るだけ</b>で、
/// 既にあれば作らずに失敗する（既にあるものに触らない）。中のフォルダ・ファイルも新規作成だけで、
/// 終わったとき（成功・失敗・例外のどれでも）に何も変えない・消さない。片付けは利用者が手で行う（%TEMP%\retac-p32-*）。
/// 使い終わったら消す既存の部品は、後始末で属性を戻して丸ごと消すので使わない。
/// </summary>
internal sealed class Phase32Workspace
{
    private Phase32Workspace(string root) => Root = root;

    public string Root { get; }

    /// <summary>%TEMP% の下に新しく作る</summary>
    public static Phase32Workspace Create() => Create(Path.GetTempPath(), "retac-p32-" + Guid.NewGuid().ToString("N"));

    /// <summary>名前を決めて作る（テストの入口）。既にあれば IOException で、そのフォルダには何もしない</summary>
    public static Phase32Workspace Create(string tempRoot, string name)
    {
        var root = Path.Combine(Path.GetFullPath(tempRoot), name);
        if (Path.Exists(root)) throw new IOException($"{root} は既にある。触らない");
        Directory.CreateDirectory(root);
        return new Phase32Workspace(root);
    }

    /// <summary>作業場所の中に新しいフォルダを作る（途中のフォルダも含む）。最後のフォルダが既にあれば IOException</summary>
    public string CreateFolder(params string[] relative)
    {
        var path = Path.Combine([Root, .. relative]);
        if (Path.Exists(path)) throw new IOException($"{path} は既にある。触らない");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>作業場所の中に新しいファイルを作る。新規作成だけで、既にあれば IOException でそのファイルは変えない。親のフォルダは作る</summary>
    public string CreateFile(string text, params string[] relative)
    {
        var path = Path.Combine([Root, .. relative]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var writer = new StreamWriter(stream);
        writer.Write(text);
        return path;
    }
}
