using ReTAC.Domain.Navigation;

namespace ReTAC.App;

/// <summary>
/// R-147: パスの欄・ジャンプの入口・参照の部品へ実パスを渡すときの文字を作る（画面に触れない。テストのため）。
/// 欄の文字は「式」なので、実パスをそのまま入れると `%` を含む名前が環境変数として読み直されてしまう。
/// </summary>
internal static class PathField
{
    /// <summary>実パスを欄へ入れる文字。実パスを欄の Text へ直接代入しない。</summary>
    public static string Text(string realPath) => PathExpression.Literal(realPath);

    /// <summary>
    /// パンくず・一覧が JumpRequested で出す文字。ジャンプの入口が受け取るのはいつも式（欄の文字と同じ経路で解決する）なので、
    /// 実パスは式に書いて渡す。
    /// </summary>
    public static string JumpText(string realPath) => PathExpression.Literal(realPath);

    /// <summary>
    /// R-52-2: 欄の文字から参照の最初の場所を作る。参照の部品が受け取るのは実パスなので、解決の処理を通す。
    /// 空・解決できないときは null（呼び出し側はカレントフォルダで開く）。
    /// </summary>
    public static string? BrowseStartFromField(string text, string currentFolder, Func<string, string?> env) =>
        InputText.Unquote(text).Length == 0 ? null : PathResolver.Resolve(currentFolder, text, env);

    /// <summary>R-52-2: 保存した値から参照の最初の場所を作る。使えない値・旧設定の値は null（カレントフォルダで開く）。</summary>
    public static string? BrowseStartFromSaved(string saved, Func<string, string?> env) =>
        PathExpression.ResolveSaved(saved, env);
}
