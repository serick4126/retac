using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

/// <summary>R-130 / R-133: 入力ダイアログの定義の検査</summary>
public class PromptDefinitionRulesTests
{
    private static PromptItem Text(int id, string label = "名前") => new() { Id = id, Kind = PromptItemKind.Text, Label = label };

    private static PromptDefinition With(params PromptItem[] items) => new()
    {
        Items = [.. items],
        Arguments = [.. items.Select(i => PromptArgument.Item(i.Id))],
    };

    private static string[] Messages(PromptDefinition definition) =>
        [.. PromptDefinitionRules.Validate(definition).Select(p => p.Message)];

    [Fact]
    public void 定義の無いプロンプトの代わりは誤りが無い()
    {
        var simple = PromptDefinition.Simple("検索");
        Assert.Empty(PromptDefinitionRules.Validate(simple));
        Assert.Equal("検索", simple.Title);
        var item = Assert.Single(simple.Items);
        Assert.Equal((1, PromptItemKind.Text, "検索"), (item.Id, item.Kind, item.Label));
        Assert.Equal(1, Assert.Single(simple.Arguments).ItemId);
    }

    [Fact]
    public void 空の定義は誤りが無い()
    {
        Assert.Empty(PromptDefinitionRules.Validate(new PromptDefinition()));
    }

    [Fact]
    public void 引数に入っていない項目は誤り()
    {
        var definition = new PromptDefinition { Items = [Text(1, "展開先")] };
        var problem = Assert.Single(PromptDefinitionRules.Validate(definition));
        Assert.Equal("「展開先」が引数に入っていません。", problem.Message);
        Assert.Equal(1, problem.ItemId);
    }

    [Fact]
    public void 同じ項目を二つの行に入れてよい()
    {
        var definition = With(Text(1)) with { Arguments = [PromptArgument.Item(1), PromptArgument.Item(1)] };
        Assert.Empty(PromptDefinitionRules.Validate(definition));
    }

    [Fact]
    public void 項目は十個まで()
    {
        var eleven = With([.. Enumerable.Range(1, 11).Select(i => Text(i, $"項目{i}"))]);
        Assert.Contains("項目は 10 個までです。", Messages(eleven));
        Assert.Empty(PromptDefinitionRules.Validate(With([.. Enumerable.Range(1, 10).Select(i => Text(i, $"項目{i}"))])));
    }

    [Fact]
    public void ラベルが空白だけの項目は誤り()
    {
        Assert.Contains("ラベルが空の項目があります。", Messages(With(Text(1, " \t"))));
    }

    [Fact]
    public void 項目の番号の重複は誤り()
    {
        Assert.Contains("項目の番号が重なっています。", Messages(With(Text(1, "a"), Text(1, "b"))));
    }

    [Fact]
    public void 無い項目を指す行は誤り()
    {
        var definition = With(Text(1)) with { Arguments = [PromptArgument.Item(1), PromptArgument.Item(9)] };
        var problem = Assert.Single(PromptDefinitionRules.Validate(definition));
        Assert.Equal("引数の 2 行目が指す項目がありません。", problem.Message);
        Assert.Equal(1, problem.ArgumentIndex);
    }

    [Theory]
    [InlineData("\"-o", "", "「名前」の「前に付ける」の引用符（\"）が閉じていません。")]
    [InlineData("", "\"x", "「名前」の「後ろに付ける」の引用符（\"）が閉じていません。")]
    public void 付ける文字の引用符が閉じていなければ誤り(string prefix, string suffix, string message)
    {
        Assert.Contains(message, Messages(With(Text(1) with { Prefix = prefix, Suffix = suffix })));
    }

    [Fact]
    public void チェックボックスの送る値が空か引用符が閉じていなければ誤り()
    {
        var empty = new PromptItem { Id = 1, Kind = PromptItemKind.CheckBox, Label = "上書き", Value = " " };
        Assert.Contains("「上書き」の送る値が空です。", Messages(With(empty)));
        Assert.Contains("「上書き」の送る値の引用符（\"）が閉じていません。", Messages(With(empty with { Value = "\"-y" })));
        Assert.Empty(PromptDefinitionRules.Validate(With(empty with { Value = "-y" })));
    }

    private static PromptItem DropDown(params PromptChoice[] choices) => new()
    {
        Id = 1, Kind = PromptItemKind.DropDown, Label = "画質", Choices = [.. choices], InitialChoiceId = choices.FirstOrDefault()?.Id ?? 0,
    };

    private static PromptChoice Choice(int id, string label, string value = "") => new() { Id = id, Label = label, Value = value };

    [Fact]
    public void ドロップダウンリストの誤り()
    {
        Assert.Contains("「画質」に選択肢がありません。", Messages(With(DropDown())));
        Assert.Contains("「画質」に表示名が空の選択肢があります。", Messages(With(DropDown(Choice(1, "")))));
        Assert.Contains("「画質」の選択肢の表示名「標準」が重なっています。", Messages(With(DropDown(Choice(1, "標準", "a"), Choice(2, "標準", "b")))));
        Assert.Contains("「画質」の選択肢の番号が重なっています。", Messages(With(DropDown(Choice(1, "a"), Choice(1, "b")))));
        Assert.Contains("「画質」の選択肢「a」の送る値の引用符（\"）が閉じていません。", Messages(With(DropDown(Choice(1, "a", "\"x")))));
        Assert.Contains("「画質」の初期の選択がありません。", Messages(With(DropDown(Choice(1, "a")) with { InitialChoiceId = 5 })));
        Assert.Empty(PromptDefinitionRules.Validate(With(DropDown(Choice(1, "指定しない"), Choice(2, "高速", "-preset fast")))));
    }

    [Fact]
    public void マクロを含む引数の行は引数欄と同じ検査をしプロンプトは書けない()
    {
        var definition = new PromptDefinition { Arguments = [PromptArgument.Template("${nope}"), PromptArgument.Template("${prompt}")] };
        var problems = PromptDefinitionRules.Validate(definition);
        Assert.Contains(problems, p => p.ArgumentIndex == 0 && p.Message.StartsWith("引数の 1 行目: ", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.ArgumentIndex == 1 && p.Message == "引数の 2 行目に ${prompt} は書けません。");
    }

    [Fact]
    public void 固定の引数は何を書いても誤りにしない()
    {
        var definition = new PromptDefinition { Arguments = [PromptArgument.Fixed(""), PromptArgument.Fixed("\"x"), PromptArgument.Fixed("${nope}")] };
        Assert.Empty(PromptDefinitionRules.Validate(definition));
    }

    [Fact]
    public void 次の番号は最大の番号の次()
    {
        Assert.Equal(1, new PromptDefinition().NextItemId());
        Assert.Equal(8, With(Text(3), Text(7)).NextItemId());
    }

    [Fact]
    public void 未定義の種類は誤り()
    {
        // JsonStringEnumConverter は数値も読むので、手で直した設定ファイルに 999 が入りうる
        var item = Text(1) with { Kind = (PromptItemKind)999 };
        Assert.Contains("「名前」の種類が正しくありません。", Messages(With(item)));
        var definition = new PromptDefinition { Arguments = [new PromptArgument { Kind = (PromptArgumentKind)999 }] };
        var problem = Assert.Single(PromptDefinitionRules.Validate(definition));
        Assert.Equal(("引数の 1 行目の種類が正しくありません。", 0), (problem.Message, problem.ArgumentIndex!.Value));
    }

    private static readonly PromptItem Quality = new()
    {
        Id = 1, Kind = PromptItemKind.DropDown, Label = "画質", InitialChoiceId = 1, LastChoiceId = 2,
        Choices = [new PromptChoice { Id = 1, Label = "高速", Value = "fast" }, new PromptChoice { Id = 2, Label = "標準", Value = "medium" }],
    };

    [Fact]
    public void 選択肢の行から番号と初期の選択を決める()
    {
        var item = PromptChoiceRows.Apply(Quality, [new(1, "速い", "fast", false), new(2, "標準", "medium", true), new(null, "高画質", "slow", false)]);
        Assert.Equal([1, 2, 3], item.Choices.Select(c => c.Id));
        Assert.Equal("速い", item.Choices[0].Label);
        Assert.Equal(2, item.InitialChoiceId);
        Assert.Equal(2, item.LastChoiceId);            // 表示名を直しても前回の選択は保つ
    }

    [Fact]
    public void 初期の選択が無ければ先頭()
    {
        var item = PromptChoiceRows.Apply(Quality, [new(2, "標準", "medium", false), new(1, "高速", "fast", false)]);
        Assert.Equal(2, item.InitialChoiceId);
    }

    [Fact]
    public void 消した選択肢の番号を使い回さない()
    {
        // 前回選んだ 2 を消し、新しい選択肢を足す。新しい選択肢に 2 を付けると、前回の値が別の選択肢を指してしまう
        var item = PromptChoiceRows.Apply(Quality, [new(1, "高速", "fast", true), new(null, "高画質", "slow", false)]);
        Assert.Equal([1, 3], item.Choices.Select(c => c.Id));
        Assert.Null(item.LastChoiceId);                 // 消した選択肢を指す前回の値は消す（次は初期の選択で開く）
        Assert.Equal(1, item.InitialChoiceId);
    }

    [Fact]
    public void 編集前に無い選択肢を指す前回の値は新しい選択肢に結び付けない()
    {
        // 選択肢は 1 だけ、前回の値は 2（手で直した設定ファイル・以前の編集で消えた）。新しい選択肢に 2 が付いても、前回の値は消す
        var source = Quality with { Choices = [Quality.Choices[0]], LastChoiceId = 2 };
        var item = PromptChoiceRows.Apply(source, [new(1, "高速", "fast", true), new(null, "高画質", "slow", false)]);
        Assert.Equal([1, 2], item.Choices.Select(c => c.Id));
        Assert.Null(item.LastChoiceId);
    }
}
