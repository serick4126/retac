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

    /// <summary>
    /// R-40: 常駐する。終了コマンドでプロセスを終わらせず最小化する。
    /// 既定は常駐しない（2026-09-29 利用者の決定）。初めて使う人が閉じてもプロセスが残ると、終わっていないことに気づきにくい
    /// </summary>
    public bool Resident { get; set; }
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

    /// <summary>R-86: アドレスバーを出すか。初回は出す。</summary>
    public bool ShowAddressBar { get; set; } = true;

    /// <summary>R-89: ブックマークバーを出すか。初回は出す（空の案内を出す）。</summary>
    public bool ShowBookmarkBar { get; set; } = true;

    /// <summary>B-20: 初回と設定キーが欠けた場合は、ドライブツリーを表示する。</summary>
    public bool ShowLeftPanel { get; set; } = true;
    public LeftPanelViewKind LeftPanelView { get; set; } = LeftPanelViewKind.DriveTree;

    /// <summary>R-96: 全ビュー共通の幅を 96 DPI 論理値で保存する。</summary>
    public int LeftPanelWidth { get; set; } = 280;

    /// <summary>
    /// R-112-4: 今の表示モード。アプリ全体で 1 つ（INV-VIEWMODE-APP-WIDE）で、切り替えたら全ウィンドウへ当てて保存する。
    /// 知らない名前で設定ファイル全体を捨てないよう、寛容な変換器で読み、Normalize で一覧に直す。
    /// </summary>
    [JsonConverter(typeof(LenientEnumConverter<FileViewMode>))]
    public FileViewMode ViewMode { get; set; } = FileViewMode.List;

    /// <summary>R-112: ファイルビューの設定（系統ごとの欄と共通の欄）。</summary>
    public FileViewSettings FileViews { get; set; } = new();

    /// <summary>ブックマークビューで展開しているグループの安定 ID。</summary>
    public List<string> ExpandedBookmarkGroupIds { get; set; } = [];

    /// <summary>
    /// R-89: ブックマーク。置き場は「バー」と「その他」の 2 つで固定。
    /// B-22: 設定ファイルが無いときだけ、使い方を見せる初期の 6 件をバーに置く（INV-NO-PREFERENCE-DEFAULTS の例外）。
    /// 欄があれば（空でも）その値をそのまま使う。移行コードは書かない。
    /// </summary>
    public BookmarkSet Bookmarks { get; set; } = DefaultBookmarks.Create(AppContext.BaseDirectory);

    /// <summary>R-89: ブックマークバーの表示の形。</summary>
    public BookmarkBarStyle BookmarkBarStyle { get; set; } = BookmarkBarStyle.IconAndText;

    // --- 配色とフォント（5-1 節） ------------------------------------------
    /// <summary>R-108: 既定は Windows の設定に従う。キーが無い既存の設定もこれで起動する（移行はしない）。</summary>
    public Rendering.ColorMode ColorMode { get; set; } = Rendering.ColorMode.System;

    /// <summary>独自の配色で、既定から変えた色だけを持つ。キーは <see cref="ThemeSlots"/> の Key。</summary>
    public Dictionary<string, string> Colors { get; set; } = [];

    /// <summary>
    /// R-108-2: 「Windows の設定に従う」でライト用・ダーク用の 8 色。推奨値から変えた色だけを持つ。
    /// キーは <see cref="ThemeSlots.SystemMode"/> の Key。ほかのキー・読めない色は読むときに無視する。
    /// </summary>
    public Dictionary<string, string> SystemLightColors { get; set; } = [];
    public Dictionary<string, string> SystemDarkColors { get; set; } = [];
    public string? FontFamily { get; set; }
    public float? FontSize { get; set; }

    /// <summary>R-101: 左パネルのフォント。一覧とは別に持つ。</summary>
    public string? LeftPanelFontFamily { get; set; }
    public float? LeftPanelFontSize { get; set; }

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
        if (!string.IsNullOrWhiteSpace(LeftPanelFontFamily)) theme = theme with { LeftPanelFontFamily = LeftPanelFontFamily };
        if (LeftPanelFontSize is > 0) theme = theme with { LeftPanelFontSize = LeftPanelFontSize.Value };
        return theme;
    }

    /// <summary>
    /// R-108-2: その組の推奨値に、保存されている 8 色の変更を重ねる。8 項目以外のキー・null・読めない色は無視して
    /// 推奨値のままにする（手で書いた設定ファイルで起動を止めない）。
    /// </summary>
    public Rendering.Theme ToSystemTheme(bool dark)
    {
        var theme = Rendering.Theme.Recommended(dark);
        var colors = dark ? SystemDarkColors : SystemLightColors;
        foreach (var slot in ThemeSlots.SystemMode)
            if (colors.TryGetValue(slot.Key, out var hex) && ThemeSlots.FromHex(hex) is { } color)
                theme = slot.Set(theme, color);
        return theme;
    }

    /// <summary>R-108-2: その組の推奨値と違う色だけを書き出す。推奨値に戻した項目はキーごと消える。</summary>
    public void FromSystemTheme(bool dark, Rendering.Theme theme)
    {
        var recommended = Rendering.Theme.Recommended(dark);
        Dictionary<string, string> colors = [];
        foreach (var slot in ThemeSlots.SystemMode)
        {
            // 名前付きの色（Color.Red）と同じ ARGB の色は == で等しくならないので、値で比べる
            var color = slot.Get(theme);
            if (color.ToArgb() != slot.Get(recommended).ToArgb()) colors[slot.Key] = ThemeSlots.ToHex(color);
        }
        if (dark) SystemDarkColors = colors;
        else SystemLightColors = colors;
    }

    /// <summary>
    /// R-108: ファイルリストに実際に当てる配色。起動時のモードと OS の状態で解決し、8 色は起動時の側の組だけを使う
    /// （反対側の組は保存とプレビューだけで、実画面に出さない。R-108-2）。
    /// </summary>
    public Rendering.Theme ToScreenTheme(Rendering.ColorMode startupMode, Rendering.OsTheme os) =>
        Rendering.Theme.Resolve(ToTheme(), startupMode, os, ToSystemTheme(os.Dark));

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
        LeftPanelFontFamily = theme.LeftPanelFontFamily == Rendering.Theme.Default.LeftPanelFontFamily ? null : theme.LeftPanelFontFamily;
        LeftPanelFontSize = Math.Abs(theme.LeftPanelFontSize - Rendering.Theme.Default.LeftPanelFontSize) < 0.01f ? null : theme.LeftPanelFontSize;
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
    /// <summary>
    /// Q4 / Q10: 読み込みの直後に、消した外部ツール・読めないコマンド・クイックアクセスの Group を取り除く。
    /// その場で書き換えるだけで保存はしない（次の通常の保存で書く）。外部ツールの番号は使い回さないので、
    /// ファイルに古い参照がしばらく残っても別のツールに化けることはない（キー割り当てと同じ作り）。
    /// </summary>
    public void Normalize()
    {
        // 手で書いた JSON の null を既定値に戻す。System.Text.Json は非 nullable の欄にも明示された null を
        // そのまま入れるので、型注釈だけでは防げず、ここで直さないと起動が NullReferenceException で止まる
        HiddenDrives = [.. (HiddenDrives ?? []).OfType<string>()];
        FolderHistory = [.. (FolderHistory ?? []).OfType<string>()];
        ExpandedBookmarkGroupIds = [.. (ExpandedBookmarkGroupIds ?? []).OfType<string>()];
        DriveFolders = WithoutNulls(DriveFolders);
        Colors = WithoutNulls(Colors);
        SystemLightColors = WithoutNulls(SystemLightColors);
        SystemDarkColors = WithoutNulls(SystemDarkColors);
        KeyBindings = WithoutNulls(KeyBindings);
        ExternalTools = [.. (ExternalTools ?? DefaultExternalTools.Create()).OfType<ExternalTool>()
            .Select(t => t with { Name = t.Name ?? "", Path = t.Path ?? "", Arguments = t.Arguments ?? "" })];
        QuickAccess = [.. (QuickAccess ?? []).OfType<QuickAccessEntry>()
            .Select(e => e with { Title = e.Title ?? "", Path = e.Path ?? "" })];
        Bookmarks ??= new();
        Bookmarks.Bar ??= [];
        Bookmarks.Other ??= [];

        var ids = ExternalTools.Select(t => t.Id).ToList();
        var list = new QuickAccessList();
        foreach (var entry in QuickAccess) list.Add(entry);
        // 一時の一覧を整理しただけでは QuickAccess は変わらない。書き戻さないと、後で共有の一覧を作るときに整理前の値から作ってしまう
        if (list.DropUnknownTools(ids)) QuickAccess = [.. list.Items];
        BookmarkRules.DropUnknownTools(Bookmarks, ids);
        BookmarkRules.EnsureIds(Bookmarks);   // R-98: DropUnknownTools が null の項目を落とした後で
        FileViews = FileViewSettings.Normalize(FileViews);
        ViewMode = FileViewModes.Normalize(ViewMode);
    }

    private static Dictionary<string, string> WithoutNulls(Dictionary<string, string>? map) =>
        (map ?? []).Where(p => p.Value is not null).ToDictionary();

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
