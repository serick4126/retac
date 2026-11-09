using System.Text.Json;
using ReTAC.Domain.Navigation;

namespace ReTAC.Domain.Tools;

/// <param name="Text">テキスト・フォルダ・ファイルの入力欄の文字</param>
/// <param name="Checked">チェックボックス</param>
/// <param name="ChoiceId">ドロップダウンリストで選んだ選択肢の番号。指す先は <see cref="PromptChoice.Id"/></param>
public sealed record PromptValue(string Text = "", bool Checked = false, int ChoiceId = 0);

/// <summary>展開に使う定義と値（R-130）。値はフォルダが絶対パス（空欄はカレントフォルダ）、ファイルが絶対パスか空。</summary>
public sealed record PromptInput(PromptDefinition Definition, IReadOnlyDictionary<int, PromptValue> Values);

/// <param name="Values">展開に使う値。誤りがあれば null</param>
/// <param name="ItemId">誤りのある項目</param>
public sealed record PromptCheck(IReadOnlyDictionary<int, PromptValue>? Values, int? ItemId, string? Error);

/// <summary>入力ダイアログの値の検査・初期値・前回の値（R-131 / R-132）。</summary>
public static class PromptAnswerRules
{
    /// <summary>R-133: 例に使う未展開の定義の値。</summary>
    public static IReadOnlyDictionary<int, PromptValue> DefinedValues(PromptDefinition definition) =>
        ById(definition, i => new PromptValue(IsPath(i) ? PathExpression.Display(i.Initial) : i.Initial, i.InitialChecked, i.InitialChoiceId));

    /// <summary>R-147: 項目の初期値を画面に出す形（フォルダ・ファイルの項目は設定ファイルの印を外す。テンプレートのまま、展開しない）。</summary>
    public static string InitialForDisplay(PromptItem item) => IsPath(item) ? PathExpression.Display(item.Initial) : item.Initial;

    /// <summary>
    /// R-147: 項目の編集の OK で、初期値の欄の文字を項目に残す形にする。フォルダ・ファイルは地の文字の正規の形を設定ファイルの形で残す。
    /// 書き換えていない欄は元の値のまま残す（旧設定の値を、開いて OK しただけで書き換えない）。
    /// </summary>
    public static string InitialToStore(PromptItem source, string fieldText, Func<string, string?>? env = null) =>
        !IsPath(source) ? fieldText
        : fieldText == InitialForDisplay(source) ? source.Initial
        : PathExpression.Store(ScalarTemplate.Canonical(fieldText, Env(env)));

    private static bool IsPath(PromptItem item) => item.Kind is PromptItemKind.Folder or PromptItemKind.File;

    /// <summary>
    /// R-147: 環境変数の読み取り。<c>null</c> は何も定義されていない環境（式の `%名前%` は文字のまま）。
    /// App は <c>Environment.GetEnvironmentVariable</c> を渡す。
    /// </summary>
    private static Func<string, string?> Env(Func<string, string?>? env) => env ?? (static _ => null);

    /// <summary>R-136: 定義の初期値を展開する。失敗したら部分的な値を返さない。</summary>
    public static PromptCheck Defaults(PromptDefinition definition, MacroContext context, Func<string, string?>? env = null) =>
        Initial(definition with { RememberLast = false }, context, env);

    /// <summary>R-132 / R-136: 前回の文字は再解析せず、前回値がない欄だけ初期値を展開する。</summary>
    public static PromptCheck Initial(PromptDefinition definition, MacroContext context, Func<string, string?>? env = null)
    {
        var lookup = Env(env);
        var values = new Dictionary<int, PromptValue>();
        foreach (var item in definition.Items)
        {
            var text = "";
            if (item.Kind is PromptItemKind.Text or PromptItemKind.Folder or PromptItemKind.File)
            {
                // R-147 / R-132: フォルダ・ファイルの前回の入力は設定ファイルの形。旧設定の値（Read が null）は式として読めないので使わず、定義の初期値へ進む
                if (definition.RememberLast && item.LastText is { } last && (!IsPath(item) || PathExpression.Read(last) is not null))
                    text = IsPath(item) ? PathExpression.Read(last)! : last;
                // R-147 / R-136: 旧設定の初期値のテンプレートは使わない（その項目は空欄から始まる）
                else if (IsPath(item) && PathExpression.Read(item.Initial) is null) text = "";
                else
                {
                    var source = IsPath(item) ? PathExpression.Read(item.Initial)! : item.Initial;
                    var template = ScalarTemplate.Parse(source);
                    // R-147 / R-136: フォルダ・ファイルは部品の並びを 1 回で式に書く。テキストは今までどおり文字列
                    MacroName? emptyRequired;
                    string? expanded;
                    if (IsPath(item))
                    {
                        var parts = template.ExpandParts(context, out emptyRequired);
                        expanded = parts is null ? null : PathExpression.Write(parts, lookup);
                    }
                    else expanded = template.Expand(context, out emptyRequired);
                    if (expanded is null)
                    {
                        var reason = template.Errors.FirstOrDefault()?.Message
                            ?? $"${{{ArgumentTemplate.Names.First(n => n.Value == emptyRequired).Key}}}! の値が空です。";
                        return new(null, item.Id, $"「{item.Label}」の初期値: {reason}");
                    }
                    text = expanded;
                }
            }
            var check = definition.RememberLast ? item.LastChecked ?? item.InitialChecked : item.InitialChecked;
            var choice = definition.RememberLast && item.LastChoiceId is { } id && item.Choices.Any(c => c.Id == id)
                ? id : item.InitialChoiceId;
            values[item.Id] = new(text, check, choice);
        }
        return new(values, null, null);
    }

    /// <summary>
    /// 番号が重なっていても例外にしない（後の項目が勝つ）。手で直した設定ファイルでもヘルパーを開いて直せるように。
    /// 重なりは <see cref="PromptDefinitionRules.Validate"/> が知らせる。<c>ToDictionary</c> は使わない。
    /// </summary>
    private static Dictionary<int, PromptValue> ById(PromptDefinition definition, Func<PromptItem, PromptValue> value)
    {
        var values = new Dictionary<int, PromptValue>();
        foreach (var item in definition.Items) values[item.Id] = value(item);
        return values;
    }

    /// <summary>
    /// R-131: OK のときの検査と、展開に使う値への変換。表示順で最初の誤りだけを返す。
    /// フォルダ・ファイルは端の空白と引用符を外し（R-127）、カレントフォルダ基準で絶対パスにする（R-61）。存在は確かめない。
    /// </summary>
    public static PromptCheck Check(PromptDefinition definition, IReadOnlyDictionary<int, PromptValue> input, string currentFolder, Func<string, string?> env)
    {
        var values = new Dictionary<int, PromptValue>();
        foreach (var item in definition.Items)
        {
            var value = input.GetValueOrDefault(item.Id) ?? new PromptValue();
            switch (item.Kind)
            {
                case PromptItemKind.Text:
                    if (item.Required && InputText.TrimEdge(value.Text).Length == 0) return Fail(item, $"{item.Label}を入力してください。");
                    values[item.Id] = value;
                    break;
                case PromptItemKind.Folder or PromptItemKind.File:
                    // 空かどうかは引用符を外した後で判断する。解決には入力のままを渡す（Resolve が 1 回だけ外す。2 回外すと ""C:\x"" が通ってしまう）
                    if (InputText.Unquote(value.Text).Length == 0)
                    {
                        if (item.Required) return Fail(item, $"{item.Label}を入力してください。");
                        values[item.Id] = value with { Text = item.Kind == PromptItemKind.Folder && item.FolderEmptyBehavior == PromptFolderEmptyBehavior.CurrentFolder ? currentFolder : "" };
                        break;
                    }
                    if (PathResolver.Resolve(currentFolder, value.Text, env) is not { } resolved) return Fail(item, $"{item.Label}のパスが正しくありません。");
                    values[item.Id] = value with { Text = resolved };
                    break;
                default:
                    values[item.Id] = value;
                    break;
            }
        }
        return new PromptCheck(values, null, null);

        static PromptCheck Fail(PromptItem item, string message) => new(null, item.Id, message);
    }

    /// <summary>R-131: フォルダ履歴に入れるパス（表示順）。空欄（カレントフォルダ）は入れない。ファイルの項目は入れない。</summary>
    public static IReadOnlyList<string> HistoryFolders(
        PromptDefinition definition, IReadOnlyDictionary<int, PromptValue> input, IReadOnlyDictionary<int, PromptValue> resolved) =>
        [.. definition.Items
            .Where(i => i.Kind == PromptItemKind.Folder && InputText.Unquote(input.GetValueOrDefault(i.Id)?.Text ?? "").Length > 0)
            .Select(i => resolved[i.Id].Text)];

    /// <summary>R-132: OK した入力を前回の値として残す。入力欄の文字はそのまま（空欄も「空欄だった」として残す）。</summary>
    public static PromptDefinition Remember(PromptDefinition definition, IReadOnlyDictionary<int, PromptValue> input, Func<string, string?>? env = null)
    {
        if (!definition.RememberLast) return definition;
        return definition with
        {
            Items = [.. definition.Items.Select(i => input.GetValueOrDefault(i.Id) is not { } value ? i : i.Kind switch
            {
                PromptItemKind.CheckBox => i with { LastChecked = value.Checked },
                PromptItemKind.DropDown => i with { LastChoiceId = value.ChoiceId },
                // R-147 / R-132: フォルダ・ファイルは正規の形の式を設定ファイルの形で残す（欄の文字が別の PC・後の環境で意味を変えないように）。空欄は空欄のまま
                PromptItemKind.Folder or PromptItemKind.File => i with { LastText = PathExpression.Store(PathExpression.Canonical(value.Text, Env(env))) },
                _ => i with { LastText = value.Text },
            })],
        };
    }

    /// <summary>R-132: 「前回の入力を初期値にする」をオフにしたら、そのツールの前回の値をすべて消す。</summary>
    public static PromptDefinition ForgetLast(PromptDefinition definition) => definition with
    {
        Items = [.. definition.Items.Select(i => i with { LastText = null, LastChecked = null, LastChoiceId = null })],
    };

    /// <summary>前回の値を除いた中身が同じか。前回の値を残すだけの書き換えと、定義の編集を見分ける。</summary>
    public static bool SameShape(PromptDefinition a, PromptDefinition b) =>
        // 型が List を持つので、record の == では中身を比べられない。前回の値を消してから JSON にして比べる
        JsonSerializer.Serialize(ForgetLast(a)) == JsonSerializer.Serialize(ForgetLast(b));

    /// <summary>
    /// R-132: 外部ツールの一覧に前回の値を書き込む。今のそのツールの定義が、開いた定義（<paramref name="shown"/>）と
    /// 前回の値を除いて同じときだけ。入力の間に設定ダイアログで定義を直した・ツールを消したなら、古い定義の値で上書きしない。
    /// 同じインスタンスかでは判断しない: 同じツールを 2 つのウィンドウから開くと、先の OK で定義のインスタンスが替わり、後の OK が残らなくなる。
    /// </summary>
    /// <returns>書き込んだ一覧。残さないときは null</returns>
    public static List<ExternalTool>? RememberIn(
        IReadOnlyList<ExternalTool> tools, int toolId, PromptDefinition shown, IReadOnlyDictionary<int, PromptValue> input, Func<string, string?>? env = null)
    {
        if (!shown.RememberLast) return null;
        var list = tools.ToList();
        var index = list.FindIndex(t => t.Id == toolId);
        if (index < 0 || list[index].Prompt is not { } current || !SameShape(current, shown)) return null;
        list[index] = list[index] with { Prompt = Remember(current, input, env) };
        return list;
    }
}
