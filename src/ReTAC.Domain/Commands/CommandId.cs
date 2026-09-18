namespace ReTAC.Domain.Commands;

/// <summary>
/// R-13: コマンドIDは卓駆と同じ値を用いる。
/// ReTAC 独自のコマンドは卓駆が使用していない 0x9000 帯を用いる。
/// 列挙するのは Phase 1 のスコープ内コマンドのみ。
/// </summary>
public enum CommandId
{
    // --- ファイル操作 ---
    OpenFile = 0x82DC,
    CopyToFolder = 0x82DE,
    MoveToFolder = 0x82E0,
    Delete = 0x82DF,
    Rename = 0x82E1,
    ConcatFiles = 0x82E4,
    ChangeAttributes = 0x82E2,
    CreateShortcut = 0x82F1,
    CreateFolder = 0x82F8,
    Quit = 0x831A,
    QuitAll = 0x814E,

    // --- 選択・マーク ---
    ToggleAllMarks = 0x8307,
    InvertMarks = 0x8312,
    MarkBySameExtension = 0x8325,
    MarkByWildcard = 0x8326,

    // --- 移動・ナビゲーション ---
    GoParent = 0x831E,
    GoRoot = 0x82FB,
    GoBack = 0x832D,
    GoForward = 0x832E,
    FolderHistory = 0x82FD,
    QuickAccess = 0x82FC,
    QuickAccessSettings = 0x82FA,
    QuickAccessAdd = 0x832C,
    DirectJump = 0x8327,
    SelectDrive = 0x82F3,
    DriveByNumberKey = 0x831F,
    GoDesktop = 0x832F,

    // --- 表示・ソート ---
    SortSettings = 0x8300,
    Refresh = 0x830C,
    FileTypeSettings = 0x82FF,
    ShowProperties = 0x82F2,

    // --- クリップボード ---
    ClipboardCopy = 0xE122,
    ClipboardCut = 0xE123,
    ClipboardPaste = 0xE125,
    CopyFileName = 0x831B,
    CopyFileNameWithPath = 0x83A9,
    CopyFileNameOnly = 0x7FC4,
    /// <summary>卓駆にない追加要件（scope §7.1）。`C:/DEV/...` 形式。</summary>
    CopyFileNameWithPathSlash = 0x9001,

    // --- 外部連携 ---
    // F-01: LaunchEditor / LaunchViewer / LaunchTerminal は外部ツールの一覧（ToolTarget）に置き換えた
    ExternalToolSettings = 0x815A,
    ShowPopupMenu = 0x831C,
    RunCommandLine = 0x82E9,
    ShowContextMenu = 0x8328,
    /// <summary>F-05: 外部ツールキューの進行状況のウィンドウを開く。確実な入口（ステータスバーは表示できれば便利な入口）。</summary>
    ExternalToolQueue = 0x9002,
    /// <summary>R-80: インクリメンタルサーチ。既定は Ctrl+F（B-18）。</summary>
    IncrementalSearch = 0x9003,
    /// <summary>R-81: 今いるフォルダの背景のシェルメニュー（「新規作成」を含む）。既定のキーは無い。</summary>
    ShowFolderBackgroundMenu = 0x9004,
    /// <summary>R-77: ドライブバーの表示・非表示。メニューの項目もコマンドの経路を通す（R-12）。</summary>
    ToggleDriveBar = 0x9005,
    /// <summary>R-82: 元に戻す。Ctrl+Z は固定のキーとして別に効く（R-83）。</summary>
    Undo = 0x9006,
    /// <summary>R-86: アドレスバーの表示・非表示。ドライブバーと同じくウィンドウごと。</summary>
    ToggleAddressBar = 0x9007,

    // --- 設定・ウィンドウ ---
    ColorAndFontSettings = 0x8151,
    KeyAssignSettings = 0x8155,
    VisibleDriveSettings = 0x814C,
    EnvironmentSettings = 0x8318,
    NewWindow = 0x830B,

    // --- ヘルプ ---
    /// <summary>卓駆のヘルプメニューと同じ ID。キー割り当ての対象にはしない（CommandLabels に載せない）。</summary>
    About = 0xE140,
}
