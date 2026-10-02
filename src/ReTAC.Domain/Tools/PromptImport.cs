using ReTAC.Domain.Navigation;

namespace ReTAC.Domain.Tools;

/// <summary>R-134: 固定の引数の値の見た目。目安で、何も自動では変えない。</summary>
public enum ArgumentMark
{
    None,
    Switch,
    Path,
}

/// <summary>パスの印の行が実際に指しているもの（App が調べて渡す）。</summary>
public enum PathKind
{
    File,
    Folder,
    Missing,
}

public enum ConversionTarget
{
    CheckBox,
    Text,
    FolderItem,
    FileItem,
    MacroFile,
    MacroCursorFile,
    MacroPath,
    MacroCwd,
    DropDown,
}

public sealed record ConversionCandidate(ConversionTarget Target, string Label, bool Enabled = true, string? Reason = null);

/// <param name="Path">ツールのパス（端の引用符を外したもの）</param>
public sealed record ImportedCommand(string Path, IReadOnlyList<PromptArgument> Arguments);

/// <summary>R-134: 読取りに失敗した理由を入力画面へ返す。成功時は Command のみを持つ。</summary>
public sealed record PromptImportRead(ImportedCommand? Command, string? Error);

/// <summary>
/// R-134: ふだんのコマンドラインから定義を組み立てる。読み込んだ直後は貼り付けたものと同じ引数の並びが送られることを保証し
/// （すべて固定の引数）、項目やマクロへの変換は利用者が行ごとに選ぶ。
/// </summary>
public static class PromptImport
{
    public static ImportedCommand? Parse(string commandLine) => Read(commandLine).Command;

    /// <summary>R-134: 実行ファイルを先に切り出す。存在・PATH によって境界を変えない。</summary>
    public static PromptImportRead Read(string commandLine)
    {
        static PromptImportRead Fail(string reason) => new(null, reason);
        if (commandLine.IndexOfAny(['\r', '\n']) >= 0) return Fail("コマンドラインに改行は書けません。");
        var start = 0;
        while (start < commandLine.Length && ArgumentSplitter.IsSeparator(commandLine[start])) start++;
        if (start == commandLine.Length) return Fail("実行ファイルのパスを入れてください。");

        string path;
        int end;
        if (commandLine[start] == '"')
        {
            var close = commandLine.IndexOf('"', start + 1);
            if (close < 0) return Fail("実行ファイルのパスの引用符（\"）が閉じていません。");
            path = commandLine[(start + 1)..close];
            end = close + 1;
            if (end < commandLine.Length && !ArgumentSplitter.IsSeparator(commandLine[end]))
                return Fail("パスを閉じる引用符（\"）の後には空白を入れてください。");
        }
        else
        {
            end = start;
            while (end < commandLine.Length && !ArgumentSplitter.IsSeparator(commandLine[end])) end++;
            var first = commandLine[start..end];
            var pathLike = first.Contains('\\') || first.Contains('/')
                || (first.Length >= 2 && char.IsAsciiLetter(first[0]) && first[1] == ':');
            if (pathLike)
            {
                for (var i = start; i + 4 <= commandLine.Length; i++)
                {
                    if (commandLine[i] != '.') continue;
                    var ext = commandLine.AsSpan(i, 4);
                    if (!ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                        && !ext.Equals(".com", StringComparison.OrdinalIgnoreCase)
                        && !ext.Equals(".bat", StringComparison.OrdinalIgnoreCase)
                        && !ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase)) continue;
                    var after = i + 4;
                    if (after != commandLine.Length && !ArgumentSplitter.IsSeparator(commandLine[after])) continue;
                    end = after;
                    break;
                }
            }
            path = commandLine[start..end];
            if (path.Contains('"')) return Fail("実行ファイルのパスは全体を引用符（\"）で囲んでください。");
        }
        if (InputText.TrimEdge(path).Length == 0) return Fail("実行ファイルのパスが空です。");
        if (ArgumentSplitter.Split(commandLine[end..]) is not { } args)
            return Fail("引数の引用符（\"）が閉じていません。");
        return new(new ImportedCommand(path, [.. args.Select(PromptArgument.Fixed)]), null);
    }

    public static ArgumentMark Mark(string value)
    {
        if (value.Length >= 2 && (value[0] is '-' or '/') && !value.Contains('\\')) return ArgumentMark.Switch;
        if (value.StartsWith(@"\\", StringComparison.Ordinal) || value.Contains('\\')
            || (value.Length >= 2 && char.IsAsciiLetter(value[0]) && value[1] == ':')) return ArgumentMark.Path;
        return ArgumentMark.None;
    }

    /// <summary>R-139: 項目への変換は内容で制限しない。上限でも候補を隠さず理由を表示する。</summary>
    public static IReadOnlyList<ConversionCandidate> Candidates(string value, PathKind? kind, int itemCount)
    {
        ConversionTarget[] items = [ConversionTarget.Text, ConversionTarget.FolderItem, ConversionTarget.FileItem, ConversionTarget.CheckBox, ConversionTarget.DropDown];
        var enabled = itemCount < PromptDefinitionRules.MaxItems;
        var result = items.Select(t => new ConversionCandidate(t, LabelOf(t), enabled, enabled ? null : "項目は 10 個までです。")).ToList();
        ConversionTarget[] macros = Mark(value) switch
        {
            ArgumentMark.Path when kind == PathKind.Folder || value.EndsWith('\\') => [ConversionTarget.MacroPath, ConversionTarget.MacroCwd],
            ArgumentMark.Path when kind == PathKind.File => [ConversionTarget.MacroFile, ConversionTarget.MacroCursorFile, ConversionTarget.MacroPath],
            ArgumentMark.Path => [ConversionTarget.MacroFile],
            _ => [],
        };
        result.AddRange(macros.Select(t => new ConversionCandidate(t, LabelOf(t))));
        return result;
    }

    internal static string EscapeMacroLiteral(string value) => value.Replace("$", "$$", StringComparison.Ordinal);

    public static string? ConversionWarning(string value, ConversionTarget target) =>
        target is ConversionTarget.CheckBox or ConversionTarget.DropDown && value.Contains('"')
        ? "元の値に引用符があるため、送る値をそのまま下書きに入れました。保存する前に送る値を編集してください。" : null;

    private static bool IsItem(ConversionTarget target) =>
        target is ConversionTarget.CheckBox or ConversionTarget.Text or ConversionTarget.FolderItem or ConversionTarget.FileItem or ConversionTarget.DropDown;

    private static string LabelOf(ConversionTarget target) => target switch
    {
        ConversionTarget.CheckBox => "チェックボックス",
        ConversionTarget.Text => "テキスト",
        ConversionTarget.FolderItem => "フォルダ",
        ConversionTarget.FileItem => "ファイル",
        ConversionTarget.DropDown => "ドロップダウンリスト",
        ConversionTarget.MacroFile => "${file}（マークしたファイル。フォルダを含まない）",
        ConversionTarget.MacroCursorFile => "${cursorFile}（マークに関係なくカーソルの 1 件）",
        ConversionTarget.MacroPath => "${path}（フォルダも含む）",
        _ => "${cwd}（カレントフォルダ）",
    };

    /// <summary>
    /// R-139: 固定の引数の 1 行を変換する。元の値を下書きへ引き継ぎ、未完成でも項目を末尾に足す。
    /// マクロに変えたときは、その行をマクロを含む引数の行にする（送るものは変わる）。
    /// </summary>
    public static PromptDefinition Convert(PromptDefinition definition, int index, ConversionTarget target)
    {
        if (index < 0 || index >= definition.Arguments.Count || definition.Arguments[index].Kind != PromptArgumentKind.Fixed
            || !Enum.IsDefined(target) || IsItem(target) && definition.Items.Count >= PromptDefinitionRules.MaxItems) return definition;
        var value = definition.Arguments[index].Text;
        var arguments = definition.Arguments.ToList();
        var macro = target switch
        {
            ConversionTarget.MacroFile => "${file}",
            ConversionTarget.MacroCursorFile => "${cursorFile}",
            ConversionTarget.MacroPath => "${path}",
            ConversionTarget.MacroCwd => "${cwd}",
            _ => null,
        };
        if (macro is not null)
        {
            arguments[index] = PromptArgument.Template(macro);
            return definition with { Arguments = arguments };
        }

        var id = definition.NextItemId();
        var item = target switch
        {
            ConversionTarget.CheckBox => new PromptItem
            {
                Id = id, Kind = PromptItemKind.CheckBox, Label = value, Value = ArgumentSplitter.QuoteOne(EscapeMacroLiteral(value)) ?? EscapeMacroLiteral(value), InitialChecked = true,
            },
            ConversionTarget.FolderItem => new PromptItem
            {
                // 初期値から \ を外さない（C:\ が C: になると、そのドライブのカレントフォルダを指す）。重なった \ は展開で 1 つになる
                Id = id, Kind = PromptItemKind.Folder, Label = value, Initial = EscapeMacroLiteral(value), Suffix = value.EndsWith('\\') ? @"\" : "",
            },
            ConversionTarget.FileItem => new PromptItem { Id = id, Kind = PromptItemKind.File, Label = value, Initial = EscapeMacroLiteral(value) },
            ConversionTarget.DropDown => new PromptItem
            {
                Id = id, Kind = PromptItemKind.DropDown, Label = value, InitialChoiceId = 1,
                Choices = [new PromptChoice { Id = 1, Label = value, Value = ArgumentSplitter.QuoteOne(EscapeMacroLiteral(value)) ?? EscapeMacroLiteral(value) }],
            },
            _ => new PromptItem { Id = id, Kind = PromptItemKind.Text, Label = value, Initial = EscapeMacroLiteral(value) },
        };
        arguments[index] = PromptArgument.Item(id);
        return definition with { Items = [.. definition.Items, item], Arguments = arguments };
    }

    /// <summary>送る順で連続した、空でなく <c>"</c> を含まない固定の引数の 2 行以上で、項目が 10 個未満なら、1 つのチェックボックスにまとめられる。</summary>
    public static bool CanMerge(PromptDefinition definition, IReadOnlyList<int> indices)
    {
        if (indices.Count < 2 || definition.Items.Count >= PromptDefinitionRules.MaxItems) return false;
        var sorted = indices.Order().ToList();
        for (var k = 1; k < sorted.Count; k++)
        {
            if (sorted[k] != sorted[k - 1] + 1) return false;
        }
        return sorted.All(i => i >= 0 && i < definition.Arguments.Count
            && definition.Arguments[i] is { Kind: PromptArgumentKind.Fixed, Text: { Length: > 0 } text } && !text.Contains('"'));
    }

    /// <summary>まとめるチェックボックスの下書き。ラベルは選んだ行の値を送る順に半角空白でつないだもの（引用符なしの表示用。R-134）、送る値は各行を 1 つの引数の形にして送る順に半角空白でつなぐ。</summary>
    public static PromptItem MergeDraft(PromptDefinition definition, IReadOnlyList<int> indices)
    {
        var values = indices.Order().Select(i => definition.Arguments[i].Text).ToList();
        return new PromptItem
        {
            Id = definition.NextItemId(),
            Kind = PromptItemKind.CheckBox,
            Label = string.Join(" ", values),
            InitialChecked = true,
            Value = string.Join(" ", values.Select(v => ArgumentSplitter.QuoteOne(EscapeMacroLiteral(v))!)),
        };
    }

    /// <summary>選んだ行をすべて取り除き、最初の行の位置に項目の行を 1 つ入れる。項目は末尾に足す。</summary>
    public static PromptDefinition ApplyMerge(PromptDefinition definition, IReadOnlyList<int> indices, PromptItem item)
    {
        var sorted = indices.Order().ToList();
        var arguments = definition.Arguments.ToList();
        arguments.RemoveRange(sorted[0], sorted.Count);
        arguments.Insert(sorted[0], PromptArgument.Item(item.Id));
        return definition with { Items = [.. definition.Items, item], Arguments = arguments };
    }
}
