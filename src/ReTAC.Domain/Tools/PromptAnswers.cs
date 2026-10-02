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
        ById(definition, i => new PromptValue(i.Initial, i.InitialChecked, i.InitialChoiceId));

    /// <summary>R-136: 定義の初期値を展開する。失敗したら部分的な値を返さない。</summary>
    public static PromptCheck Defaults(PromptDefinition definition, MacroContext context) =>
        Initial(definition with { RememberLast = false }, context);

    /// <summary>R-132 / R-136: 前回の文字は再解析せず、前回値がない欄だけ初期値を展開する。</summary>
    public static PromptCheck Initial(PromptDefinition definition, MacroContext context)
    {
        var values = new Dictionary<int, PromptValue>();
        foreach (var item in definition.Items)
        {
            var text = "";
            if (item.Kind is PromptItemKind.Text or PromptItemKind.Folder or PromptItemKind.File)
            {
                if (definition.RememberLast && item.LastText is { } last) text = last;
                else
                {
                    var template = ScalarTemplate.Parse(item.Initial);
                    var expanded = template.Expand(context, out var emptyRequired);
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
    public static PromptCheck Check(PromptDefinition definition, IReadOnlyDictionary<int, PromptValue> input, string currentFolder)
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
                    if (PathResolver.Resolve(currentFolder, value.Text) is not { } resolved) return Fail(item, $"{item.Label}のパスが正しくありません。");
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
    public static PromptDefinition Remember(PromptDefinition definition, IReadOnlyDictionary<int, PromptValue> input)
    {
        if (!definition.RememberLast) return definition;
        return definition with
        {
            Items = [.. definition.Items.Select(i => input.GetValueOrDefault(i.Id) is not { } value ? i : i.Kind switch
            {
                PromptItemKind.CheckBox => i with { LastChecked = value.Checked },
                PromptItemKind.DropDown => i with { LastChoiceId = value.ChoiceId },
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
        IReadOnlyList<ExternalTool> tools, int toolId, PromptDefinition shown, IReadOnlyDictionary<int, PromptValue> input)
    {
        if (!shown.RememberLast) return null;
        var list = tools.ToList();
        var index = list.FindIndex(t => t.Id == toolId);
        if (index < 0 || list[index].Prompt is not { } current || !SameShape(current, shown)) return null;
        list[index] = list[index] with { Prompt = Remember(current, input) };
        return list;
    }
}
