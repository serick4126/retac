using ReTAC.App.Rendering;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Keys;
using ReTAC.Domain.Listing;
using ReTAC.Domain.Navigation;
using ReTAC.Domain.Tools;

namespace ReTAC.App;

/// <summary>
/// R-102-3: 統合した設定画面の 7 ページが編集する下書き。画面を開いたときに共有の設定から作り、
/// ページの操作はこれだけを変える。<see cref="CommitTo"/> を呼ぶまで <see cref="AppSettings"/>・
/// キーマップ・クイックアクセスの実体は変わらない（INV-SETTINGS-DRAFT）。
///
/// <b>浅いコピーを混ぜないこと。</b> 外部ツール・クイックアクセスの列は必ず新しい列で持つ
/// （要素の record 自体は不変なので使い回してよいが、列（List / 内部の QuickAccessList）は
/// 元の実体と別のインスタンスにする。混ぜると、画面では下書きに見えて編集の時点で共有の設定が変わる）。
/// </summary>
public sealed class SettingsDraft
{
    // --- 動作環境（EnvironmentPage） -------------------------------------
    public bool Resident { get; set; }
    public bool StartMinimized { get; set; }
    public bool KeepLastFolder { get; set; } = true;

    // --- 配色・フォント（ColorFontPage） ----------------------------------
    /// <summary>保存される独自の配色とフォント。OS の色で解決したものは入れない（<see cref="Theme.Resolve"/>）。</summary>
    public Theme Theme { get; set; } = Theme.Default;
    public ColorMode ColorMode { get; set; } = ColorMode.System;
    /// <summary>
    /// R-108-2: 「Windows の設定に従う」のライト用・ダーク用の 8 色。設定の Dictionary は共有せず、
    /// 読んだ結果を不変の <see cref="Theme"/> として持つ（使うのは <see cref="ThemeSlots.SystemMode"/> の項目だけ）。
    /// </summary>
    public Theme SystemLight { get; set; } = Theme.Recommended(dark: false);
    public Theme SystemDark { get; set; } = Theme.Recommended(dark: true);

    // --- ファイルビュー（FileViewPage） ------------------------------------
    /// <summary>不変の record なので共有の設定と同じインスタンスを持ってよい。ページは with で差し替える（INV-SETTINGS-DRAFT）。</summary>
    public FileViewSettings FileViews { get; set; } = new();
    /// <summary>Q33: 開いた時点の FileViews。確定のときに、下書きで変えた項目を見分けるのに使う。</summary>
    public FileViewSettings FileViewsBaseline { get; private set; } = new();

    // --- キー割り当て（KeyAssignPage） ------------------------------------
    /// <summary>枠ごとの今の割り当て。値が null なら「割り当てなし」（KeyAssignPage の内部表現と同じ形）。</summary>
    public Dictionary<KeyBinding, CommandTarget?> KeyBindings { get; set; } = [];

    // --- 外部ツール（ExternalToolPage） -----------------------------------
    private List<ExternalTool> _externalTools = [];
    /// <summary>
    /// 一覧をまるごと差し替える形（プロジェクト全体の慣習。<c>_settings.ExternalTools = [.. tools]</c> と同じ）。
    /// 消えたツールを指す割り当て・クイックアクセスの登録は、差し替えのたびにここで下書きの中から閉じる
    /// （ページがどう編集しても、消したツールへの参照を残さない。F-01 / INV-TOOLTARGET-FK）。
    /// </summary>
    public List<ExternalTool> ExternalTools
    {
        get => _externalTools;
        set
        {
            _externalTools = value;
            var ids = value.Select(t => t.Id).ToHashSet();
            foreach (var key in KeyBindings
                         .Where(b => b.Value is ToolTarget tool && !ids.Contains(tool.ToolId))
                         .Select(b => b.Key).ToList())
                KeyBindings[key] = null;
            QuickAccess.DropUnknownTools(ids);
            ToolsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public int NextExternalToolId { get; set; } = DefaultExternalTools.FirstFreeId;

    /// <summary>この画面を開いてから <see cref="RemoveTool"/> で消したツールの ID。件数以外は今のところ使わない。</summary>
    public List<int> RemovedToolIds { get; } = [];

    /// <summary>ツールを一覧・キー割り当て・クイックアクセスの下書きから一貫して外す（F-01 / INV-TOOLTARGET-FK）。</summary>
    public void RemoveTool(int id)
    {
        RemovedToolIds.Add(id);
        ExternalTools = [.. ExternalTools.Where(t => t.Id != id)];
    }

    // --- 表示するドライブ（DriveVisibilityPage） --------------------------
    public HashSet<char> HiddenDrives { get; set; } = [];
    public bool ShowDesktopButton { get; set; } = true;

    // --- クイックアクセス（QuickAccessPage） ------------------------------
    /// <summary>共有の実体とは別のインスタンス（INV-QUICKACCESS-SHARED）。確定時に <see cref="QuickAccessList.ReplaceAll"/> で書き戻す。</summary>
    public QuickAccessList QuickAccess { get; set; } = new();

    // --- ページ間のつながり ------------------------------------------
    /// <summary>外部ツールの一覧が変わった（追加・改名・削除）。キー割り当て・クイックアクセスページが一覧を出し直す合図。</summary>
    public event EventHandler? ToolsChanged;

    /// <summary>共有の設定・キーマップ・テーマ・クイックアクセスから下書きを作る。</summary>
    public static SettingsDraft From(AppSettings settings, KeyMap keyMap, Theme theme, QuickAccessList quickAccess)
    {
        var draft = new SettingsDraft
        {
            Resident = settings.Resident,
            StartMinimized = settings.StartMinimized,
            KeepLastFolder = settings.KeepLastFolder,
            Theme = theme,
            ColorMode = settings.ColorMode,
            SystemLight = settings.ToSystemTheme(dark: false),
            SystemDark = settings.ToSystemTheme(dark: true),
            FileViews = settings.FileViews,
            // KeyBindings を先に代入する。ExternalTools のセッターが「存在しないツールを指す
            // 割り当てを落とす」処理を持つため、後から代入すると順序が入れ替わり効果が消える。
            KeyBindings = KeySlots.All.ToDictionary(slot => slot, slot => keyMap.Resolve(slot)),
            ExternalTools = [.. settings.ExternalTools],
            NextExternalToolId = settings.NextExternalToolId,
            HiddenDrives = settings.ToHiddenDrives(),
            ShowDesktopButton = settings.ShowDesktopButton,
        };

        draft.QuickAccess.ShowTitles = quickAccess.ShowTitles;
        draft.QuickAccess.FixMissingAutomatically = quickAccess.FixMissingAutomatically;
        foreach (var entry in quickAccess.Items) draft.QuickAccess.Add(entry);

        draft.FileViewsBaseline = settings.FileViews;
        return draft;
    }

    /// <summary>
    /// 下書きを <paramref name="settings"/> と共有の <paramref name="quickAccess"/> へ書き写す。
    /// 画面への反映（メニューの作り直し・テーマの適用・ドライブバーの更新）は MainForm 側の仕事なので、
    /// ここでは渡された 2 つの実体を書き換えるだけで、UI には触らない（R-102-3）。
    /// </summary>
    public SettingsCommitResult CommitTo(AppSettings settings, QuickAccessList quickAccess)
    {
        settings.Resident = Resident;
        settings.StartMinimized = StartMinimized;
        settings.KeepLastFolder = KeepLastFolder;

        // R-36: 全ウィンドウのメニューの作り直しは重いので、実際に変わったときだけ MainForm に伝える
        var externalToolsChanged = !ExternalTools.SequenceEqual(settings.ExternalTools);
        settings.ExternalTools = [.. ExternalTools];
        settings.NextExternalToolId = NextExternalToolId;

        settings.FromTheme(Theme);
        settings.ColorMode = ColorMode;
        settings.FromSystemTheme(dark: false, SystemLight);
        settings.FromSystemTheme(dark: true, SystemDark);
        // Q33: 開いている間に別のウィンドウの見出しで変えた値を、古い下書きで消さない
        settings.FileViews = FileViewSettings.Merge(FileViewsBaseline, FileViews, settings.FileViews);

        var beforeKeyBindings = new Dictionary<string, string>(settings.KeyBindings);
        var keyMap = new KeyMap(KeyBindings
            .Where(b => b.Value is not null)
            .Select(b => new KeyValuePair<KeyBinding, CommandTarget>(b.Key, b.Value!)));
        // KeyBindings は公開の setter を持ち、インポート等で存在しないツールを指す値が
        // 紛れ込みうる（INV-TOOLTARGET-FK）。ExternalTools のセッターを経由しない編集もあるため、
        // 確定の直前にもう一度落とす
        keyMap.DropUnknownTools(ExternalTools.Select(t => t.Id));
        settings.FromKeyMap(keyMap);
        var keyBindingsChanged = !KeyBindingsEqual(beforeKeyBindings, settings.KeyBindings);

        settings.HiddenDrives = [.. HiddenDrives.Select(c => c.ToString())];
        settings.ShowDesktopButton = ShowDesktopButton;

        // ブックマークは統合画面に入らないが、消したツールを指す項目は確定のときにここで外す（INV-TOOLTARGET-FK）
        BookmarkRules.DropUnknownTools(settings.Bookmarks, ExternalTools.Select(t => t.Id));

        quickAccess.ShowTitles = QuickAccess.ShowTitles;
        quickAccess.FixMissingAutomatically = QuickAccess.FixMissingAutomatically;
        quickAccess.ReplaceAll(QuickAccess.Items);   // INV-QUICKACCESS-SHARED: 実体は差し替えず中身だけ

        return new SettingsCommitResult(externalToolsChanged, keyBindingsChanged);
    }

    private static bool KeyBindingsEqual(Dictionary<string, string> a, Dictionary<string, string> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var now) && now == kv.Value);
}

/// <summary>
/// <see cref="SettingsDraft.CommitTo"/> が書き写した結果、実際に変わったもの。
/// MainForm はこれを見て、全ウィンドウのメニュー・ブックマークバーの作り直し（R-36）が要るかを決める。
/// </summary>
public sealed record SettingsCommitResult(bool ExternalToolsChanged, bool KeyBindingsChanged);
