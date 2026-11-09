using ReTAC.Domain.Navigation;

namespace ReTAC.Domain.Tests;

/// <summary>R-147: パスの式の置き換え・式に書く・設定ファイルの印</summary>
public class PathExpressionTests
{
    // プロセスの環境変数は書き換えない（並列のテストに漏れる）。名前の大文字・小文字は区別しない辞書で作る
    private static Func<string, string?> Env(params (string Name, string Value)[] vars)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (n, v) in vars) d[n] = v;
        return name => d.TryGetValue(name, out var v) ? v : null;
    }

    [Theory]
    [InlineData(@"C:\%P32_A%\x", @"C:\val\x")]            // 定義済み
    [InlineData(@"C:\%p32_a%\x", @"C:\val\x")]            // 大文字・小文字は区別しない
    [InlineData(@"C:\%P32_NO%\x", @"C:\%P32_NO%\x")]      // 未定義は丸ごと文字
    [InlineData(@"100%%", "100%")]                        // %%
    [InlineData("100%", "100%")]                          // 対の無い %
    [InlineData(@"a%b%c%", @"a%b%c%")]                    // b は未定義。閉じの % の後から読むので最後の % は対が無い
    [InlineData("%NOSUCH%P32_A%", "%NOSUCH%P32_A%")]      // 閉じの % を次の組の始まりに使わない
    [InlineData("%P32_OUTER%", @"C:\x\%P32_INNER%")]      // 値の中の % はもう一度置き換えない
    [InlineData(@"%P32_A%%P32_A%", "valval")]
    [InlineData("%%P32_A%%", "%P32_A%")]                  // %% で書いた % は変数の囲みにならない
    [InlineData("", "")]
    public void Expand_ReadsFourShapes(string input, string expected)
    {
        var env = Env(("P32_A", "val"), ("P32_OUTER", @"C:\x\%P32_INNER%"), ("P32_INNER", "inner"));
        Assert.Equal(expected, PathExpression.Expand(input, env));
    }

    [Fact]
    public void Expand_UndefinedName_DoesNotShiftLaterReading()
    {
        // 正規の形の式（%% を含む）は、変数が未定義の環境でも %% の部分の読み方がずれない
        var defined = Env(("P32_V", "v"));
        var undefined = Env();
        const string expr = "%P32_V%%%a%%";
        Assert.Equal("v%a%", PathExpression.Expand(expr, defined));
        Assert.Equal("%P32_V%%a%", PathExpression.Expand(expr, undefined));
    }

    [Fact]
    public void Expand_ExpressionOverWhitespaceOnly_ProducesWhitespace()
    {
        // 空になる判定は解決の入口（ResolveSaved・PathResolver）。ここは置き換えた文字をそのまま返す
        var env = Env(("P32_E", ""), ("P32_SP", " \t"), ("P32_ZS", "\u3000"));
        Assert.Equal("", PathExpression.Expand("%P32_E%", env));
        Assert.Equal(" \t", PathExpression.Expand("%P32_SP%", env));
        Assert.Equal("\u3000", PathExpression.Expand("%P32_ZS%", env));
        Assert.Equal(" x ", PathExpression.Expand(" x ", env));   // 置き換えた結果の端の空白は落とさない
    }

    [Theory]
    [InlineData("|%P32_E%")]
    [InlineData("|%P32_SP%")]
    [InlineData("|%P32_E%%P32_SP%")]
    public void ResolveSaved_EmptyAfterExpand_IsUnusable(string stored)
    {
        Assert.Null(PathExpression.ResolveSaved(stored, Env(("P32_E", ""), ("P32_SP", " \t"))));
    }

    [Fact]
    public void ResolveSaved_FullWidthSpaceIsAName_NotEmpty()
    {
        var env = Env(("P32_ZS", "\u3000"));
        Assert.Equal(@"C:\work\" + "\u3000", PathExpression.ResolveSaved(@"|C:\work\%P32_ZS%", env));
        Assert.Equal(@"C:\work\" + "\u3000" + "x", PathExpression.ResolveSaved("C:\\work\\\u3000x", env));
    }

    [Fact]
    public void ResolveSaved_AbsoluteOnly()
    {
        var env = Env(("P32_ABS", @"C:\a\b"), ("P32_REL", "sub"), ("P32_E", ""));
        Assert.Equal(@"C:\a\b\c", PathExpression.ResolveSaved(@"|%P32_ABS%\c", env));
        Assert.Equal(@"C:\a\x", PathExpression.ResolveSaved(@"|%P32_ABS%\..\x%P32_E%", env));
        Assert.Null(PathExpression.ResolveSaved("|%P32_REL%", env));           // 相対
        Assert.Null(PathExpression.ResolveSaved("sub", env));
        Assert.Null(PathExpression.ResolveSaved(@"|%P32_NOSUCH%\x", env));      // 未定義 → 相対になる
        Assert.Null(PathExpression.ResolveSaved(@"C:\a|b", env));               // 無効な名前
        Assert.Null(PathExpression.ResolveSaved(@"|%P32_ABS%\a|%P32_E%", env));
        Assert.Equal(@"C:\work", PathExpression.ResolveSaved(@"C:\work", env));
    }

    [Fact]
    public void ResolveSaved_LegacyValue_IsUnusable()
    {
        var env = Env(("P32_DEST", "other"));
        Assert.Null(PathExpression.ResolveSaved(@"C:\work\%P32_DEST%", env));   // 旧設定の値（印なし・% が 2 つ）
        Assert.Null(PathExpression.ResolveSaved(@"C:\work\100%%", env));
        Assert.Equal(@"C:\work\other", PathExpression.ResolveSaved(@"|C:\work\%P32_DEST%", env));   // 陽性対照
        Assert.Equal(@"C:\work\100%", PathExpression.ResolveSaved(@"|C:\work\100%%", env));
        Assert.Equal(@"C:\work\100%", PathExpression.ResolveSaved(@"C:\work\100%", env));          // % が 1 つなら今までどおり
    }

    // 3 回目のレビューの反例: 名前 %P32.a% のファイルの基底名 %P32 と拡張子 .a%
    [Fact]
    public void Write_DecidesOverWholeSequence_NotPerPart()
    {
        var env = Env(("P32.a", "other"));
        PathPart[] parts = [PathPart.Value(@"C:\work"), PathPart.Expression(@"\"), PathPart.Value("%P32"), PathPart.Value(".a%")];
        var written = PathExpression.Write(parts, env);
        Assert.Equal(@"C:\work\%P32.a%", PathExpression.Expand(written, env));

        // 部品ごとに書いて連結すると変数の形になり、別の意味になる（落とし穴の実演）
        var perPart = PathExpression.Literal(@"C:\work") + @"\" + PathExpression.Literal("%P32") + PathExpression.Literal(".a%");
        Assert.Equal(@"C:\work\other", PathExpression.Expand(perPart, env));
    }

    // 規則の理由: Windows と同じ「閉じの % から読み続ける」だと、式の読み方が環境で変わる
    [Fact]
    public void WindowsStyleResume_DiffersFromSpec()
    {
        static string WindowsStyle(string text, Func<string, string?> env)
        {
            var sb = new System.Text.StringBuilder();
            var i = 0;
            while (i < text.Length)
            {
                if (text[i] != '%') { sb.Append(text[i++]); continue; }
                var close = text.IndexOf('%', i + 1);
                if (close < 0) { sb.Append(text, i, text.Length - i); break; }
                var v = close == i + 1 ? "%" : env(text.Substring(i + 1, close - i - 1));
                if (v is not null) { sb.Append(v); i = close + 1; }
                else { sb.Append(text, i, close - i); i = close; }   // 閉じの % を次の始まりに使う
            }
            return sb.ToString();
        }

        var env = Env(("P32_T", "T"));
        const string input = "%NOSUCH%P32_T%";
        Assert.Equal("%NOSUCHT", WindowsStyle(input, env));
        Assert.Equal(input, PathExpression.Expand(input, env));

        // 未定義の変数の直後の % は、仕様では対の無い % としてそのまま。Windows 方式だと %% に読まれて消える
        Assert.Equal("%P32_X%%", PathExpression.Expand("%P32_X%%", Env()));
        Assert.Equal("%P32_X%", WindowsStyle("%P32_X%%", Env()));
    }

    [Theory]
    [InlineData(@"C:\work")]
    [InlineData(@"C:\100%")]
    [InlineData("notepad.exe")]
    public void Literal_PlainPath_IsUnchanged(string value)
    {
        Assert.Equal(value, PathExpression.Literal(value));
    }

    [Fact]
    public void Literal_DoublesAllPercents_WhenTwoOrMore()
    {
        Assert.Equal(@"D:\%%TEMP%%", PathExpression.Literal(@"D:\%TEMP%"));
        var env = Env(("TEMP", @"C:\t"));
        Assert.Equal(@"D:\%TEMP%", PathExpression.Expand(PathExpression.Literal(@"D:\%TEMP%"), env));
    }

    [Theory]
    [InlineData(@"%USERPROFILE%\Downloads", @"%USERPROFILE%\Downloads")]
    [InlineData(@"C:\work", @"C:\work")]
    [InlineData(@"C:\%NOSUCH%", @"C:\%%NOSUCH%%")]
    [InlineData(@"C:\work\100%%", @"C:\work\100%")]   // %% が文字の % 1 つ → 変数無し・% が 1 つ → そのまま
    public void Canonical_Examples(string input, string expected)
    {
        Assert.Equal(expected, PathExpression.Canonical(input, Env(("USERPROFILE", @"C:\Users\u"))));
    }

    [Fact]
    public void Canonical_KeepsMeaningWhenNameIsDefinedLater()
    {
        var before = Env();
        var canonical = PathExpression.Canonical(@"C:\%P32_LATER%", before);
        var after = Env(("P32_LATER", "x"));
        Assert.Equal(@"C:\%P32_LATER%", PathExpression.Expand(canonical, after));
    }

    [Fact]
    public void Store_Read_Display_Marker()
    {
        Assert.Equal(@"|%SystemRoot%\notepad.exe", PathExpression.Store(@"%SystemRoot%\notepad.exe"));
        Assert.Equal(@"C:\work", PathExpression.Store(@"C:\work"));
        Assert.Equal("notepad.exe", PathExpression.Store("notepad.exe"));
        Assert.Equal(@"C:\100%", PathExpression.Store(@"C:\100%"));
        Assert.Equal(@"|C:\100%%", PathExpression.Store(@"C:\100%%"));

        Assert.Equal(@"%SystemRoot%\notepad.exe", PathExpression.Read(@"|%SystemRoot%\notepad.exe"));
        Assert.Equal(@"C:\work", PathExpression.Read(@"C:\work"));
        Assert.Equal(@"C:\100%", PathExpression.Read(@"C:\100%"));
        Assert.Null(PathExpression.Read(@"C:\work\%P32_DEST%"));   // 旧設定の値
        Assert.Null(PathExpression.Read(@"C:\work\100%%"));

        Assert.Equal(@"%SystemRoot%\notepad.exe", PathExpression.Display(@"|%SystemRoot%\notepad.exe"));
        Assert.Equal(@"C:\work\%P32_DEST%", PathExpression.Display(@"C:\work\%P32_DEST%"));   // 旧設定の値はそのまま
        Assert.Equal(@"C:\work", PathExpression.Display(@"C:\work"));
    }

    // 生成した部品の並びと環境で、書いた式の性質を数千件確かめる
    [Fact]
    public void Write_Properties_HoldForGeneratedParts()
    {
        var rng = new Random(32);
        string[] names = ["P32_A", "A.B", "P32_C", "P32_D"];
        const string alphabet = "%%%ab.\\ ";
        string Str(int max) => new(Enumerable.Range(0, rng.Next(0, max + 1)).Select(_ => alphabet[rng.Next(alphabet.Length)]).ToArray());
        string Frag()
        {
            // 変数の形（定義済み・未定義・大文字小文字違い）を地の文字に混ぜる
            var sb = new System.Text.StringBuilder(Str(5));
            if (rng.Next(3) == 0) sb.Append('%').Append(names[rng.Next(names.Length)]).Append('%');
            if (rng.Next(4) == 0) sb.Append('%').Append(names[rng.Next(names.Length)].ToLowerInvariant()).Append('%');
            sb.Append(Str(5));
            return sb.ToString();
        }

        for (var n = 0; n < 4000; n++)
        {
            var vars = new List<(string, string)>();
            foreach (var name in names)
                if (rng.Next(2) == 0) vars.Add((name, Str(4)));
            var env = Env([.. vars]);

            var parts = Enumerable.Range(0, rng.Next(0, 5))
                .Select(_ => rng.Next(2) == 0 ? PathPart.Expression(Frag()) : PathPart.Value(Frag()))
                .ToArray();
            var expected = string.Concat(parts.Select(p => p.IsExpression ? PathExpression.Expand(p.Text, env) : p.Text));
            var written = PathExpression.Write(parts, env);
            var ctx = $"parts=[{string.Join(" | ", parts.Select(p => (p.IsExpression ? "E:" : "V:") + p.Text))}] vars=[{string.Join(",", vars)}] written={written}";

            // 書いた式を置き換えると、部品ごとの意味を並べたものになる
            Assert.True(expected == PathExpression.Expand(written, env), ctx);

            // 正規の形にしても変わらない
            Assert.True(written == PathExpression.Canonical(written, env), ctx);

            // 後で名前を定義しても（値は別でも）意味が変わらない
            var extra = names.Where(nm => env(nm) is null).Select(nm => (nm, @"X%" + nm + "%")).ToArray();
            Assert.True(expected == PathExpression.Expand(written, Env([.. vars, .. extra])), ctx);

            // 印が往復する
            Assert.True(written == PathExpression.Read(PathExpression.Store(written)), ctx);
            Assert.True(written == PathExpression.Display(PathExpression.Store(written)), ctx);

            // 文字の値 1 つを書いて置き換えると元に戻る
            var v = Frag();
            Assert.True(v == PathExpression.Expand(PathExpression.Literal(v), env), ctx);
        }
    }
}
