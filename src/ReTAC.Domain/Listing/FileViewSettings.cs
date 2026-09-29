using System.Collections;
using System.Reflection;
using System.Text.Json.Serialization;

namespace ReTAC.Domain.Listing;

/// <summary>R-113: 名前の列（小アイコンでは項目の幅）の決め方。</summary>
public enum NameWidthMode { ShowAll, Auto, MaxChars }

/// <summary>R-116: マークのチェックボックスをいつ出すか。</summary>
public enum CheckBoxMode { HoverAndMarked, Always }

/// <summary>R-114: 詳細表示の名前以外の列。名前の列は常に先頭に出し、隠せないので含めない。</summary>
public enum DetailsColumn { Extension, Size, Modified, Created, Type, Attributes }

/// <summary>R-115: 並べて表示・コンテンツで名前の横に出す情報。並びは入りきらないときに出す順（候補の並び）。</summary>
public enum TileInfo { Type, Size, Modified, Created, Attributes }

/// <summary>R-113 / R-112-3 / R-115 / R-119: 範囲と候補。設定画面の部品もここから作る。</summary>
public static class FileViewLimits
{
    public const int MinChars = 10, MaxChars = 260, DefaultChars = 40;
    public const int MinNameLines = 1, MaxNameLines = 3, DefaultNameLines = 2;
    public static readonly IReadOnlyList<int> IconSizes = [32, 48, 64, 96, 128, 192, 256];
}

/// <summary>R-113: 方式と、方式が「最大文字数」のときの文字数（数字 0 の幅の倍数）。一覧・詳細・小アイコンで共通の形。</summary>
public sealed record NameWidthSetting
{
    [JsonConverter(typeof(LenientEnumConverter<NameWidthMode>))]
    public NameWidthMode Mode { get; init; } = NameWidthMode.ShowAll;
    public int MaxChars { get; init; } = FileViewLimits.DefaultChars;
}

/// <summary>R-114: 列 1 つの表示・非表示。並びは列の中の順で持つ（非表示の列も並びを保つ）。</summary>
public sealed record DetailsColumnSetting
{
    [JsonConverter(typeof(LenientEnumConverter<DetailsColumn>))]
    public DetailsColumn Column { get; init; }
    public bool Visible { get; init; }
}

/// <summary>R-112-1 / Phase 16 Q3: 全ビューに同じく効く設定（INV-FILEVIEW-SETTINGS-PER-GROUP）。</summary>
public sealed record FileViewCommonSettings
{
    /// <summary>R-118: 同期状態などの OS の印をアイコンに重ねる。</summary>
    public bool ShowOverlays { get; init; } = true;
    /// <summary>
    /// R-01-7 / Q20: OS に登録されている拡張子は表示しない（既定はオフ）。隠すのは表示だけで、名前の変更・検索・並べ替えは拡張子込み。
    /// オンのあいだ、ファイルには拡張子込みの名前をいつもツールチップに出す。
    /// </summary>
    public bool HideKnownExtensions { get; init; }
}

public sealed record ListViewSettings
{
    /// <summary>R-110: 一覧だけ既定はオフ（今の動作。B-05）。</summary>
    public bool InPanelDragDrop { get; init; }
    public NameWidthSetting NameWidth { get; init; } = new();
    /// <summary>R-01-6 / Q35: 拡張子を揃えた位置に表示する。オフなら本体に続けて描く（登録済みの拡張子を隠す設定で隠れた項目は本体だけ）。</summary>
    public bool AlignExtension { get; init; } = true;
}

public sealed record DetailsViewSettings
{
    public bool InPanelDragDrop { get; init; } = true;
    public NameWidthSetting NameWidth { get; init; } = new();
    /// <summary>R-01-6 / Q35: 拡張子を揃えた位置に表示する。オフなら本体に続けて描く（登録済みの拡張子を隠す設定で隠れた項目は本体だけ）。</summary>
    public bool AlignExtension { get; init; } = true;
    public IReadOnlyList<DetailsColumnSetting> Columns { get; init; } = DefaultColumns;
    public bool FitColumnsToWindow { get; init; }

    /// <summary>R-114 / Q22: 名前・サイズ・更新日時・種類・属性を表示。拡張子・作成日時は非表示で後ろに置く。</summary>
    public static readonly IReadOnlyList<DetailsColumnSetting> DefaultColumns =
    [
        new() { Column = DetailsColumn.Size, Visible = true },
        new() { Column = DetailsColumn.Modified, Visible = true },
        new() { Column = DetailsColumn.Type, Visible = true },
        new() { Column = DetailsColumn.Attributes, Visible = true },
        new() { Column = DetailsColumn.Extension, Visible = false },
        new() { Column = DetailsColumn.Created, Visible = false },
    ];
}

/// <summary>小・中・大・特大アイコン。大きさだけはモードごと（INV-FILEVIEW-SETTINGS-PER-GROUP の例外）。</summary>
public sealed record IconsViewSettings
{
    public bool InPanelDragDrop { get; init; } = true;
    public int MediumSize { get; init; } = 48;
    public int LargeSize { get; init; } = 96;
    public int ExtraLargeSize { get; init; } = 256;
    [JsonConverter(typeof(LenientEnumConverter<CheckBoxMode>))]
    public CheckBoxMode CheckBoxes { get; init; } = CheckBoxMode.HoverAndMarked;
    /// <summary>R-117: 中アイコン以上でサムネイルを出す。クラウドのファイルは OS のキャッシュにあるものだけ（設定で変えられない）。</summary>
    public bool Thumbnails { get; init; } = true;
    /// <summary>R-117: フォルダのアイコンに中身のサムネイルを出す。</summary>
    public bool FolderThumbnails { get; init; } = true;
    /// <summary>R-119: 中・大・特大の名前の行数。</summary>
    public int NameLines { get; init; } = FileViewLimits.DefaultNameLines;
    /// <summary>Phase 16 Q6: 小アイコンの項目の幅。格子なので既定は最大文字数。</summary>
    public NameWidthSetting SmallIconWidth { get; init; } = DefaultSmallIconWidth;

    public static readonly NameWidthSetting DefaultSmallIconWidth = new() { Mode = NameWidthMode.MaxChars };
}

public sealed record TilesViewSettings
{
    public bool InPanelDragDrop { get; init; } = true;
    /// <summary>R-115 / Q17: 並べて表示とコンテンツで共通の 1 つ。</summary>
    [JsonConverter(typeof(LenientEnumListConverter<TileInfo>))]
    public IReadOnlyList<TileInfo> Info { get; init; } = DefaultInfo;
    public int TilesSize { get; init; } = 48;
    public int ContentSize { get; init; } = 48;
    [JsonConverter(typeof(LenientEnumConverter<CheckBoxMode>))]
    public CheckBoxMode CheckBoxes { get; init; } = CheckBoxMode.HoverAndMarked;
    /// <summary>R-117: 中アイコン以上でサムネイルを出す。クラウドのファイルは OS のキャッシュにあるものだけ（設定で変えられない）。</summary>
    public bool Thumbnails { get; init; } = true;
    /// <summary>R-117: フォルダのアイコンに中身のサムネイルを出す。</summary>
    public bool FolderThumbnails { get; init; } = true;

    public static readonly IReadOnlyList<TileInfo> DefaultInfo = [TileInfo.Type, TileInfo.Size];
}

/// <summary>
/// R-112: ファイルビューの設定は、系統ごとの欄に持つ（INV-FILEVIEW-SETTINGS-PER-GROUP）。全ビューに同じく効くものは Common。
/// 設定 JSON にそのまま載るので required を付けない（S-14）。欠けた欄・項目は既定値で埋まる（移行はしない）。
/// 欄はすべて不変の record で、列も IReadOnlyList なので、設定画面の下書きと共有してよい（INV-SETTINGS-DRAFT）。
/// 項目を足したら FileViewSettingsTests のマージのテストにも足す。
/// </summary>
public sealed record FileViewSettings
{
    public FileViewCommonSettings Common { get; init; } = new();
    public ListViewSettings List { get; init; } = new();
    public DetailsViewSettings Details { get; init; } = new();
    public IconsViewSettings Icons { get; init; } = new();
    public TilesViewSettings Tiles { get; init; } = new();

    /// <summary>
    /// Q33: 設定画面の確定。開いた時点（baseline）から下書き（draft）で変えた項目だけを、確定する時点の設定（current）へ書き込む。
    /// 下書きで変えていない項目は current の値を残す（別のウィンドウの見出しで変えた列の表示を、古い下書きで消さないため）。
    /// 項目の単位は、系統の欄の中の各プロパティ。列は要素の並びで比べる（別の参照でも中身が同じなら変えていない）。
    /// 項目を足したら FileViewSettingsTests のマージのテストにも足す。
    /// </summary>
    public static FileViewSettings Merge(FileViewSettings baseline, FileViewSettings draft, FileViewSettings current) =>
        (FileViewSettings)MergeRecord(baseline, draft, current, groups: true);

    private static object MergeRecord(object baseline, object draft, object current, bool groups)
    {
        // record の複製（コンパイラが作る <Clone>$）に、init のプロパティをリフレクションで書く
        var result = current.GetType().GetMethod("<Clone>$")!.Invoke(current, null)!;
        foreach (var property in current.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite))
        {
            var (b, d, c) = (property.GetValue(baseline), property.GetValue(draft), property.GetValue(current));
            if (groups) property.SetValue(result, MergeRecord(b!, d!, c!, groups: false));
            else if (!Same(b, d)) property.SetValue(result, d);
        }
        return result;
    }

    private static bool Same(object? a, object? b) => a is IEnumerable x && b is IEnumerable y && a is not string
        ? x.Cast<object>().SequenceEqual(y.Cast<object>())
        : Equals(a, b);

    /// <summary>
    /// 手で書いた JSON の null・範囲外・知らない値を直す。System.Text.Json は明示された null をそのまま入れ、
    /// 範囲も見ないので、読み込みの直後にここを通す（AppSettings.Normalize）。
    /// </summary>
    public static FileViewSettings Normalize(FileViewSettings? settings)
    {
        settings ??= new();
        var list = settings.List ?? new();
        var details = settings.Details ?? new();
        var icons = settings.Icons ?? new();
        var tiles = settings.Tiles ?? new();
        return new FileViewSettings
        {
            Common = settings.Common ?? new(),
            List = list with { NameWidth = Width(list.NameWidth, NameWidthMode.ShowAll) },
            Details = details with
            {
                NameWidth = Width(details.NameWidth, NameWidthMode.ShowAll),
                Columns = Columns(details.Columns),
            },
            Icons = icons with
            {
                MediumSize = Size(icons.MediumSize, 48),
                LargeSize = Size(icons.LargeSize, 96),
                ExtraLargeSize = Size(icons.ExtraLargeSize, 256),
                CheckBoxes = Enum.IsDefined(icons.CheckBoxes) ? icons.CheckBoxes : CheckBoxMode.HoverAndMarked,
                NameLines = Math.Clamp(icons.NameLines, FileViewLimits.MinNameLines, FileViewLimits.MaxNameLines),
                SmallIconWidth = Width(icons.SmallIconWidth, NameWidthMode.MaxChars),
            },
            Tiles = tiles with
            {
                Info = tiles.Info is null
                    ? TilesViewSettings.DefaultInfo
                    : [.. tiles.Info.Where(Enum.IsDefined).Distinct().Order()],
                TilesSize = Size(tiles.TilesSize, 48),
                ContentSize = Size(tiles.ContentSize, 48),
                CheckBoxes = Enum.IsDefined(tiles.CheckBoxes) ? tiles.CheckBoxes : CheckBoxMode.HoverAndMarked,
            },
        };
    }

    private static NameWidthSetting Width(NameWidthSetting? width, NameWidthMode defaultMode) => new()
    {
        Mode = width is not null && Enum.IsDefined(width.Mode) ? width.Mode : defaultMode,
        MaxChars = Math.Clamp(width?.MaxChars ?? FileViewLimits.DefaultChars, FileViewLimits.MinChars, FileViewLimits.MaxChars),
    };

    private static int Size(int size, int fallback) => FileViewLimits.IconSizes.Contains(size) ? size : fallback;

    private static IReadOnlyList<DetailsColumnSetting> Columns(IReadOnlyList<DetailsColumnSetting>? columns)
    {
        if (columns is null) return DetailsViewSettings.DefaultColumns;
        var kept = columns.OfType<DetailsColumnSetting>().Where(c => Enum.IsDefined(c.Column)).DistinctBy(c => c.Column).ToList();
        foreach (var missing in Enum.GetValues<DetailsColumn>().Where(c => kept.All(k => k.Column != c)))
            kept.Add(new DetailsColumnSetting { Column = missing, Visible = false });
        return kept;
    }
}
