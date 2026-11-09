using ReTAC.Domain.Navigation;

namespace ReTAC.App;

/// <summary>
/// R-147: フォルダ・ファイルのクイックアクセス項目の `Path` は設定ファイルの形の式（または旧設定の値）。
/// 作る・使う・見せるの入口をここに集める（画面に触れない。テストのため）。
/// Path をファイルシステム・シェル・ジャンプへ直接渡さない。必ず <see cref="Resolve"/> を通す。
/// コマンドの項目の Path は式ではない（文字のまま）。
/// </summary>
internal static class QuickAccessPath
{
    /// <summary>実パスから作る（追加・参照・現在のフォルダ）。式に書いてから保存の形にする。</summary>
    public static string Store(string realPath) => PathExpression.Store(PathExpression.Literal(realPath));

    /// <summary>使う入口（J・↓・履歴 ▼・重複）の共通の実パス。使えない値・コマンドは null。カレントフォルダでは解決しない。</summary>
    public static string? Resolve(QuickAccessEntry entry, Func<string, string?> env) =>
        entry.Kind is BookmarkKind.Folder or BookmarkKind.File ? PathExpression.ResolveSaved(entry.Path, env) : null;

    /// <summary>
    /// 使えない値の知らせ。使えるなら null。旧設定の値は作り直すしかないので別の文にする。
    /// 項目は取り除かない（別の PC では使えるかもしれない）。PathRecall.Pick と同じ文。
    /// </summary>
    public static string? Notice(string stored, Func<string, string?> env)
    {
        if (PathExpression.ResolveSaved(stored, env) is not null) return null;
        var display = PathExpression.Display(stored);
        return PathExpression.Read(stored) is null ? $"{display} は登録し直してください。" : $"{display} は使えません。";
    }

    /// <summary>
    /// 登録ダイアログの欄から保存する値を作る。欄を書き換えていなければ前の値を一字も変えない（旧設定の値を式にしない）。
    /// 置き換えて絶対パスになる入力は正規の形の式のまま保存し、存在は実パスで確かめる（作業ディレクトリ基準にしない）。
    /// 相対パスになる入力は拒む（カレントフォルダを足して登録しない）。
    /// </summary>
    /// <param name="presetStored">編集中の項目の保存した値。新規・実パスの初期値からなら null</param>
    public static (string? Stored, string? Error) FromField(string? presetStored, string fieldText, Func<string, string?> env, Func<string, bool> exists)
    {
        if (presetStored is not null && fieldText == PathExpression.Display(presetStored)) return (presetStored, null);

        var expression = InputText.Unquote(fieldText);   // R-127: この欄はパスの解決の処理を通らない
        var missing = (null as string, $"{fieldText} は存在しません。");
        if (InputText.TrimEdge(expression).Length == 0 || !Path.IsPathFullyQualified(PathExpression.Expand(expression, env))) return missing;

        var stored = PathExpression.Store(PathExpression.Canonical(expression, env));
        return PathExpression.ResolveSaved(stored, env) is { } real && exists(real) ? (stored, null) : missing;
    }

    /// <summary>
    /// ブックマークからの追加。保存した値をそのまま写し、フォルダ・ファイルで使えない値なら追加しない（理由を返す）。
    /// コマンドは式ではないので確かめない。
    /// </summary>
    public static (QuickAccessEntry? Entry, string? Error) FromBookmark(Bookmark b, Func<string, string?> env)
    {
        var entry = new QuickAccessEntry(b.Title, b.Target, b.Kind);
        return b.Kind is BookmarkKind.Folder or BookmarkKind.File && Notice(b.Target, env) is { } error ? (null, error) : (entry, null);
    }
}
