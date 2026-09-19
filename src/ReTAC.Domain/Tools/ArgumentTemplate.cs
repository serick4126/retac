using System.Text;

namespace ReTAC.Domain.Tools;

/// <summary>外部ツールの引数に書けるマクロ（F-02）。名前は VS Code の変数に合わせる。</summary>
public enum MacroName
{
    File,
    Path,
    FileBasenameNoExtension,
    FileExtname,
    CursorFile,
    CursorPath,
    Cwd,
    Prompt,
}

public abstract record TemplatePart;

public sealed record LiteralPart(string Text) : TemplatePart;

/// <param name="Required">直後の <c>!</c>。空になったらツールを起動しない</param>
/// <param name="PromptIndex"><c>${prompt}</c> のとき、何番目の入力か。それ以外は -1</param>
public sealed record MacroPart(MacroName Name, bool Required, int PromptIndex = -1) : TemplatePart;

/// <param name="Quoted">引用符を含んでいた。マクロが空になっても <c>""</c> として残す</param>
public sealed record TemplateArgument(IReadOnlyList<TemplatePart> Parts, bool Quoted);

/// <param name="Title">空ならツールの名前をタイトルにする</param>
public sealed record PromptRequest(string Title, string Default);

public sealed record TemplateError(int Position, string Message);

/// <summary>
/// 外部ツールの引数の文字列を解析したもの（F-02 / R-71）。
/// <b>引数を区切ってから展開する</b>ので、展開した値に空白があっても 1 つの引数のまま渡る。
/// 知らない名前は誤りにする。文字どおり渡すと、後で同じ名前のマクロを足した瞬間に既存の設定の意味が変わる（F-02）。
/// </summary>
public sealed class ArgumentTemplate
{
    public static IReadOnlyDictionary<string, MacroName> Names { get; } = new Dictionary<string, MacroName>(StringComparer.Ordinal)
    {
        ["file"] = MacroName.File,
        ["path"] = MacroName.Path,
        ["fileBasenameNoExtension"] = MacroName.FileBasenameNoExtension,
        ["fileExtname"] = MacroName.FileExtname,
        ["cursorFile"] = MacroName.CursorFile,
        ["cursorPath"] = MacroName.CursorPath,
        ["cwd"] = MacroName.Cwd,
        ["prompt"] = MacroName.Prompt,
    };

    private ArgumentTemplate(List<TemplateArgument> arguments, List<PromptRequest> prompts, List<TemplateError> errors)
    {
        Arguments = arguments;
        Prompts = prompts;
        Errors = errors;
    }

    public IReadOnlyList<TemplateArgument> Arguments { get; }
    public IReadOnlyList<PromptRequest> Prompts { get; }
    public IReadOnlyList<TemplateError> Errors { get; }
    public bool IsValid => Errors.Count == 0;

    /// <summary><c>${file}</c> と同じ対象（実効対象のファイル）から値を取るマクロ。</summary>
    public static bool IsFileFamily(MacroName name) =>
        name is MacroName.File or MacroName.FileBasenameNoExtension or MacroName.FileExtname;

    public static ArgumentTemplate Parse(string text) => new Parser(text).Run();

    private sealed class Parser(string text)
    {
        private readonly List<TemplateArgument> _arguments = [];
        private readonly List<PromptRequest> _prompts = [];
        private readonly List<TemplateError> _errors = [];
        private readonly List<TemplatePart> _parts = [];
        private readonly StringBuilder _literal = new();
        private bool _started;
        private bool _quoted;
        private int _argumentStart;

        public ArgumentTemplate Run()
        {
            var inQuote = false;
            var i = 0;
            while (i < text.Length)
            {
                var c = text[i];
                if (c == '"')
                {
                    Begin(i);
                    inQuote = !inQuote;
                    _quoted = true;
                    i++;
                    continue;
                }
                // Windows のコマンドラインの区切りは半角空白とタブだけ。全角空白は名前の一部
                if (!inQuote && (c is ' ' or '\t'))
                {
                    Flush();
                    i++;
                    continue;
                }

                Begin(i);
                if (c == '$' && Next(i) == '$') { _literal.Append('$'); i += 2; continue; }
                if (c == '$' && Next(i) == '{') { i = ReadMacro(i); continue; }
                _literal.Append(c);
                i++;
            }

            if (inQuote) _errors.Add(new TemplateError(text.Length, "引用符（\"）が閉じていません。"));
            Flush();
            return new ArgumentTemplate(_arguments, _prompts, _errors);
        }

        private char Next(int i) => i + 1 < text.Length ? text[i + 1] : '\0';

        private void Begin(int i)
        {
            if (_started) return;
            _started = true;
            _argumentStart = i;
        }

        /// <returns>マクロの次の位置</returns>
        private int ReadMacro(int start)
        {
            var close = text.IndexOf('}', start + 2);
            if (close < 0)
            {
                _errors.Add(new TemplateError(start, "「${」が「}」で閉じていません。"));
                return text.Length;
            }

            var body = text[(start + 2)..close];
            var colon = body.IndexOf(':');
            var name = colon < 0 ? body : body[..colon];
            var next = close + 1;

            if (!Names.TryGetValue(name, out var macro))
            {
                _errors.Add(new TemplateError(start, $"「${{{body}}}」というマクロはありません。"));
                return next;
            }
            if (macro != MacroName.Prompt && colon >= 0)
            {
                _errors.Add(new TemplateError(start, $"「${{{name}}}」には「:」を付けられません。"));
                return next;
            }

            var promptIndex = -1;
            if (macro == MacroName.Prompt)
            {
                // 既定値は区切り記号ではなく括弧で分ける。Everything の検索文字列で「|」を使うため
                var defaultValue = "";
                if (next < text.Length && text[next] == '{')
                {
                    var end = text.IndexOf('}', next + 1);
                    if (end < 0)
                    {
                        _errors.Add(new TemplateError(next, "既定値の「{」が「}」で閉じていません。"));
                        return text.Length;
                    }
                    defaultValue = text[(next + 1)..end];
                    next = end + 1;
                }
                promptIndex = _prompts.Count;
                _prompts.Add(new PromptRequest(colon < 0 ? "" : body[(colon + 1)..], defaultValue));
            }

            var required = next < text.Length && text[next] == '!';
            if (required) next++;

            FlushLiteral();
            _parts.Add(new MacroPart(macro, required, promptIndex));
            return next;
        }

        private void FlushLiteral()
        {
            if (_literal.Length == 0) return;
            _parts.Add(new LiteralPart(_literal.ToString()));
            _literal.Clear();
        }

        private void Flush()
        {
            if (!_started) return;
            FlushLiteral();

            // ${path} と ${file} 系は件数が違いうる（フォルダの有無）。1 つの引数の中で対応を取れない
            var names = _parts.OfType<MacroPart>().Select(p => p.Name).ToList();
            if (names.Contains(MacroName.Path) && names.Any(IsFileFamily))
                _errors.Add(new TemplateError(_argumentStart,
                    "1 つの引数に ${path} と ${file} 系（${file} ${fileBasenameNoExtension} ${fileExtname}）を混ぜられません。"));

            _arguments.Add(new TemplateArgument([.. _parts], _quoted));
            _parts.Clear();
            _started = false;
            _quoted = false;
        }
    }
}
