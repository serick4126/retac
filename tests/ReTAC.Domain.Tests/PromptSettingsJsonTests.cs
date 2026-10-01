using System.Text.Json;
using ReTAC.App;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

/// <summary>R-130: 定義は外部ツールの設定に入る。手で直した null でも起動を止めない</summary>
public class PromptSettingsJsonTests
{
    [Fact]
    public void 定義は設定ファイルを通っても同じ内容で戻る()
    {
        var definition = new PromptDefinition
        {
            Title = "展開",
            RememberLast = true,
            Items =
            [
                new PromptItem { Id = 1, Kind = PromptItemKind.Folder, Label = "展開先", Suffix = @"\", LastText = @"E:\a" },
                new PromptItem { Id = 2, Kind = PromptItemKind.CheckBox, Label = "上書き", Value = "-o+", InitialChecked = true, LastChecked = false },
                new PromptItem
                {
                    Id = 3, Kind = PromptItemKind.DropDown, Label = "画質", InitialChoiceId = 2, LastChoiceId = 1,
                    Choices = [new PromptChoice { Id = 1, Label = "指定しない" }, new PromptChoice { Id = 2, Label = "高速", Value = "-preset fast" }],
                },
            ],
            Arguments = [PromptArgument.Fixed("x"), PromptArgument.Item(2), PromptArgument.Template("${file}"), PromptArgument.Item(1), PromptArgument.Item(3)],
        };
        var settings = new AppSettings { ExternalTools = [new ExternalTool { Id = 5, Name = "展開", Path = "WinRAR.exe", Arguments = "${prompt}", Prompt = definition }] };
        settings.Normalize();   // 読み込み側と同じ正規化を通してから比べる（ほかの設定の既定値の直しで差が出ないように）

        var json = JsonSerializer.Serialize(settings, AppSettings.Json);
        var loaded = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.Json)!;
        loaded.Normalize();

        Assert.Equal(json, JsonSerializer.Serialize(loaded, AppSettings.Json));
        Assert.DoesNotContain("IsEmpty", json);   // 計算だけのメンバーを設定ファイルに書かない（R-55）
        Assert.Contains("\"Kind\": \"CheckBox\"", json);   // 列挙は名前で書く（人が読んで直せる。R-55）
    }

    [Fact]
    public void 定義の無いツールはnullのまま()
    {
        var settings = new AppSettings();
        settings.Normalize();
        Assert.All(settings.ExternalTools, t => Assert.Null(t.Prompt));
    }

    [Fact]
    public void nullの欄を既定値に戻す()
    {
        const string json = """
            {
              "ExternalTools": [
                { "Id": 5, "Name": "x", "Path": "p", "Arguments": "${prompt}",
                  "Prompt": { "Title": null, "Items": [ null, { "Id": 1, "Kind": "DropDown", "Label": null, "Initial": null, "Prefix": null, "Suffix": null, "Value": null, "Choices": [ null, { "Id": 1, "Label": null, "Value": null } ] } ],
                              "Arguments": [ null, { "Kind": "Fixed", "Text": null } ] } }
              ]
            }
            """;
        var loaded = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.Json)!;
        loaded.Normalize();

        var prompt = Assert.Single(loaded.ExternalTools).Prompt!;
        Assert.Equal("", prompt.Title);
        var item = Assert.Single(prompt.Items);
        Assert.Equal(("", "", "", "", ""), (item.Label, item.Initial, item.Prefix, item.Suffix, item.Value));
        var choice = Assert.Single(item.Choices);
        Assert.Equal(("", ""), (choice.Label, choice.Value));
        Assert.Equal("", Assert.Single(prompt.Arguments).Text);
    }

    [Fact]
    public void 定義の中のリストがnullでも空として読む()
    {
        const string json = """{ "ExternalTools": [ { "Id": 5, "Name": "x", "Prompt": { "Items": null, "Arguments": null } } ] }""";
        var loaded = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.Json)!;
        loaded.Normalize();
        var prompt = Assert.Single(loaded.ExternalTools).Prompt!;
        Assert.Empty(prompt.Items);
        Assert.Empty(prompt.Arguments);
    }
}
