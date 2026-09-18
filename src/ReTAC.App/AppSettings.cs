using System.Text.Json;
using System.Text.Json.Serialization;
using ReTAC.Domain.Listing;
using ReTAC.Domain.Navigation;
using ReTAC.Domain.Tools;
using SortOrder = ReTAC.Domain.Listing.SortOrder;

namespace ReTAC.App;

/// <summary>
/// R-55: 設定は実行ファイルと同じディレクトリの JSON に置く。人が読んで直せること。
/// 卓駆のレジストリは読み書きしない（R-20）。
/// <b>既定値はすべてコードに埋まっている</b>ので、ファイルが無くても現行の卓駆と同じ挙動になる（R-20-2 / T7-2）。
/// </summary>
public sealed class AppSettings
{
    public const string FileName = "retac.settings.json";

    // --- 起動と常駐 -------------------------------------------------------
    /// <summary>R-26: 終了時のフォルダを保持する。</summary>
    public bool KeepLastFolder { get; set; } = true;
    public string? LastFolder { get; set; }
    /// <summary>R-26: 「保持する」が無効なときの固定の起動フォルダ。</summary>
    public string? StartFolder { get; set; }

    /// <summary>R-40: 常駐する。終了コマンドでプロセスを終わらせず最小化する。</summary>
    public bool Resident { get; set; } = true;
    /// <summary>R-40-6: 起動時はウィンドウを表示しない。</summary>
    public bool StartMinimized { get; set; }

    public int WindowX { get; set; } = -1;
    public int WindowY { get; set; } = -1;
    public int WindowWidth { get; set; } = 1200;
    public int WindowHeight { get; set; } = 700;

    // --- 表示 -------------------------------------------------------------
    public SortKey SortKey { get; set; } = SortOrder.Default.Key;
    public SortDirection SortDirection { get; set; } = SortOrder.Default.Direction;
    public ComparisonMode SortMode { get; set; } = SortOrder.Default.Mode;

    public bool ShowFolders { get; set; } = true;
    public bool ShowPrograms { get; set; } = true;
    public bool ShowAssociated { get; set; } = true;
    public bool ShowArchives { get; set; } = true;
    public bool ShowOthers { get; set; } = true;
    public bool ShowSystemFiles { get; set; } = true;
    public bool ShowHiddenFiles { get; set; } = true;

    // --- ドライブバー（16.7 節） -------------------------------------------
    /// <summary>表示しないドライブ。空ならすべて表示する。</summary>
    public List<string> HiddenDrives { get; set; } = [];

    /// <summary>
    /// ドライブごとに最後にいたフォルダ。キーは "C:\\" のようなルート。
    /// 数字キーやドライブバーで戻ったとき、そのドライブで前にいた場所へ着く（卓駆の挙動）。
    /// </summary>
    public Dictionary<string, string> DriveFolders { get; set; } = [];
    public bool ShowDesktopButton { get; set; } = true;

    /// <summary>
    /// R-77: ドライブバーを出すか。非表示のときの `L` はモーダルで選ぶ。
    /// 型を変えない（変えると読み込みに失敗し、設定全体が既定値に戻る）。
    /// </summary>
    public bool ShowDriveBar { get; set; } = true;

    // --- 配色とフォント（5-1 節） ------------------------------------------
    /// <summary>既定から変えた色だけを持つ。キーは <see cref="ThemeSlots"/> の Key。</summary>
    public Dictionary<string, string> Colors { get; set; } = [];
    public string? FontFamily { get; set; }
    public float? FontSize { get; set; }

    // --- キー割り当て（5-2 節） --------------------------------------------
    /// <summary>
    /// 既定（`DefaultKeyMap`）から変えた枠だけを持つ。値が空文字なら「割り当てなし」。
    /// キーは "C" "Shift+F3" のような表記（<see cref="KeySlots.Label"/>）。
    /// </summary>
    public Dictionary<string, string> KeyBindings { get; set; } = [];

    // --- 操作 -------------------------------------------------------------
    /// <summary>
    /// F-09: 複数選択の時外部ツールの連続起動はしない。ON ならすべての操作（外部ツール・プロパティ表示）で
    /// カーソル位置の 1 件だけを渡す。処理の種類で振る舞いを変えない。
    /// </summary>
    public bool SuppressMultipleToolLaunch { get; set; } = true;

    // --- 外部ツール（F-01） -------------------------------------------------
    /// <summary>件数可変。並びはメニューと `G` の「外部ツール ▶」の並び。初期状態は OS 標準の 3 件（B-05）。</summary>
    public List<ExternalTool> ExternalTools { get; set; } = DefaultExternalTools.Create();

    /// <summary>次に足すツールの番号。削除した番号を使い回さない（既定のキーが別のツールを指さないように）。</summary>
    public int NextExternalToolId { get; set; } = DefaultExternalTools.FirstFreeId;

    // --- クイックアクセスと履歴 -------------------------------------------
    public List<QuickAccessEntry> QuickAccess { get; set; } = [];
    public bool QuickAccessShowTitles { get; set; } = true;
    public bool QuickAccessFixMissing { get; set; }

    /// <summary>N-02: 移動とコピー先で共通のひとつ。R-20-3 により卓駆からは引き継がない。</summary>
    public List<string> FolderHistory { get; set; } = [];

    // ---------------------------------------------------------------------

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,                         // R-55: 人が読んで直せること
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>実行ディレクトリ。書けない場合は R-55-3 によりユーザープロファイル配下へ逃がす。</summary>
    public static string PrimaryPath =>
        Path.Combine(AppContext.BaseDirectory, FileName);

    public static string FallbackPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ReTAC", FileName);

    /// <summary>実際に読み書きしている場所。フォールバックへ移った場合は初回に一度だけ知らせる。</summary>
    public static string? ActualPath { get; private set; }

    public static AppSettings Load()
    {
        foreach (var path in new[] { PrimaryPath, FallbackPath })
        {
            try
            {
                if (!File.Exists(path)) continue;
                if (JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) is not { } loaded) continue;
                ActualPath = path;
                return loaded;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // 壊れていても既定値で起動する。設定ファイルのために動かなくなってはならない
            }
        }
        return new AppSettings();
    }

    /// <returns>フォールバックへ逃がしたら true（R-55-3 の通知が要る）。</returns>
    public bool Save()
    {
        var json = JsonSerializer.Serialize(this, Json);
        try
        {
            WriteAtomic(PrimaryPath, json);
            ActualPath = PrimaryPath;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FallbackPath)!);
            WriteAtomic(FallbackPath, json);
            var moved = ActualPath != FallbackPath;
            ActualPath = FallbackPath;
            return moved;
        }
    }

    /// <summary>
    /// 一時ファイルに書いてから差し替える。直接上書きすると、書き込み中に落ちたときに
    /// JSON が壊れる。<see cref="Load"/> は壊れたファイルを黙って捨てて既定値で起動するので、
    /// キー割り当て・配色・クイックアクセス・履歴が予告なく全部消える（V-07）。
    /// </summary>
    private static void WriteAtomic(string path, string json)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }

    public SortOrder ToSortOrder() => new(SortKey, SortDirection, SortMode);

    public HashSet<char> ToHiddenDrives() =>
        [.. HiddenDrives.Where(d => d.Length > 0).Select(d => char.ToUpperInvariant(d[0]))];

    /// <summary>5-1 節の既定に、保存されている変更だけを重ねる。</summary>
    public Rendering.Theme ToTheme()
    {
        var theme = Rendering.Theme.Default;
        foreach (var slot in ThemeSlots.All)
            if (Colors.TryGetValue(slot.Key, out var hex) && ThemeSlots.FromHex(hex) is { } color)
                theme = slot.Set(theme, color);

        if (!string.IsNullOrWhiteSpace(FontFamily)) theme = theme with { FontFamily = FontFamily };
        if (FontSize is > 0) theme = theme with { FontSize = FontSize.Value };
        return theme;
    }

    /// <summary>既定と違う色だけを書き出す。設定ファイルを読みやすく保つため。</summary>
    public void FromTheme(Rendering.Theme theme)
    {
        Colors = [];
        foreach (var slot in ThemeSlots.All)
        {
            var color = slot.Get(theme);
            if (color != slot.Get(Rendering.Theme.Default)) Colors[slot.Key] = ThemeSlots.ToHex(color);
        }
        FontFamily = theme.FontFamily == Rendering.Theme.Default.FontFamily ? null : theme.FontFamily;
        FontSize = Math.Abs(theme.FontSize - Rendering.Theme.Default.FontSize) < 0.01f ? null : theme.FontSize;
    }

    /// <summary>R-14 の初期キーマップに、保存されている変更だけを重ねる。</summary>
    public Domain.Keys.KeyMap ToKeyMap()
    {
        var map = Domain.Keys.DefaultKeyMap.Create();
        foreach (var (label, command) in KeyBindings)
        {
            // R-25: 枠に無いキー（Ctrl+C など）・Ctrl+Z（R-83）は、設定ファイルに書かれていても割り当てない
            if (KeySlots.Parse(label) is not { } binding || !KeySlots.All.Contains(binding)) continue;
            map.Assign(binding, Domain.Commands.CommandTarget.Parse(command));
        }
        // 消したツールを指す割り当ては残さない（既定のキーも含めて落とす）
        map.DropUnknownTools(ExternalTools.Select(t => t.Id));
        return map;
    }

    /// <summary>既定と違う枠だけを書き出す。</summary>
    public void FromKeyMap(Domain.Keys.KeyMap map)
    {
        var defaults = Domain.Keys.DefaultKeyMap.Create();
        KeyBindings = [];
        foreach (var slot in KeySlots.All)
        {
            var now = map.Resolve(slot);
            if (Equals(now, defaults.Resolve(slot))) continue;
            KeyBindings[KeySlots.Label(slot)] = now?.Serialize() ?? "";
        }
    }

    public FileTypeFilter ToFileTypeFilter() => new()
    {
        Folders = ShowFolders,
        Programs = ShowPrograms,
        Associated = ShowAssociated,
        Archives = ShowArchives,
        Others = ShowOthers,
        SystemFiles = ShowSystemFiles,
        HiddenFiles = ShowHiddenFiles,
    };

    /// <summary>
    /// B-05: 既定の登録は持たない。何をよく開くかは人によるので、初期値に誰かの好みを埋めない
    /// （`J` は登録 0 件でも「設定...」「このフォルダを追加」が出るので空でも操作できる）。
    /// </summary>
    public QuickAccessList ToQuickAccess()
    {
        var list = new QuickAccessList { ShowTitles = QuickAccessShowTitles, FixMissingAutomatically = QuickAccessFixMissing };
        foreach (var entry in QuickAccess) list.Add(entry);
        return list;
    }

    public FolderHistory ToFolderHistory()
    {
        var history = new FolderHistory();
        // 新しい順に入っているので、古い方から入れ直す
        foreach (var path in Enumerable.Reverse(FolderHistory)) history.Remember(path);
        return history;
    }
}
