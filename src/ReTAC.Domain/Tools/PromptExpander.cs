using ReTAC.Domain.Navigation;

namespace ReTAC.Domain.Tools;

/// <summary>R-130: 入力ダイアログの定義の引数の並びを、<c>${prompt}</c> の位置に展開する。</summary>
public static class PromptExpander
{
    /// <returns>展開した引数。マクロを含む引数の行の <c>!</c> で起動しないと決まったら null</returns>
    public static IReadOnlyList<string>? Expand(PromptInput input, MacroContext context)
    {
        var result = new List<string>();
        foreach (var argument in input.Definition.Arguments)
        {
            switch (argument.Kind)
            {
                case PromptArgumentKind.Fixed:
                    result.Add(argument.Text);   // 構文を通さない。空なら空の引数（インポートした値を変えない。R-134）
                    break;
                case PromptArgumentKind.Template:
                    // ${prompt} は定義の検査で弾いてある。入れ子にしない
                    if (ArgumentExpander.Expand(ArgumentTemplate.Parse(argument.Text), context with { Prompt = null }) is not { } expanded) return null;
                    result.AddRange(expanded);
                    break;
                case PromptArgumentKind.Item when input.Definition.ItemOf(argument.ItemId) is { } item:
                    result.AddRange(ItemArguments(item, input.Values.GetValueOrDefault(item.Id) ?? new PromptValue(), context.PathForm));
                    break;
            }
        }
        return result;
    }

    /// <summary>1 つの項目が送る引数。</summary>
    public static IReadOnlyList<string> ItemArguments(PromptItem item, PromptValue value, Func<string, string>? pathForm)
    {
        switch (item.Kind)
        {
            case PromptItemKind.CheckBox:
                return value.Checked ? ArgumentSplitter.Split(item.Value) ?? [] : [];
            case PromptItemKind.DropDown:
                // 存在しない選択肢の番号は何も送らない（例外にしない）
                return item.Choices.FirstOrDefault(c => c.Id == value.ChoiceId) is { } choice ? ArgumentSplitter.Split(choice.Value) ?? [] : [];
            default:
                // 必須でない空欄は付ける文字ごと送らない（-ss だけが残らない）。フォルダの空欄は検査でカレントフォルダになっている
                if (InputText.TrimEdge(value.Text).Length == 0) return [];
                var path = item.Kind is PromptItemKind.Folder or PromptItemKind.File;
                // 長いパスは外部のアプリが開けないので、${file} と同じく短い形にする（ToolLauncher.TargetPath）
                var text = path && pathForm is not null ? pathForm(value.Text) : value.Text;
                return ArgumentSplitter.Join(item.Prefix, text, item.Suffix, mergeBackslash: path) ?? [];
        }
    }
}

/// <summary>R-133: ヘルパーの「結果の例」。送る引数を 1 行にする（ツールのパスは含めない）。</summary>
public static class PromptExample
{
    /// <summary>マクロを含む引数の行は書いたまま、項目は与えた値で作る。フォルダの空欄は <c>${cwd}</c>（設定画面ではカレントフォルダが確かでない）。</summary>
    public static string Render(PromptDefinition definition, IReadOnlyDictionary<int, PromptValue> values)
    {
        var parts = new List<string>();
        foreach (var argument in definition.Arguments)
        {
            switch (argument.Kind)
            {
                case PromptArgumentKind.Fixed:
                    parts.Add(LaunchRequest.Quote(argument.Text));
                    break;
                case PromptArgumentKind.Template:
                    parts.Add(argument.Text);
                    break;
                case PromptArgumentKind.Item when definition.ItemOf(argument.ItemId) is { } item:
                    var value = values.GetValueOrDefault(item.Id) ?? new PromptValue();
                    if (item.Kind is PromptItemKind.Folder or PromptItemKind.File)
                    {
                        var text = InputText.Unquote(value.Text);
                        value = value with { Text = text.Length == 0 && item.Kind == PromptItemKind.Folder ? "${cwd}" : text };
                    }
                    parts.AddRange(PromptExpander.ItemArguments(item, value, null).Select(LaunchRequest.Quote));
                    break;
            }
        }
        return string.Join(" ", parts);
    }
}
