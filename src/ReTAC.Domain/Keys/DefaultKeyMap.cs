using ReTAC.Domain.Commands;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Keys;

/// <summary>Phase 1 で使う仮想キーコード。値は Win32 の VK_* と同じ（WinForms の Keys とも一致する）。</summary>
public static class Vk
{
    /// <summary>
    /// R-73: マウスのボタン。VK_MBUTTON / VK_XBUTTON1 / VK_XBUTTON2 で、
    /// WinForms の <c>Keys.MButton</c> などと同じ値。キーと同じ枠に入れるためここに置く。
    /// 左（0x01）と右（0x02）は割り当ての対象にしないので定数も作らない。
    /// </summary>
    public const ushort MButton = 0x04;
    public const ushort XButton1 = 0x05;
    public const ushort XButton2 = 0x06;

    public const ushort Back = 0x08;
    public const ushort Enter = 0x0D;
    public const ushort Escape = 0x1B;
    public const ushort Delete = 0x2E;

    public const ushort D0 = 0x30;
    public const ushort A = 0x41;
    public const ushort F1 = 0x70;

    public static ushort Digit(int n) => (ushort)(D0 + n);
    public static ushort Letter(char c) => (ushort)(A + (char.ToUpperInvariant(c) - 'A'));
    public static ushort Function(int n) => (ushort)(F1 + n - 1);
}

/// <summary>
/// R-14: 現行の TACKEY.KFM 全 65 件を初期キーマップとして組み込む。
/// スコープ外の 6 件（F=内蔵検索 / P=圧縮 / U=書庫復元 / Y=絞込み / F2=CTRL+キー実行 / F10=サブ関連付け）は
/// 未割り当てとする（X-01・X-03）。
/// 結果: ReTAC では Esc を割り当て枠に含めないため、修飾なし・Shift の 64 枠のうち 39 枠が有効、25 枠が空。
/// Ctrl の 48 枠（F-06）には、Ctrl+F（B-18）を除いて既定の割り当てを付けない。
/// B-17: マウスの 3 枠（R-73）はボタン4/5 の 2 枠が有効。ボタン3 は空。
/// F-01: E / Shift+Enter / V / Z / F3 は、初期登録の外部ツール（<see cref="DefaultExternalTools"/>）を指す。
/// </summary>
public static class DefaultKeyMap
{
    public static KeyMap Create() => new(Bindings());

    private static IEnumerable<KeyValuePair<KeyBinding, CommandTarget>> Bindings()
    {
        // 英字キー（B / F / P / U / Y は未割り当て）
        yield return Letter('A', CommandId.ChangeAttributes);
        yield return Letter('C', CommandId.CopyToFolder);
        yield return Letter('D', CommandId.Delete);
        yield return Tool(new KeyBinding(Vk.Letter('E')), DefaultExternalTools.EditorId);
        yield return Letter('G', CommandId.ShowPopupMenu);
        yield return Letter('H', CommandId.FolderHistory);
        yield return Letter('I', CommandId.CopyFileName);
        yield return Letter('J', CommandId.QuickAccess);
        yield return Letter('K', CommandId.CreateFolder);
        yield return Letter('L', CommandId.SelectDrive);
        yield return Letter('M', CommandId.MoveToFolder);
        yield return Letter('N', CommandId.Rename);
        yield return Letter('O', CommandId.CreateShortcut);
        yield return Letter('Q', CommandId.Quit);
        yield return Letter('R', CommandId.ShowProperties);
        yield return Letter('S', CommandId.SortSettings);
        yield return Letter('T', CommandId.DirectJump);
        yield return Tool(new KeyBinding(Vk.Letter('V')), DefaultExternalTools.ViewerId);
        yield return Letter('W', CommandId.Refresh);
        yield return Letter('X', CommandId.RunCommandLine);
        yield return Tool(new KeyBinding(Vk.Letter('Z')), DefaultExternalTools.TerminalId);

        // 数字キー: 0 はルートへ、1〜9 は数字キードライブ割当て
        yield return Key(new KeyBinding(Vk.Digit(0)), CommandId.GoRoot);
        for (var n = 1; n <= 9; n++)
            yield return Key(new KeyBinding(Vk.Digit(n)), CommandId.DriveByNumberKey);

        // ファンクションキー（F1 / F2 / F4 / F6 / F7 / F10〜F12 は未割り当て）
        yield return Tool(new KeyBinding(Vk.Function(3)), DefaultExternalTools.TerminalId);
        yield return Key(new KeyBinding(Vk.Function(5)), CommandId.Refresh);
        yield return Key(new KeyBinding(Vk.Function(8)), CommandId.GoBack);
        yield return Key(new KeyBinding(Vk.Function(9)), CommandId.GoForward);

        // その他（Esc は未割り当て。R-18 により常に「中止」として働く）
        yield return Key(new KeyBinding(Vk.Enter), CommandId.OpenFile);
        yield return Tool(new KeyBinding(Vk.Enter, Shift: true), DefaultExternalTools.EditorId);
        yield return Key(new KeyBinding(Vk.Back), CommandId.GoParent);
        yield return Key(new KeyBinding(Vk.Delete), CommandId.ToggleAllMarks);

        // B-17 / R-73: マウスのサイドボタン。エクスプローラー・ブラウザを含め Windows 全体で
        // 「戻る」「進む」が標準なので、INV-NO-PREFERENCE-DEFAULTS の例外条項に当たる。
        // ホイール押し込み（ボタン3）は標準の挙動がアプリごとに違うため空のままにする
        yield return Key(new KeyBinding(Vk.XButton1), CommandId.GoBack);
        yield return Key(new KeyBinding(Vk.XButton2), CommandId.GoForward);

        // B-18: Ctrl+F は Windows 全体で「検索」の標準キー。INV-NO-PREFERENCE-DEFAULTS の例外条項に当たる。
        // Ctrl の枠で既定を持つのはこれだけ。便利だからという理由で Ctrl の枠に既定を足さない
        yield return Key(new KeyBinding(Vk.Letter('F'), Ctrl: true), CommandId.IncrementalSearch);
    }

    private static KeyValuePair<KeyBinding, CommandTarget> Letter(char c, CommandId id) =>
        Key(new KeyBinding(Vk.Letter(c)), id);

    private static KeyValuePair<KeyBinding, CommandTarget> Key(KeyBinding binding, CommandId id) =>
        new(binding, new BuiltinTarget(id));

    private static KeyValuePair<KeyBinding, CommandTarget> Tool(KeyBinding binding, int toolId) =>
        new(binding, new ToolTarget(toolId));
}
