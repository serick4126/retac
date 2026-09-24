using ReTAC.App.Rendering;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Keys;
using ReTAC.Domain.Navigation;
using ReTAC.Domain.Tools;

namespace ReTAC.App;

/// <summary>
/// R-102-3: 統合した設定画面の 6 ページが編集する下書き。画面を開いたときに共有の設定から作り、
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
    public bool Resident { get; set; } = true;
    public bool StartMinimized { get; set; }
    public bool KeepLastFolder { get; set; } = true;

    private bool _suppressMultipleToolLaunch = true;
    /// <summary>
    /// F-09: 外部ツールの連続起動をしない。外部ツールページの「マークした項目ごとに起動する」の
    /// 灰色表示はこの値に従うので、変わったら <see cref="SuppressMultipleChanged"/> を上げる。
    /// </summary>
    public bool SuppressMultipleToolLaunch
    {
        get => _suppressMultipleToolLaunch;
        set
        {
            if (_suppressMultipleToolLaunch == value) return;
            _suppressMultipleToolLaunch = value;
            SuppressMultipleChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // --- 配色・フォント（ColorFontPage） ----------------------------------
    public Theme Theme { get; set; } = Theme.Default;

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
    /// <summary>「連続起動はしない」が変わった。外部ツールページの灰色表示を更新する合図（F-09）。</summary>
    public event EventHandler? SuppressMultipleChanged;

    /// <summary>共有の設定・キーマップ・テーマ・クイックアクセスから下書きを作る。</summary>
    public static SettingsDraft From(AppSettings settings, KeyMap keyMap, Theme theme, QuickAccessList quickAccess)
    {
        var draft = new SettingsDraft
        {
            Resident = settings.Resident,
            StartMinimized = settings.StartMinimized,
            KeepLastFolder = settings.KeepLastFolder,
            SuppressMultipleToolLaunch = settings.SuppressMultipleToolLaunch,
            Theme = theme,
            KeyBindings = KeySlots.All.ToDictionary(slot => slot, slot => keyMap.Resolve(slot)),
            ExternalTools = [.. settings.ExternalTools],
            NextExternalToolId = settings.NextExternalToolId,
            HiddenDrives = settings.ToHiddenDrives(),
            ShowDesktopButton = settings.ShowDesktopButton,
        };

        draft.QuickAccess.ShowTitles = quickAccess.ShowTitles;
        draft.QuickAccess.FixMissingAutomatically = quickAccess.FixMissingAutomatically;
        foreach (var entry in quickAccess.Items) draft.QuickAccess.Add(entry);

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
        settings.SuppressMultipleToolLaunch = SuppressMultipleToolLaunch;

        // R-36: 全ウィンドウのメニューの作り直しは重いので、実際に変わったときだけ MainForm に伝える
        var externalToolsChanged = !ExternalTools.SequenceEqual(settings.ExternalTools);
        settings.ExternalTools = [.. ExternalTools];
        settings.NextExternalToolId = NextExternalToolId;

        settings.FromTheme(Theme);

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
