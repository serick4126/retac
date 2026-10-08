namespace ReTAC.Domain.Tests;

/// <summary>シェルの COM（圧縮フォルダ・IFileOperation）は STA で呼ぶ。xunit のスレッドは MTA。</summary>
internal static class Sta
{
    public static void Run(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception e) { error = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    /// <summary>
    /// %TEMP% の下に毎回新しいフォルダを作り、終わっても消さない（片付けは利用者が手で行う。S8）。
    /// ジャンクションや読み取り専用のファイルを残すテスト用。消す側（WithTempFolder）は、リンクを含むフォルダを正しく消せない。
    /// </summary>
    public static void WithKeptFolder(Action<string> action) =>
        action(Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retac-vtest-kept-" + Guid.NewGuid().ToString("N"))).FullName);

    /// <summary>%TEMP% の下に使い捨てのフォルダを作り、終わったら消す。名前は ReTAC- で始めない（本物の一時フォルダと見分ける）</summary>
    public static void WithTempFolder(Action<string> action)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retac-vtest-" + Guid.NewGuid().ToString("N"))).FullName;
        try { action(root); }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }
}
