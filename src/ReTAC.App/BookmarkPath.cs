using ReTAC.Domain.Navigation;

namespace ReTAC.App;

/// <summary>
/// R-147: フォルダ・ファイルのブックマークの `Target` は設定ファイルの形の式（または旧設定の値）。
/// 作る・使う・見せるの入口をここに集める（画面に触れない。テストのため）。
/// Target をファイルシステム・シェル・ジャンプへ直接渡さない。必ず <see cref="Resolve"/> を通す。
/// </summary>
internal static class BookmarkPath
{
    private static bool IsPath(Bookmark b) => b.Kind is BookmarkKind.Folder or BookmarkKind.File;

    /// <summary>実パスから作る（現在のフォルダ・カーソル位置・ドラッグ・参照・既定）。式に書いてから保存の形にする。</summary>
    public static string Store(string realPath) => PathExpression.Store(PathExpression.Literal(realPath));

    public static Bookmark ForRealPath(BookmarkKind kind, string realPath) => new("", kind, Store(realPath));

    /// <summary>
    /// 使う入口（移る・開く・ドロップの宛先・シェルのメニュー・アイコン）の共通の実パス。
    /// 使えない（旧設定の値・絶対パスにならない・無効）ものと、パスを持たない種類は null。カレントフォルダでは解決しない。
    /// </summary>
    public static string? Resolve(Bookmark b, Func<string, string?> env) =>
        IsPath(b) ? PathExpression.ResolveSaved(b.Target, env) : null;

    /// <summary>画面（説明・一覧・知らせ）に出す文字。印を外す。</summary>
    public static string Display(Bookmark b) => PathExpression.Display(b.Target);

    /// <summary>
    /// 使えない項目の知らせ。使えるなら null。旧設定の値は作り直すしかないので別の文にする。
    /// 「見つかりません」と同じ知らせにするが、ブックマークは取り除かない（別の PC では使えるかもしれない）。
    /// </summary>
    public static string? Notice(Bookmark b, Func<string, string?> env) =>
        Resolve(b, env) is not null ? null
        : PathExpression.Read(b.Target) is null ? $"{Display(b)} は登録し直してください。"
        : $"{Display(b)} は見つかりません。";

    /// <summary>
    /// R-147: 登録ダイアログの欄から保存する値を作る。欄を書き換えていなければ前の値を一字も変えない（旧設定の値を式にしない）。
    /// 置き換えて絶対パスになる入力は正規の形の式のまま、相対パスになるものはカレントフォルダ基準の実パスを式に書く。
    /// </summary>
    public static (string? Target, string? Error) FromField(Bookmark preset, string fieldText, string currentFolder, Func<string, string?> env)
    {
        var input = InputText.TrimEdge(fieldText);
        if (input.Length == 0) return (null, "登録先を入れてください。");
        if (fieldText == PathExpression.Display(preset.Target)) return (preset.Target, null);
        if (PathResolver.Resolve(currentFolder, input, env) is not { } resolved) return (null, $"{input} はパスとして読めません。");

        var expression = InputText.Unquote(input);   // R-127
        return Path.IsPathFullyQualified(PathExpression.Expand(expression, env))
            ? (PathExpression.Store(PathExpression.Canonical(expression, env)), null)
            : (Store(resolved), null);
    }
}
