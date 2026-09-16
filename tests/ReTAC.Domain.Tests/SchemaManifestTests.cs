using System.Drawing;
using System.Text.Json;
using System.Text.Json.Nodes;
using ReTAC.App;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Keys;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

/// <summary>
/// Phase 5: スキーマ・マニフェストの生成と検証。
///
/// `dotnet test`                       … コミット済みの manifest.json と突き合わせる
/// `RETAC_SCHEMA_UPDATE=1 dotnet test` … 突き合わせず上書きする
///
/// L11 / L12 は「制約が無い箇所」を実装から自動で列挙し、invariants.json での分類を強制する。
/// 分類しないと落ちる。黙って通る経路は用意しない。
/// </summary>
public class SchemaManifestTests
{
    private static readonly string Root = SchemaManifest.RepoRoot();
    private static readonly string ManifestPath = Path.Combine(Root, "schema", "manifest.json");
    private static readonly string InvariantsPath = Path.Combine(Root, "schema", "invariants.json");

    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly string[] Statuses = ["enforced", "accepted", "deferred", "missing"];

    private static JsonObject Manifest => field ??= SchemaManifest.Build(Root);
    private static JsonObject Invariants => field ??= Load(InvariantsPath);

    private static JsonObject Load(string path) =>
        JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    private static IEnumerable<JsonObject> Rules =>
        Invariants["invariants"]!.AsArray().Select(n => n!.AsObject());

    /// <summary>invariants.json の applies に一度でも出てきた対象。分類済みとみなす。</summary>
    private static HashSet<string> Triaged => field ??= [.. Rules
        .SelectMany(r => r["applies"]?.AsArray() ?? [])
        .Select(n => n!.GetValue<string>())];

    // === 生成と突き合わせ =================================================

    [Fact]
    public void マニフェストが実装と一致する()
    {
        var generated = Manifest.ToJsonString(Format).ReplaceLineEndings("\n");

        if (Environment.GetEnvironmentVariable("RETAC_SCHEMA_UPDATE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
            File.WriteAllText(ManifestPath, generated + "\n");
            return;
        }

        Assert.True(File.Exists(ManifestPath),
            $"{ManifestPath} が無い。RETAC_SCHEMA_UPDATE=1 dotnet test で作る");
        Assert.Equal(
            File.ReadAllText(ManifestPath).ReplaceLineEndings("\n").TrimEnd('\n'),
            generated);
    }

    // === L1〜L4: コマンド配線の横断検証 ===================================

    [Fact]
    public void L1_全コマンドが表示名を持つか除外が明示されている()
    {
        var unlisted = Manifest["wiring"]!["commands"]!.AsArray()
            .Select(n => n!.AsObject())
            .Where(c => !c["assignable"]!.GetValue<bool>())
            .Select(c => c["id"]!.GetValue<string>())
            .Where(id => !Triaged.Contains("command:" + id))
            .ToList();

        Assert.True(unlisted.Count == 0,
            $"CommandLabels に無いコマンド: {string.Join(", ", unlisted)}。"
            + "設定画面から触れない。表示名を足すか invariants.json に command:<id> で除外理由を書く");
    }

    [Fact]
    public void L2_既定のキーはすべて割り当て可能な枠に含まれる()
    {
        var outside = DefaultKeyMap.Create().Bindings.Keys
            .Where(k => !KeySlots.All.Contains(k))
            .Select(KeySlots.Display)
            .ToList();

        Assert.True(outside.Count == 0,
            $"枠の外に既定キーがある: {string.Join(", ", outside)}。ユーザーが解除できない");
    }

    [Fact]
    public void L3_既定のツールキーは既定のツールを指す()
    {
        var known = DefaultExternalTools.Create().Select(t => t.Id).ToHashSet();
        var dangling = DefaultKeyMap.Create().Bindings.Values
            .OfType<ToolTarget>()
            .Where(t => !known.Contains(t.ToolId))
            .Select(t => t.Serialize())
            .Distinct()
            .ToList();

        Assert.True(dangling.Count == 0,
            $"存在しないツールを指す既定キー: {string.Join(", ", dangling)}");
    }

    [Fact]
    public void L4_配色のキー空間が設定と画面で一致する()
    {
        foreach (var slot in ThemeSlots.All)
        {
            // 既定と違う色を 1 枠だけ変えたら、その枠だけが JSON に出るはず
            var probe = slot.Get(App.Rendering.Theme.Default) == Color.Magenta ? Color.Lime : Color.Magenta;
            var settings = new AppSettings();
            settings.FromTheme(slot.Set(App.Rendering.Theme.Default, probe));

            Assert.Equal([slot.Key], settings.Colors.Keys);
            Assert.Equal(ThemeSlots.ToHex(probe), settings.Colors[slot.Key]);
            Assert.Equal(probe.ToArgb(), slot.Get(settings.ToTheme()).ToArgb());
        }
    }

    // === L5・L9: 設定ファイル契約 =========================================

    [Fact]
    public void L5_全ての設定項目に編集手段が記録されている()
    {
        var owners = Invariants["settingsOwners"]!.AsObject();
        var missing = Manifest["settings"]!["keys"]!.AsArray()
            .Select(n => n!["key"]!.GetValue<string>())
            .Where(key => !owners.TryGetPropertyValue(key, out var owner)
                       || owner!.GetValue<string>() is "UNKNOWN" or "")
            .ToList();

        Assert.True(missing.Count == 0,
            $"編集手段が不明な設定項目: {string.Join(", ", missing)}。"
            + "invariants.json の settingsOwners に dialog:/runtime:/manual: を書く");
    }

    [Fact]
    public void L9_null許容でない設定項目には既定値がある()
    {
        var missing = Manifest["settings"]!["keys"]!.AsArray()
            .Select(n => n!.AsObject())
            .Where(k => !k["nullable"]!.GetValue<bool>() && k["default"] is null)
            .Select(k => k["key"]!.GetValue<string>())
            .ToList();

        Assert.True(missing.Count == 0,
            $"既定値の無い設定項目: {string.Join(", ", missing)}。古い設定ファイルを読むと壊れる");
    }

    // === L6〜L8: マニフェスト自身の健全性 =================================

    [Fact]
    public void L6_不変条件の参照先がすべて解決する()
    {
        var types = Manifest["types"]!.AsObject().Select(p => "type:" + p.Key).ToHashSet();
        var keys = Manifest["settings"]!["keys"]!.AsArray()
            .Select(n => "settings:" + n!["key"]!.GetValue<string>()).ToHashSet();

        var broken = Rules
            .SelectMany(r => (r["applies"]?.AsArray() ?? []).Select(a => (Rule: r, Target: a!.GetValue<string>())))
            .Where(x => x.Target.StartsWith("type:", StringComparison.Ordinal) && !types.Contains(x.Target)
                     || x.Target.StartsWith("settings:", StringComparison.Ordinal)
                        && x.Target != "settings:*" && !keys.Contains(x.Target))
            .Select(x => $"{x.Rule["id"]} → {x.Target}")
            .ToList();

        Assert.True(broken.Count == 0, $"解決できない参照: {string.Join(", ", broken)}");
    }

    [Fact]
    public void L7_不変条件の仕様IDがコードに残っている()
    {
        var index = Manifest["specIndex"]!.AsObject();
        var orphans = Rules
            .SelectMany(r => (r["specIds"]?.AsArray() ?? [])
                .Select(s => (Rule: r, Id: s!.GetValue<string>())))
            .Where(x => !index.ContainsKey(x.Id))
            .Select(x => $"{x.Rule["id"]} → {x.Id}")
            .ToList();

        Assert.True(orphans.Count == 0, $"コードから消えた仕様 ID: {string.Join(", ", orphans)}");
    }

    [Fact]
    public void L8_エッジに矛盾が無い()
    {
        var edges = Manifest["edges"]!.AsArray().Select(n => n!.AsObject()).ToList();

        // 参照先が書かれていない値参照。invariants.json で分類済みのものは、
        // そこで「参照ではない」と説明が付いているので除く（L12 が分類自体を保証する）。
        var unresolved = edges
            .Where(e => e["to"]!.GetValue<string>() == "UNRESOLVED"
                     && !Triaged.Contains(e["candidateId"]?.GetValue<string>() ?? ""))
            .Select(e => $"{e["from"]}#{e["fk"]!["field"]}")
            .ToList();
        Assert.True(unresolved.Count == 0,
            $"参照先が不明なエッジ: {string.Join(", ", unresolved)}。cref で参照先を書く");

        var duplicated = edges
            .GroupBy(e => $"{e["from"]}|{e["to"]}|{e["fk"]!["field"]}")
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.True(duplicated.Count == 0, $"同じ経路が重複している: {string.Join(", ", duplicated)}");

        // 同じ型ペアを結ぶコレクションが、片方では一意・片方では重複可、という食い違いを許さない。
        // N:N と N:1 の取り違えは、このコードベースではこの形で現れる。
        var conflicting = edges
            .Where(e => e["type"]!.GetValue<string>() == "1:N")
            .GroupBy(e => $"{e["from"]}|{e["to"]}")
            .Where(g => g.Select(e => e["fk"]!["unique"]?.GetValue<bool>()).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.True(conflicting.Count == 0,
            $"同じ型ペアで一意性の扱いが揃っていない: {string.Join(", ", conflicting)}");
    }

    // === L10: 分類そのものの健全性 ========================================

    [Fact]
    public void L10_不変条件の分類が埋まっている()
    {
        var problems = new List<string>();
        foreach (var rule in Rules)
        {
            var id = rule["id"]?.GetValue<string>() ?? "(id なし)";
            var status = rule["status"]?.GetValue<string>();

            if (status is null || !Statuses.Contains(status))
            {
                problems.Add($"{id}: status が {status ?? "未記載"}");
                continue;
            }
            if (status is "accepted" or "deferred" && string.IsNullOrWhiteSpace(rule["reason"]?.GetValue<string>()))
                problems.Add($"{id}: {status} には reason が要る");
            if (status == "deferred" && rule["plannedShape"] is null)
                problems.Add($"{id}: deferred には plannedShape が要る");
            if (status == "enforced" && string.IsNullOrWhiteSpace(rule["enforcedBy"]?.GetValue<string>()))
                problems.Add($"{id}: enforced には enforcedBy が要る");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void L10b_実装漏れが残っていない()
    {
        var missing = Rules
            .Where(r => r["status"]?.GetValue<string>() == "missing")
            .Select(r => $"{r["id"]}: {r["rule"]}")
            .ToList();

        Assert.True(missing.Count == 0,
            "実装漏れが残っている（直すまでこのテストは失敗し続ける）:\n" + string.Join("\n", missing));
    }

    // === L11・L12: 制約の欠落を自動で列挙し、分類を強制する ================

    [Fact]
    public void L11_重複を許すコレクションが分類されている()
    {
        var untriaged = UniquenessCandidates()
            .Where(c => !Triaged.Contains(c.Id))
            .Select(c => $"{c.Id}  ({c.Description})")
            .ToList();

        Assert.True(untriaged.Count == 0,
            "一意性の強制が無いコレクションが未分類:\n" + string.Join("\n", untriaged)
            + "\ninvariants.json の applies に id を書き、status を enforced / accepted / deferred / missing のどれかにする");
    }

    [Fact]
    public void L12_型で守られない参照が分類されている()
    {
        var untriaged = Manifest["edges"]!.AsArray()
            .Select(n => n!.AsObject())
            .Where(e => e["candidateId"] is not null)
            .Select(e => e["candidateId"]!.GetValue<string>())
            .Where(id => !Triaged.Contains(id))
            .ToList();

        Assert.True(untriaged.Count == 0,
            "参照整合性の強制が無い値参照が未分類:\n" + string.Join("\n", untriaged));
    }

    /// <summary>
    /// 一意性の強制が無いコレクション。record を List / 配列で持つ箇所を候補とする。
    /// HashSet や Dictionary は型が一意性を保証するので候補にしない。
    /// 同じ関係を private の実体と public の読み取り専用ビューで二重に持つ型があるので、
    /// 関係ごとに 1 件へまとめる（実体側を代表にする）。
    /// </summary>
    private static IEnumerable<(string Id, string Description)> UniquenessCandidates() =>
        Manifest["edges"]!.AsArray()
            .Select(n => n!.AsObject())
            .Where(e => e["type"]!.GetValue<string>() == "1:N"
                     && e["fk"]!["unique"]?.GetValue<bool>() == false)
            .GroupBy(e => $"{e["from"]}|{e["to"]}")
            .Select(g => g.OrderBy(e => e["visibility"]!.GetValue<string>() == "private" ? 0 : 1).First())
            .Select(e => (
                Id: $"unique:{e["from"]!.GetValue<string>()["type:".Length..]}#{e["fk"]!["field"]}",
                Description: $"{e["fk"]!["container"]} → {e["to"]}"));
}
