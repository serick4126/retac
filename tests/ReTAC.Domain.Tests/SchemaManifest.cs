using System.Collections;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ReTAC.Domain.Tests;

/// <summary>
/// Phase 5: 実装からスキーマ・マニフェストを組み立てる。
/// 出力先は schema/manifest.json。手で編集しない（再生成で消える）。
/// 検証と更新は <see cref="SchemaManifestTests"/> から行う。
/// </summary>
public static class SchemaManifest
{
    /// <summary>仕様 ID（R-04 / R-55-3 / F-09 など）。UTF-8 のような語中の一致は \b で避ける。</summary>
    private static readonly Regex SpecIdPattern = new(@"\b[A-Z]-\d+(?:-\d+)?\b", RegexOptions.Compiled);

    private static readonly Regex DeclarationPattern = new(
        @"^\s*(?:public|internal)\s+(?:(?:sealed|abstract|static|partial|readonly|record)\s+)*(?:class|record|struct|enum|interface)\s+(\w+)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // ---------------------------------------------------------------------

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ReTAC.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("ReTAC.slnx が見つからない");
    }

    public static JsonObject Build(string repoRoot)
    {
        var sources = ScanSources(repoRoot);
        var docs = LoadXmlDocs();
        var domain = typeof(Entries.Entry).Assembly;
        var types = domain.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();

        return new JsonObject
        {
            ["$schemaVersion"] = 1,
            ["generatedFrom"] = new JsonObject
            {
                ["note"] = "自動生成。手で編集しない。RETAC_SCHEMA_UPDATE=1 dotnet test で更新する",
                ["assemblies"] = new JsonArray("ReTAC.Domain", "ReTAC.App"),
            },
            ["settings"] = BuildSettings(docs),
            ["types"] = BuildTypes(types, sources, docs),
            // 設定は外部契約なので、Domain 型と同じ網に掛ける
            ["edges"] = BuildEdges([.. types, typeof(App.AppSettings)], docs),
            ["wiring"] = BuildWiring(),
            ["specIndex"] = BuildSpecIndex(sources),
        };
    }

    // --- 型インベントリ ---------------------------------------------------

    private static JsonObject BuildTypes(List<Type> types, SourceScan sources, XmlDocs docs)
    {
        var result = new JsonObject();
        foreach (var type in types)
        {
            var entry = new JsonObject
            {
                ["id"] = TypeId(type),
                ["kind"] = KindOf(type),
                ["file"] = sources.FileOf(type.Name),
            };
            if (docs.ForType(type) is { } doc) entry["doc"] = doc;

            if (type.IsEnum)
            {
                entry["members"] = new JsonArray([.. Enum.GetNames(type).Select(n => (JsonNode)n)]);
            }
            else
            {
                var fields = new JsonArray();
                foreach (var p in PublicProperties(type)) fields.Add(DescribeProperty(p, docs));
                if (fields.Count > 0) entry["fields"] = fields;
            }
            result[type.FullName!] = entry;
        }
        return result;
    }

    private static JsonObject DescribeProperty(PropertyInfo p, XmlDocs docs)
    {
        var node = new JsonObject
        {
            ["name"] = p.Name,
            ["type"] = Name(p.PropertyType),
            ["nullable"] = IsNullable(p),
            ["required"] = p.GetCustomAttribute<RequiredMemberAttribute>() is not null,
            ["mutability"] = Mutability(p),
        };
        if (docs.ForProperty(p) is { } doc) node["doc"] = doc;
        return node;
    }

    private static string Mutability(PropertyInfo p)
    {
        var setter = p.SetMethod;
        if (setter is null || !setter.IsPublic) return "get-only";
        return setter.ReturnParameter.GetRequiredCustomModifiers()
            .Any(m => m.Name == "IsExternalInit") ? "init" : "set";
    }

    // --- 参照エッジ -------------------------------------------------------

    private static JsonArray BuildEdges(List<Type> types, XmlDocs docs)
    {
        var edges = new JsonArray();
        foreach (var type in types.Where(t => !t.IsEnum))
        {
            foreach (var (member, memberType, isPublic) in DataMembers(type))
            {
                var element = ElementOf(memberType);
                var target = element ?? memberType;
                if (!IsDomainType(target) || target == type) continue;

                var edge = new JsonObject
                {
                    ["from"] = TypeId(type),
                    ["to"] = TypeId(target),
                    ["type"] = element is not null ? "1:N" : "1:1",
                    ["fk"] = new JsonObject
                    {
                        ["field"] = member,
                        ["container"] = element is not null ? Name(memberType) : null,
                        ["unique"] = element is not null ? EnforcesUniqueness(memberType) : (JsonNode?)null,
                    },
                    ["visibility"] = isPublic ? "public" : "private",
                };
                if (element is null && IsNullableType(memberType)) edge["type"] = "0..1";
                edges.Add(edge);
            }

            foreach (var reference in ValueReferences(type, docs)) edges.Add(reference);
        }
        return edges;
    }

    /// <summary>
    /// L12 の候補。`XxxId` という名前のプリミティブは、型では守られない参照（外部キー相当）とみなす。
    /// 自分自身の識別子（`Id`）は除く。
    /// </summary>
    private static IEnumerable<JsonObject> ValueReferences(Type type, XmlDocs docs)
    {
        foreach (var (member, memberType, _) in DataMembers(type))
        {
            if (member is "Id" || !member.EndsWith("Id", StringComparison.Ordinal)) continue;
            if (memberType != typeof(int) && memberType != typeof(long) && memberType != typeof(string)) continue;

            yield return new JsonObject
            {
                ["from"] = TypeId(type),
                ["to"] = docs.CrefOf(type, member) ?? "UNRESOLVED",
                ["type"] = "N:1",
                ["fk"] = new JsonObject { ["field"] = member, ["byValue"] = true },
                ["onDelete"] = "UNENFORCED",
                ["candidateId"] = FkCandidateId(type, member),
            };
        }
    }

    private static bool EnforcesUniqueness(Type container)
    {
        if (!container.IsGenericType) return false;
        var name = container.GetGenericTypeDefinition().Name;
        return name.StartsWith("HashSet", StringComparison.Ordinal)
            || name.StartsWith("IReadOnlySet", StringComparison.Ordinal)
            || name.StartsWith("ISet", StringComparison.Ordinal)
            || name.Contains("Dictionary", StringComparison.Ordinal);
    }

    /// <summary>公開プロパティと、状態を持つ private フィールド。後者を見ないと List の保持を取り逃がす。</summary>
    private static IEnumerable<(string Member, Type Type, bool IsPublic)> DataMembers(Type type)
    {
        foreach (var p in PublicProperties(type))
            yield return (p.Name, p.PropertyType, true);

        foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                     .Where(f => !f.Name.Contains('<'))
                     .OrderBy(f => f.Name, StringComparer.Ordinal))
            yield return (f.Name, f.FieldType, false);
    }

    private static IEnumerable<PropertyInfo> PublicProperties(Type type) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(p => p.GetIndexParameters().Length == 0 && p.Name != "EqualityContract")
            .OrderBy(p => p.MetadataToken);

    /// <summary>コレクションの要素型。辞書は値側を見る。</summary>
    public static Type? ElementOf(Type type)
    {
        if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type)) return null;
        if (type.IsArray) return type.GetElementType();
        if (!type.IsGenericType) return null;
        var args = type.GetGenericArguments();
        return args.Length == 0 ? null : args[^1];
    }

    public static bool IsDomainType(Type type) => type.Assembly == typeof(Entries.Entry).Assembly;

    // --- 設定ファイル契約 -------------------------------------------------

    private static JsonObject BuildSettings(XmlDocs docs)
    {
        var settingsType = typeof(App.AppSettings);
        var defaults = new App.AppSettings();
        var keys = new JsonArray();

        foreach (var p in settingsType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(p => p.CanWrite).OrderBy(p => p.MetadataToken))
        {
            var key = new JsonObject
            {
                ["key"] = p.Name,
                ["type"] = Name(p.PropertyType),
                ["nullable"] = IsNullable(p),
                ["default"] = Literal(p.GetValue(defaults)),
            };
            if (docs.ForProperty(p) is { } doc) key["doc"] = doc;
            keys.Add(key);
        }

        return new JsonObject
        {
            ["file"] = App.AppSettings.FileName,
            ["sourceType"] = TypeId(settingsType),
            ["paths"] = new JsonObject
            {
                ["primary"] = "AppContext.BaseDirectory",
                ["fallback"] = "%APPDATA%\\ReTAC\\",
                ["specId"] = "R-55-3",
            },
            ["versioning"] = new JsonObject
            {
                ["hasVersionField"] = settingsType.GetProperty("Version") is not null,
                ["migration"] = "forbidden",
                ["reason"] = "一般リリース前のため旧設定からの移行・互換は考慮しない",
            },
            ["onLoadFailure"] = "silent-fallback-to-defaults",
            ["writeStrategy"] = new JsonObject { ["mode"] = "atomic-temp-rename", ["specId"] = "V-07" },
            ["keys"] = keys,
        };
    }

    /// <summary>既定値を JSON へ。中身まで写すと差分が暴れるので、コレクションは件数だけにする。</summary>
    private static JsonNode? Literal(object? value) => value switch
    {
        null => null,
        string s => s,
        bool b => b,
        int i => i,
        float f => f,
        Enum e => e.ToString(),
        IDictionary d => $"{{}} ({d.Count} 件)",
        IEnumerable list => $"[] ({list.Cast<object>().Count()} 件)",
        _ => value.ToString(),
    };

    // --- コマンド配線 -----------------------------------------------------

    private static JsonObject BuildWiring()
    {
        var labels = App.CommandLabels.Grouped
            .ToDictionary(r => r.Command, r => (r.Category, r.Label));
        var defaults = Keys.DefaultKeyMap.Create();

        var commands = new JsonArray();
        foreach (var id in Enum.GetValues<Commands.CommandId>().OrderBy(c => c.ToString(), StringComparer.Ordinal))
        {
            var known = labels.TryGetValue(id, out var row);
            commands.Add(new JsonObject
            {
                ["id"] = id.ToString(),
                ["hex"] = "0x" + ((int)id).ToString("X4"),
                ["label"] = known ? row.Label : null,
                ["category"] = known ? row.Category : null,
                ["assignable"] = known,
                ["defaultKeys"] = new JsonArray([.. defaults.Bindings
                    .Where(b => b.Value is Commands.BuiltinTarget builtin && builtin.Command == id)
                    .Select(b => (JsonNode)App.KeySlots.Label(b.Key))
                    .OrderBy(n => n!.GetValue<string>(), StringComparer.Ordinal)]),
            });
        }

        var slots = new JsonArray();
        foreach (var slot in App.KeySlots.All)
            slots.Add(new JsonObject
            {
                ["label"] = App.KeySlots.Label(slot),
                ["vk"] = slot.VirtualKey,
                ["shift"] = slot.Shift,
                ["ctrl"] = slot.Ctrl,
                ["defaultTarget"] = defaults.Resolve(slot)?.Serialize(),
            });

        var themes = new JsonArray();
        foreach (var slot in App.ThemeSlots.All)
            themes.Add(new JsonObject { ["key"] = slot.Key, ["label"] = slot.Label });

        // パスと引数まで写す。ここが変わったことを差分で見えるようにするため（B-05 の判断が絡む）
        var tools = new JsonArray();
        foreach (var tool in Tools.DefaultExternalTools.Create())
            tools.Add(new JsonObject
            {
                ["id"] = tool.Id,
                ["name"] = tool.Name,
                ["path"] = tool.Path,
                ["arguments"] = tool.Arguments,
            });

        return new JsonObject
        {
            ["commands"] = commands,
            ["keySlots"] = slots,
            ["themeSlots"] = themes,
            ["defaultTools"] = tools,
            ["firstFreeToolId"] = Tools.DefaultExternalTools.FirstFreeId,
        };
    }

    // --- 仕様 ID 索引 -----------------------------------------------------

    private static JsonObject BuildSpecIndex(SourceScan sources)
    {
        var index = new JsonObject();
        foreach (var (id, places) in sources.SpecIds.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var array = new JsonArray();
            foreach (var (file, line) in places)
                array.Add(new JsonObject { ["file"] = file, ["line"] = line });
            index[id] = array;
        }
        return index;
    }

    // --- ソース走査 -------------------------------------------------------

    public sealed class SourceScan
    {
        public Dictionary<string, string> Declarations { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<(string File, int Line)>> SpecIds { get; } = new(StringComparer.Ordinal);

        public JsonNode? FileOf(string typeName) =>
            Declarations.TryGetValue(typeName, out var file) ? file : null;
    }

    public static SourceScan ScanSources(string repoRoot)
    {
        var scan = new SourceScan();
        var src = Path.Combine(repoRoot, "src");
        foreach (var path in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
                     .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                              && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
            var text = File.ReadAllText(path);

            foreach (Match m in DeclarationPattern.Matches(text))
                scan.Declarations.TryAdd(m.Groups[1].Value, relative);

            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
                foreach (Match m in SpecIdPattern.Matches(lines[i]))
                {
                    if (!scan.SpecIds.TryGetValue(m.Value, out var places))
                        scan.SpecIds[m.Value] = places = [];
                    places.Add((relative, i + 1));
                }
        }
        return scan;
    }

    // --- XML ドキュメント -------------------------------------------------

    public sealed class XmlDocs
    {
        private readonly Dictionary<string, XElement> _members = new(StringComparer.Ordinal);

        public XmlDocs(IEnumerable<string> paths)
        {
            foreach (var path in paths.Where(File.Exists))
                foreach (var member in XDocument.Load(path).Descendants("member"))
                    _members.TryAdd(member.Attribute("name")?.Value ?? "", member);
        }

        public string? ForType(Type type) => Summary("T:" + type.FullName);

        public string? ForProperty(PropertyInfo p) =>
            Summary($"P:{p.DeclaringType!.FullName}.{p.Name}")
            ?? ParamOf($"T:{p.DeclaringType!.FullName}", p.Name);

        /// <summary>`XxxId` が指す先。record の param に書かれた cref を参照先として読む。</summary>
        public string? CrefOf(Type type, string member)
        {
            var param = _members.TryGetValue("T:" + type.FullName, out var t)
                ? t.Elements("param").FirstOrDefault(e => e.Attribute("name")?.Value == member)
                : null;
            var cref = (param ?? (_members.TryGetValue($"P:{type.FullName}.{member}", out var p) ? p : null))
                ?.Descendants("see").Select(s => s.Attribute("cref")?.Value).FirstOrDefault(v => v is not null);
            if (cref is null) return null;

            // "P:ReTAC.Domain.Tools.ExternalTool.Id" → "type:ReTAC.Domain.Tools.ExternalTool"
            var name = cref[2..];
            var dot = name.LastIndexOf('.');
            return cref.StartsWith("T:", StringComparison.Ordinal) || dot < 0
                ? "type:" + name
                : "type:" + name[..dot];
        }

        private string? ParamOf(string key, string param) =>
            _members.TryGetValue(key, out var m)
                ? Clean(m.Elements("param").FirstOrDefault(e => e.Attribute("name")?.Value == param)?.Value)
                : null;

        private string? Summary(string key) =>
            _members.TryGetValue(key, out var m) ? Clean(m.Element("summary")?.Value) : null;

        private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text)
            ? null
            : Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static XmlDocs LoadXmlDocs() => new(
        new[] { "ReTAC.Domain", "ReTAC.Shell", "ReTAC.App" }
            .Select(n => Path.Combine(AppContext.BaseDirectory, n + ".xml")));

    // --- 共通 -------------------------------------------------------------

    public static string TypeId(Type type) => "type:" + type.FullName;

    public static string FkCandidateId(Type type, string member) => $"fk:{type.FullName}#{member}";

    public static string UniqueCandidateId(Type type, string member) => $"unique:{type.FullName}#{member}";

    private static string KindOf(Type type) =>
        type.IsEnum ? "enum"
        : type.IsInterface ? "interface"
        : type.IsValueType ? (IsRecord(type) ? "record struct" : "struct")
        : type.IsAbstract && type.IsSealed ? "static class"
        : IsRecord(type) ? (type.IsAbstract ? "abstract record" : type.IsSealed ? "sealed record" : "record")
        : type.IsAbstract ? "abstract class"
        : type.IsSealed ? "sealed class" : "class";

    private static bool IsRecord(Type type) =>
        type.GetMethod("<Clone>$", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null
        || type.GetProperty("EqualityContract", BindingFlags.Instance | BindingFlags.NonPublic) is not null;

    private static bool IsNullable(PropertyInfo p) =>
        IsNullableType(p.PropertyType)
        || new NullabilityInfoContext().Create(p).ReadState == NullabilityState.Nullable;

    private static bool IsNullableType(Type type) => Nullable.GetUnderlyingType(type) is not null;

    public static string Name(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } inner) return Name(inner) + "?";
        if (type.IsArray) return Name(type.GetElementType()!) + "[]";
        if (!type.IsGenericType) return Alias(type);
        var raw = type.Name[..type.Name.IndexOf('`')];
        return $"{raw}<{string.Join(", ", type.GetGenericArguments().Select(Name))}>";
    }

    private static string Alias(Type type) => type.FullName switch
    {
        "System.String" => "string",
        "System.Boolean" => "bool",
        "System.Int32" => "int",
        "System.Int64" => "long",
        "System.UInt16" => "ushort",
        "System.Single" => "float",
        "System.Double" => "double",
        _ => type.FullName ?? type.Name,
    };
}
