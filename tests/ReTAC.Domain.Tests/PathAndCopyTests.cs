using ReTAC.Domain.Commands;
using ReTAC.Domain.FileOps;
using ReTAC.Domain.Keys;
using ReTAC.Domain.Navigation;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

/// <summary>§12.1「相対パス解決」「複写条件」＋ キーマップ</summary>
public class PathResolverTests
{
    [Theory]
    [InlineData(@"C:\a\b\c", "..", @"C:\a\b")]
    [InlineData(@"C:\a\b\c", @"..\..", @"C:\a")]
    [InlineData(@"C:\a\b\c", "sub", @"C:\a\b\c\sub")]
    [InlineData(@"C:\a\b\c", @"..\other", @"C:\a\b\other")]
    [InlineData(@"C:\a\b\c", "../other", @"C:\a\b\other")]
    [InlineData(@"C:\a", "..", @"C:\")]
    [InlineData(@"C:\a\b", @".\x\.\y", @"C:\a\b\x\y")]
    public void 相対パスはカレントフォルダ基準で解決される(string current, string input, string expected)
    {
        Assert.Equal(expected, PathResolver.Resolve(current, input));
    }

    [Theory]
    [InlineData(@"C:\", "..")]
    [InlineData(@"C:\a", @"..\..")]
    [InlineData(@"C:\a\b", @"..\..\..")]
    public void ドライブルートを超える相対パスは無効である(string current, string input)
    {
        // R-61-3
        Assert.Null(PathResolver.Resolve(current, input));
    }

    [Theory]
    [InlineData(@"C:", "C|D")]
    [InlineData(@"C:", "a?b")]
    [InlineData(@"C:", "\"quoted\"")]
    public void 打てるが解決できない入力は例外ではなく無効として返る(string current, string input)
    {
        // 呼び出し側（宛先入力欄）は catch していない。投げるとアプリごと落ちる
        Assert.Null(PathResolver.Resolve(current, input));
    }

    [Fact]
    public void 長すぎる入力でも落ちない()
    {
        // 長さそのものは無効ではない。転送を試みた時点で OS が IOException で断る（そこは catch 済み）
        var exception = Record.Exception(() => PathResolver.Resolve(@"C:", new string('x', 40000)));
        Assert.Null(exception);
    }

    [Fact]
    public void 絶対パスはそのまま採られドライブをまたげる()
    {
        // R-52-4: 宛先を明示的に指定する操作は R-39 の対象外
        Assert.Equal(@"D:\dest", PathResolver.Resolve(@"C:\a\b", @"D:\dest"));
    }

    [Fact]
    public void 空欄は解決できない()
    {
        // R-63 / R-63-2 の判定は呼び出し側が行う
        Assert.Null(PathResolver.Resolve(@"C:\a", "   "));
    }

    [Fact]
    public void 末尾に全角空白を持つ名前が宛先として解決できる()
    {
        // B-01: string.Trim() が U+3000 を落としていた
        var resolved = PathResolver.Resolve(@"C:\work", "フォルダ　　");
        Assert.Equal(@"C:\work\フォルダ　　", resolved);
    }

    [Fact]
    public void 宛先の前後の半角空白は従来どおり落とす()
    {
        Assert.Equal(@"C:\work\sub", PathResolver.Resolve(@"C:\work", "  sub  "));
    }
}

public class ConflictResolverTests
{
    private static readonly DateTime Older = new(2026, 1, 1);
    private static readonly DateTime Newer = new(2026, 6, 1);

    [Fact]
    public void 既定の複写条件は新しい時に複写である()
    {
        // R-41-5
        Assert.Equal(CopyCondition.NewerOnly, ConflictResolver.Default);
    }

    [Fact]
    public void 衝突がなければ常に転送する()
    {
        var d = ConflictResolver.Decide(Older, null, CopyCondition.NewerOnly);
        Assert.True(d.Transfer);
        Assert.False(d.RenameTarget);
    }

    [Fact]
    public void 新しい時に複写はコピー元が新しいときだけ転送する()
    {
        // R-41-4: 差分更新の中核
        Assert.True(ConflictResolver.Decide(Newer, Older, CopyCondition.NewerOnly).Transfer);
        Assert.False(ConflictResolver.Decide(Older, Newer, CopyCondition.NewerOnly).Transfer);
        Assert.False(ConflictResolver.Decide(Older, Older, CopyCondition.NewerOnly).Transfer);
    }

    [Theory]
    [InlineData(CopyCondition.Overwrite, true, false)]
    [InlineData(CopyCondition.Skip, false, false)]
    [InlineData(CopyCondition.RenameCopy, true, true)]
    public void 各複写条件が衝突時に期待どおり判定する(CopyCondition condition, bool transfer, bool rename)
    {
        var d = ConflictResolver.Decide(Older, Newer, condition);
        Assert.Equal(transfer, d.Transfer);
        Assert.Equal(rename, d.RenameTarget);
    }
}

public class KeyMapTests
{
    private static readonly KeyMap Map = DefaultKeyMap.Create();

    [Theory]
    [InlineData('C', CommandId.CopyToFolder)]
    [InlineData('M', CommandId.MoveToFolder)]
    [InlineData('D', CommandId.Delete)]
    [InlineData('N', CommandId.Rename)]
    [InlineData('Q', CommandId.Quit)]
    [InlineData('I', CommandId.CopyFileName)]
    [InlineData('J', CommandId.QuickAccess)]
    public void 英字キーが卓駆と同じコマンドに解決される(char key, CommandId expected)
    {
        Assert.Equal(new BuiltinTarget(expected), Map.Resolve(new KeyBinding(Vk.Letter(key))));
    }

    [Fact]
    public void 外部ツールの既定のキーは初期登録の3件を指す()
    {
        // F-01
        Assert.Equal(new ToolTarget(DefaultExternalTools.EditorId), Map.Resolve(new KeyBinding(Vk.Letter('E'))));
        Assert.Equal(new ToolTarget(DefaultExternalTools.EditorId), Map.Resolve(new KeyBinding(Vk.Enter, Shift: true)));
        Assert.Equal(new ToolTarget(DefaultExternalTools.ViewerId), Map.Resolve(new KeyBinding(Vk.Letter('V'))));
        Assert.Equal(new ToolTarget(DefaultExternalTools.TerminalId), Map.Resolve(new KeyBinding(Vk.Letter('Z'))));
        Assert.Equal(new ToolTarget(DefaultExternalTools.TerminalId), Map.Resolve(new KeyBinding(Vk.Function(3))));
    }

    [Fact]
    public void Deleteは削除ではなく全選択全解除である()
    {
        // 5-2 節: 工場出荷時からの意図的な変更
        Assert.Equal(new BuiltinTarget(CommandId.ToggleAllMarks), Map.Resolve(new KeyBinding(Vk.Delete)));
        Assert.Equal(new BuiltinTarget(CommandId.Delete), Map.Resolve(new KeyBinding(Vk.Letter('D'))));
    }

    [Fact]
    public void Enterは関連付け実行である()
    {
        Assert.Equal(new BuiltinTarget(CommandId.OpenFile), Map.Resolve(new KeyBinding(Vk.Enter)));
    }

    [Fact]
    public void 数字キーは0がルートで1から9がドライブ割当てである()
    {
        Assert.Equal(new BuiltinTarget(CommandId.GoRoot), Map.Resolve(new KeyBinding(Vk.Digit(0))));
        for (var n = 1; n <= 9; n++)
            Assert.Equal(new BuiltinTarget(CommandId.DriveByNumberKey), Map.Resolve(new KeyBinding(Vk.Digit(n))));
    }

    [Theory]
    [InlineData('B')]   // 卓駆でも未割り当て
    [InlineData('F')]   // 内蔵検索はスコープ外
    [InlineData('P')]   // 書庫はスコープ外
    [InlineData('U')]   // 同上
    [InlineData('Y')]   // 表示ワイルドカード機構ごとスコープ外（S-19）
    public void スコープ外のキーは未割り当てである(char key)
    {
        Assert.Null(Map.Resolve(new KeyBinding(Vk.Letter(key))));
    }

    [Theory]
    [InlineData(2)]     // CTRL+キー実行の一覧は作らない（F-06）
    [InlineData(10)]    // サブ関連付け実行はスコープ外
    public void スコープ外のファンクションキーは未割り当てである(int n)
    {
        Assert.Null(Map.Resolve(new KeyBinding(Vk.Function(n))));
    }

    [Fact]
    public void 有効な割り当ては41件でCtrlには無い()
    {
        // 卓駆の 65 枠のうち、元から未割り当て 20 枠 ＋ スコープ外 6 枠を除いた 39 枠 ＋ マウスボタンの既定 2 枠（B-17）
        Assert.Equal(41, Map.Bindings.Count);
        Assert.DoesNotContain(Map.Bindings.Keys, key => key.Ctrl);
    }

    [Fact]
    public void 割り当ての変更と解除ができる()
    {
        // R-12 の経路は 1 つのまま、割り当てだけを差し替える
        var map = DefaultKeyMap.Create();
        var b = new KeyBinding(Vk.Letter('F'));
        map.Assign(b, new BuiltinTarget(CommandId.Refresh));
        Assert.Equal(new BuiltinTarget(CommandId.Refresh), map.Resolve(b));
        map.Assign(b, null);
        Assert.Null(map.Resolve(b));
    }

    [Fact]
    public void Ctrl付きのキーは修飾なしと別の枠である()
    {
        var map = DefaultKeyMap.Create();
        map.Assign(new KeyBinding(Vk.Letter('E'), Ctrl: true), new BuiltinTarget(CommandId.Refresh));

        Assert.Equal(new BuiltinTarget(CommandId.Refresh), map.Resolve(new KeyBinding(Vk.Letter('E'), Ctrl: true)));
        Assert.Equal(new ToolTarget(DefaultExternalTools.EditorId), map.Resolve(new KeyBinding(Vk.Letter('E'))));
    }

    [Fact]
    public void 削除したツールのキーは未割り当てに戻る()
    {
        var map = DefaultKeyMap.Create();
        map.ReleaseTool(DefaultExternalTools.EditorId);

        Assert.Null(map.Resolve(new KeyBinding(Vk.Letter('E'))));
        Assert.Null(map.Resolve(new KeyBinding(Vk.Enter, Shift: true)));
        Assert.Equal(new ToolTarget(DefaultExternalTools.ViewerId), map.Resolve(new KeyBinding(Vk.Letter('V'))));
    }
}
