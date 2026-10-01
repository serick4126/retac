using ReTAC.Domain.Navigation;

namespace ReTAC.Domain.Tools;

/// <param name="ItemId">誤りのある項目。ヘルパーがその行を選ぶ</param>
/// <param name="ArgumentIndex">誤りのある引数の行（0 から）</param>
public sealed record PromptProblem(string Message, int? ItemId = null, int? ArgumentIndex = null);

/// <summary>
/// 入力ダイアログの定義の検査（R-130 / R-133）。ヘルパーの OK・設定ページの OK・起動の直前（手で直した設定ファイル）で同じものを使う。
/// 例外ではなく誤りの一覧を返す。
/// </summary>
public static class PromptDefinitionRules
{
    public const int MaxItems = 10;

    public static IReadOnlyList<PromptProblem> Validate(PromptDefinition definition)
    {
        var problems = new List<PromptProblem>();
        if (definition.Items.Count > MaxItems) problems.Add(new($"項目は {MaxItems} 個までです。"));

        var ids = new HashSet<int>();
        foreach (var item in definition.Items)
        {
            if (!ids.Add(item.Id)) problems.Add(new("項目の番号が重なっています。", item.Id));
            var name = InputText.TrimEdge(item.Label);
            if (name.Length == 0) problems.Add(new("ラベルが空の項目があります。", item.Id));
            // JsonStringEnumConverter は数値も読む。手で直した設定ファイルの 999 を黙って通すと、引数が欠けたまま起動してしまう
            if (!Enum.IsDefined(item.Kind)) problems.Add(new($"「{(name.Length > 0 ? name : "（ラベルなし）")}」の種類が正しくありません。", item.Id));
            CheckItem(item, name.Length > 0 ? name : "（ラベルなし）", problems);
        }

        for (var i = 0; i < definition.Arguments.Count; i++)
        {
            var argument = definition.Arguments[i];
            if (!Enum.IsDefined(argument.Kind))
            {
                problems.Add(new($"引数の {i + 1} 行目の種類が正しくありません。", ArgumentIndex: i));
                continue;
            }
            switch (argument.Kind)
            {
                case PromptArgumentKind.Item when definition.ItemOf(argument.ItemId) is null:
                    problems.Add(new($"引数の {i + 1} 行目が指す項目がありません。", ArgumentIndex: i));
                    break;
                case PromptArgumentKind.Template:
                    var template = ArgumentTemplate.Parse(argument.Text);
                    if (template.HasPrompt)
                        problems.Add(new($"引数の {i + 1} 行目に ${{prompt}} は書けません。", ArgumentIndex: i));
                    foreach (var error in template.Errors)
                        problems.Add(new($"引数の {i + 1} 行目: {error.Message}", ArgumentIndex: i));
                    break;
            }
        }

        foreach (var item in definition.Items)
        {
            if (!definition.Arguments.Any(a => a.Kind == PromptArgumentKind.Item && a.ItemId == item.Id))
                problems.Add(new($"「{InputText.TrimEdge(item.Label)}」が引数に入っていません。", item.Id));
        }
        return problems;
    }

    private static void CheckItem(PromptItem item, string name, List<PromptProblem> problems)
    {
        void Add(string message) => problems.Add(new(message, item.Id));

        switch (item.Kind)
        {
            case PromptItemKind.Text or PromptItemKind.Folder or PromptItemKind.File:
                if (ArgumentSplitter.Split(item.Prefix) is null) Add($"「{name}」の「前に付ける」の引用符（\"）が閉じていません。");
                if (ArgumentSplitter.Split(item.Suffix) is null) Add($"「{name}」の「後ろに付ける」の引用符（\"）が閉じていません。");
                break;
            case PromptItemKind.CheckBox:
                if (InputText.TrimEdge(item.Value).Length == 0) Add($"「{name}」の送る値が空です。");
                else if (ArgumentSplitter.Split(item.Value) is null) Add($"「{name}」の送る値の引用符（\"）が閉じていません。");
                break;
            case PromptItemKind.DropDown:
                if (item.Choices.Count == 0)
                {
                    Add($"「{name}」に選択肢がありません。");
                    break;
                }
                if (item.Choices.Any(c => InputText.TrimEdge(c.Label).Length == 0)) Add($"「{name}」に表示名が空の選択肢があります。");
                foreach (var label in item.Choices.Select(c => c.Label).Where(l => l.Length > 0).GroupBy(l => l, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key))
                    Add($"「{name}」の選択肢の表示名「{label}」が重なっています。");
                if (item.Choices.Select(c => c.Id).Distinct().Count() != item.Choices.Count) Add($"「{name}」の選択肢の番号が重なっています。");
                foreach (var choice in item.Choices.Where(c => ArgumentSplitter.Split(c.Value) is null))
                    Add($"「{name}」の選択肢「{choice.Label}」の送る値の引用符（\"）が閉じていません。");
                if (!item.Choices.Any(c => c.Id == item.InitialChoiceId)) Add($"「{name}」の初期の選択がありません。");
                break;
        }
    }
}
