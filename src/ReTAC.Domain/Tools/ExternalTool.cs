namespace ReTAC.Domain.Tools;

/// <summary>
/// 外部ツール 1 件（F-01）。件数可変で登録し、キー割り当てとメニューは <see cref="Id"/> で参照する
/// （名前で参照すると改名で外れる）。設定 JSON にそのまま載るので <c>required</c> を付けない（S-14）。
/// </summary>
public sealed record ExternalTool
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    /// <summary>実行ファイル。名前だけ（<c>git.exe</c>）でもよい。空なら何もしない</summary>
    public string Path { get; init; } = "";
    /// <summary>マクロを書ける（F-02）</summary>
    public string Arguments { get; init; } = "";
    /// <summary>全体の設定「連続起動はしない」が OFF のときだけ効く。外部ツールキューで順番に処理する（F-05）</summary>
    public bool LaunchPerItem { get; init; }
    /// <summary><c>G</c> →「外部ツール ▶」に出す（F-08）</summary>
    public bool ShowInPopup { get; init; } = true;
    /// <summary>コンソールのツール向け。終了後もキーを押すまで結果を残す（F-03）</summary>
    public bool KeepWindowOpen { get; init; }
    /// <summary>展開後のコマンドラインを見せてから起動する（F-03）</summary>
    public bool ConfirmBeforeRun { get; init; }
}

/// <summary>
/// 初期状態の 3 件（F-01）。既定のキー割り当て（<see cref="Keys.DefaultKeyMap"/>）がこの番号を指す。
///
/// B-05: 既定値に誰かの好みは埋めない。ただし<b>設定ファイルが無い環境でも最低限動く</b>ことは
/// それとは別の要件なので、どの Windows にも必ずある <c>notepad.exe</c> と <c>cmd.exe</c> を置く。
/// 好みの道具に替えたい利用者は外部ツールの設定（0x815A）で変えられる。
/// <b>ここに特定の製品をインストールした環境でしか通らないパスを足さないこと。</b>
///
/// エディタは <c>${file}</c>、ビューアは <c>${file}!</c> と、必須マクロの有無をわざと違えてある。
/// 対象が無いときエディタは空のまま開き、ビューアは起動しない。初回の利用者がこの差に触れることで、
/// マクロの <c>!</c> の意味が自然と分かる（F-02）。
/// </summary>
public static class DefaultExternalTools
{
    public const int EditorId = 1;
    public const int ViewerId = 2;
    public const int TerminalId = 3;

    /// <summary>利用者が足すツールの最初の番号。削除した番号は使い回さない（既定のキーが別のツールを指さないように）</summary>
    public const int FirstFreeId = 4;

    public static List<ExternalTool> Create() =>
    [
        new() { Id = EditorId, Name = "テキスト エディタ", Path = "notepad.exe", Arguments = "${file}" },
        new() { Id = ViewerId, Name = "テキスト ビューア", Path = "notepad.exe", Arguments = "${file}!" },
        new() { Id = TerminalId, Name = "ターミナル", Path = "cmd.exe" },
    ];
}
