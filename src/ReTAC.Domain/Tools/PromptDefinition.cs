namespace ReTAC.Domain.Tools;

/// <summary>R-130: 入力ダイアログの項目の種類。作った後には変えない（前回の値の意味が変わるため）。</summary>
public enum PromptItemKind
{
    Text,
    Folder,
    File,
    CheckBox,
    DropDown,
}

/// <summary>R-130: 引数の行の種類。</summary>
public enum PromptArgumentKind
{
    /// <summary>値そのもの。構文を通さず 1 つの引数として送る（インポートした値を変えずに持つため。R-134）</summary>
    Fixed,
    /// <summary>引数欄と同じ書き方の文字列。マクロを書ける（<c>${prompt}</c> を除く）</summary>
    Template,
    /// <summary>項目の値</summary>
    Item,
}

/// <summary>ドロップダウンリストの選択肢。初期の選択と前回の値は <see cref="Id"/> で指す（表示名を直しても選択を保つ。R-132）。</summary>
public sealed record PromptChoice
{
    public int Id { get; init; }
    /// <summary>表示名。項目の中で重ねない</summary>
    public string Label { get; init; } = "";
    /// <summary>送る値。引数欄と同じ規則で区切る。空なら何も送らない</summary>
    public string Value { get; init; } = "";
}

/// <summary>
/// 入力ダイアログの 1 項目（R-130）。種類ごとに使う欄が違い、使わない欄は既定値のまま。
/// 設定 JSON にそのまま載るので <c>required</c> を付けない（S-14）。
/// </summary>
public sealed record PromptItem
{
    public int Id { get; init; }
    public PromptItemKind Kind { get; init; }
    public string Label { get; init; } = "";
    /// <summary>テキスト・フォルダ・ファイルの初期値。フォルダの空はカレントフォルダの意味</summary>
    public string Initial { get; init; } = "";
    /// <summary>テキスト・フォルダ・ファイルの「前に付ける」。空白で終わっていなければ値とつながる</summary>
    public string Prefix { get; init; } = "";
    /// <summary>テキスト・フォルダ・ファイルの「後ろに付ける」。空白で始まっていなければ値とつながる</summary>
    public string Suffix { get; init; } = "";
    /// <summary>テキスト・ファイルの「入力を必須にする」</summary>
    public bool Required { get; init; }
    /// <summary>チェックボックスの送る値</summary>
    public string Value { get; init; } = "";
    public bool InitialChecked { get; init; }
    public List<PromptChoice> Choices { get; init; } = [];
    /// <summary>初期の選択。指す先は <see cref="PromptChoice.Id"/></summary>
    public int InitialChoiceId { get; init; }
    /// <summary>R-132: 前回 OK したときの入力欄の文字。前回の入力を初期値にするツールだけが持つ</summary>
    public string? LastText { get; init; }
    public bool? LastChecked { get; init; }
    public int? LastChoiceId { get; init; }
}

/// <summary>入力ダイアログの引数の 1 行（R-130）。</summary>
public sealed record PromptArgument
{
    public PromptArgumentKind Kind { get; init; }
    /// <summary>固定の引数なら送る値そのもの、マクロを含む引数なら引数欄と同じ書き方の文字列</summary>
    public string Text { get; init; } = "";
    /// <summary>項目の行が指す <see cref="PromptItem.Id"/>。型では守られない（<see cref="PromptDefinitionRules.Validate"/> が守る）</summary>
    public int ItemId { get; init; }

    public static PromptArgument Fixed(string value) => new() { Kind = PromptArgumentKind.Fixed, Text = value };
    public static PromptArgument Template(string text) => new() { Kind = PromptArgumentKind.Template, Text = text };
    public static PromptArgument Item(int id) => new() { Kind = PromptArgumentKind.Item, ItemId = id };
}

/// <summary>
/// 外部ツールの入力ダイアログの定義（R-130）。引数欄の <c>${prompt}</c> の位置に <see cref="Arguments"/> を送る順に展開する。
/// 項目は表示順、引数は送る順で別々に持つ。項目の間に <c>${file}</c> を挟む形（WinRAR の <c>x -o+ ${file} 展開先\</c>）を作るため。
/// 設定 JSON にそのまま載るので <c>required</c> を付けない（S-14）。
/// </summary>
public sealed record PromptDefinition
{
    /// <summary>空ならツールの名前</summary>
    public string Title { get; init; } = "";
    /// <summary>R-132: 前回 OK した入力を次の初期値にする</summary>
    public bool RememberLast { get; init; }
    /// <summary>表示順。10 個まで</summary>
    public List<PromptItem> Items { get; init; } = [];
    /// <summary>送る順</summary>
    public List<PromptArgument> Arguments { get; init; } = [];

    /// <summary>
    /// R-130: 定義の無い <c>${prompt}</c>。ツールの名前をタイトルとラベルにしたテキスト 1 つで、入力をそのまま 1 つの引数として送る。
    /// 定義のある場合と同じ道（ダイアログ・検査・展開）を通すために、定義の形で作る。
    /// </summary>
    public static PromptDefinition Simple(string toolName) => new()
    {
        Title = toolName,
        Items = [new PromptItem { Id = 1, Kind = PromptItemKind.Text, Label = toolName }],
        Arguments = [PromptArgument.Item(1)],
    };

    /// <summary>R-130・R-133: 項目も引数の行も無い定義は、定義なし（<see cref="Simple"/>）と同じに扱う。入口（ヘルパーか引数欄の手書きか）で動きを変えないため</summary>
    public bool IsEmpty => Items.Count == 0 && Arguments.Count == 0;

    public int NextItemId() => Items.Count == 0 ? 1 : Items.Max(i => i.Id) + 1;

    public PromptItem? ItemOf(int id) => Items.FirstOrDefault(i => i.Id == id);

    /// <summary>
    /// 手で直した設定ファイルの null を既定値に戻す。System.Text.Json は非 nullable の欄にも明示された null を入れるので、
    /// ここで直さないと展開や画面で NullReferenceException になる（R-55: 設定は手で直せる）。
    /// </summary>
    public PromptDefinition Normalized() => this with
    {
        Title = Title ?? "",
        Items = [.. (Items ?? []).OfType<PromptItem>().Select(i => i with
        {
            Label = i.Label ?? "",
            Initial = i.Initial ?? "",
            Prefix = i.Prefix ?? "",
            Suffix = i.Suffix ?? "",
            Value = i.Value ?? "",
            Choices = [.. (i.Choices ?? []).OfType<PromptChoice>().Select(c => c with { Label = c.Label ?? "", Value = c.Value ?? "" })],
        })],
        Arguments = [.. (Arguments ?? []).OfType<PromptArgument>().Select(a => a with { Text = a.Text ?? "" })],
    };
}

/// <summary>項目の編集の、選択肢の表の 1 行。<paramref name="Id"/> は足したばかりの行なら null。</summary>
public sealed record PromptChoiceRow(int? Id, string Label, string Value, bool Initial);

/// <summary>R-132: 選択肢の表から、選択肢・初期の選択・前回の選択を決める。</summary>
public static class PromptChoiceRows
{
    /// <summary>
    /// 新しい行の番号は、編集前の選択肢と今の行の番号の最大 + 1。消した選択肢の番号を使い回すと、
    /// 前回の選択（番号で持つ）が別の選択肢を指してしまう。前回の選択が残った選択肢に無ければ消す。
    /// 初期の選択は印の付いた最初の行、無ければ先頭。
    /// </summary>
    public static PromptItem Apply(PromptItem source, IReadOnlyList<PromptChoiceRow> rows)
    {
        var next = source.Choices.Select(c => c.Id).Concat(rows.Select(r => r.Id ?? 0)).DefaultIfEmpty(0).Max() + 1;
        var choices = new List<PromptChoice>();
        var initial = (int?)null;
        foreach (var row in rows)
        {
            var id = row.Id ?? next++;
            choices.Add(new PromptChoice { Id = id, Label = row.Label, Value = row.Value });
            if (row.Initial) initial ??= id;
        }
        return source with
        {
            Choices = choices,
            InitialChoiceId = initial ?? choices.FirstOrDefault()?.Id ?? 0,
            // 前回の値は、編集前にもあり、編集後にも残る選択肢を指すときだけ残す。編集前に既に無い選択肢を指していると、
            // その番号が新しい選択肢に付いたときに結び付いてしまう（前回の選択肢が無ければ初期の選択。R-132）
            LastChoiceId = source.LastChoiceId is { } last && source.Choices.Any(c => c.Id == last) && choices.Any(c => c.Id == last) ? last : null,
        };
    }
}
