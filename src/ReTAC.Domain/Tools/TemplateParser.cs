using System.Text;

namespace ReTAC.Domain.Tools;

internal enum TemplateMode { Arguments, Scalar }

// R-136: マクロの読取りは共有し、初期値では空白と引用符を文字として保持する。
internal static class TemplateParser
{
    internal static (IReadOnlyList<TemplateArgument> Arguments, IReadOnlyList<TemplateError> Errors) Parse(string text, TemplateMode mode) =>
        new Parser(text, mode).Run();

    private sealed class Parser(string text, TemplateMode mode)
    {
        private readonly List<TemplateArgument> _arguments = [];
        private int _promptCount;
        private readonly List<TemplateError> _errors = [];
        private readonly List<TemplatePart> _parts = [];
        private readonly StringBuilder _literal = new();
        private bool _started;
        private bool _quoted;
        private int _argumentStart;

        public (IReadOnlyList<TemplateArgument> Arguments, IReadOnlyList<TemplateError> Errors) Run()
        {
            if (mode == TemplateMode.Scalar) Begin(0);
            var inQuote = false;
            var i = 0;
            while (i < text.Length)
            {
                var c = text[i];
                if (mode == TemplateMode.Arguments && c == '"')
                {
                    Begin(i);
                    inQuote = !inQuote;
                    _quoted = true;
                    i++;
                    continue;
                }
                // Windows のコマンドラインの区切りは半角空白とタブだけ。全角空白は名前の一部
                if (mode == TemplateMode.Arguments && !inQuote && (c is ' ' or '\t'))
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
            return (_arguments, _errors);
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

            if (!ArgumentTemplate.Names.TryGetValue(name, out var macro))
            {
                _errors.Add(new TemplateError(start, $"「${{{body}}}」というマクロはありません。"));
                return next;
            }
            if (macro == MacroName.Prompt) return ReadPrompt(start, colon, next);
            if (colon >= 0)
            {
                _errors.Add(new TemplateError(start, $"「${{{name}}}」には「:」を付けられません。"));
                return next;
            }

            var required = next < text.Length && text[next] == '!';
            if (required) next++;

            FlushLiteral();
            _parts.Add(new MacroPart(macro, required));
            return next;
        }

        /// <summary>
        /// R-130: <c>${prompt}</c> は引数欄に 1 つだけ、それだけで 1 つの引数として書く。項目・必須・既定値は入力ダイアログの定義で持つ。
        /// 今の <c>${prompt:タイトル}{既定値}</c> は読み替えずに誤りにする（設定は移行しない）。
        /// </summary>
        private int ReadPrompt(int start, int colon, int next)
        {
            if (mode == TemplateMode.Scalar)
                _errors.Add(new TemplateError(start, "この欄では ${prompt} は使えません。"));
            FlushLiteral();
            _parts.Add(new MacroPart(MacroName.Prompt, Required: false));
            if (colon >= 0)
                _errors.Add(new TemplateError(start, "「${prompt:…}」の書き方は使えません。入力ダイアログ編集で設定してください。"));
            if (++_promptCount == 2)
                _errors.Add(new TemplateError(start, "${prompt} は 1 つだけ書けます。"));
            if (next < text.Length && text[next] == '{')
            {
                _errors.Add(new TemplateError(next, "${prompt} の後ろに既定値は書けません。入力ダイアログ編集で設定してください。"));
                var end = text.IndexOf('}', next + 1);
                return end < 0 ? text.Length : end + 1;
            }
            if (next < text.Length && text[next] == '!')
            {
                _errors.Add(new TemplateError(next, "${prompt} に「!」は付けられません。必須は入力ダイアログ編集で設定してください。"));
                return next + 1;
            }
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
            if (mode == TemplateMode.Arguments && names.Any(MacroValues.IsPathFamily) && names.Any(MacroValues.IsFileFamily))
                _errors.Add(new TemplateError(_argumentStart,
                    "1 つの引数に ${path} 系と ${file} 系を混ぜられません。"));

            // R-130: ${prompt} は定義の引数の並びに置き換わるので、ほかの文字とつなげられない
            if (mode == TemplateMode.Arguments && _parts.Any(p => p is MacroPart { Name: MacroName.Prompt }) && (_parts.Count != 1 || _quoted))
                _errors.Add(new TemplateError(_argumentStart,
                    "${prompt} は、前後に文字をつなげず、引用符で囲まずに、それだけで 1 つの引数として書いてください。"));

            _arguments.Add(new TemplateArgument([.. _parts], _quoted));
            _parts.Clear();
            _started = false;
            _quoted = false;
        }
    }
}
