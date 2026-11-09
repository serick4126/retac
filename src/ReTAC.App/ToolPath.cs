using ReTAC.Domain.Navigation;
using ReTAC.Shell;

namespace ReTAC.App;

/// <summary>
/// R-147: 外部ツールの実行ファイルのパス（設定ファイルの形の式）から、起動するパスを作る唯一の入口。
/// 起動・設定画面の存在の確認・試しの起動はすべてここを通す（どれかが保存した値を直接 <see cref="ExecutableResolver"/> へ渡すと、
/// 印 `|` が付いた式や `%` を含む式を探せない）。画面にも Process にも触れない（テストのため）。
/// </summary>
internal static class ToolPath
{
    /// <param name="Legacy">旧設定の値（<see cref="PathExpression.Read"/> が null）。起動しない</param>
    /// <param name="Expanded">置き換えた文字。絶対パスに限らない（名前だけ・相対もある）。旧設定の値なら空</param>
    /// <param name="Found">実行ファイルの探し方（名前だけなら PATH と App Paths・相対は今のまま）で見つかったフルパス</param>
    internal readonly record struct Result(bool Legacy, string Expanded, string? Found)
    {
        /// <summary>見つからなければ書かれたまま Windows に任せる（F-03）</summary>
        public string LaunchPath => Found ?? Expanded;
    }

    public static Result Resolve(string stored, Func<string, string?> env) => Resolve(stored, env, ExecutableResolver.Resolve);

    /// <param name="find">テストで差し替える探し方</param>
    internal static Result Resolve(string stored, Func<string, string?> env, Func<string, string?> find)
    {
        if (PathExpression.Read(stored) is not { } expression) return new Result(true, "", null);
        var expanded = PathExpression.Expand(expression, env);
        return new Result(false, expanded, find(expanded));
    }

    /// <summary>
    /// ボタン・メニューのアイコンを取るパス。保存した値をそのまま <c>ShellIcons</c> へ渡すと、印 `|` や `%` を含む式では
    /// 探せずアイコンが出ない。旧設定の値（起動しない）は null。
    /// </summary>
    public static string? IconPath(string stored, Func<string, string?> env) =>
        Resolve(stored, env) is { Legacy: false } r ? r.LaunchPath : null;

    /// <summary>旧設定の値で起動しなかったときの知らせ（R-147）</summary>
    public static string LegacyNotice(string stored) => $"{PathExpression.Display(stored)} を登録し直してください。";
}
