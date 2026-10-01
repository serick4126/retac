using System.Drawing;
using System.Windows.Forms;
using ReTAC.Domain.Navigation;
using ReTAC.Domain.Tools;

namespace ReTAC.App;

/// <summary>入力ダイアログの引数の 1 行（固定の引数・マクロを含む引数）を入れる（R-133）。</summary>
public sealed class PromptArgumentDialog : Form
{
    private readonly PromptArgumentKind _kind;
    private readonly TextBox _input = new() { Bounds = new Rectangle(14, 34, 330, 23) };

    public PromptArgumentDialog(PromptArgumentKind kind, string text)
    {
        _kind = kind;
        var fixedValue = kind == PromptArgumentKind.Fixed;
        Text = fixedValue ? "固定の引数" : "マクロを含む引数";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;

        _input.Text = text;
        Controls.Add(new Label { Text = fixedValue ? "値(&V):" : "引数(&V):", AutoSize = true, Location = new Point(14, 14) });
        Controls.Add(_input);
        Controls.Add(new Label
        {
            Text = fixedValue ? "1 行が 1 つの引数です。空白も含めてそのまま送ります。" : "引数欄と同じ書き方です。${prompt} は書けません。",
            AutoSize = true, Location = new Point(14, 64), UseMnemonic = false,
        });
        if (!fixedValue)
        {
            var macros = new Button { Text = "マクロ(&R)...", Bounds = new Rectangle(352, 33, 92, 26) };
            macros.Click += (_, _) => InsertMacro();
            Controls.Add(macros);
        }

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(258, 92, 90, 28) };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(354, 92, 90, 28) };
        Controls.AddRange([ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;
        ClientSize = new Size(458, 134);

        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK || Problem() is not { } message) return;
            MessageBox.Show(this, message, "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);   // R-48
            e.Cancel = true;
            _input.Focus();
        };
        Shown += (_, _) => { _input.Focus(); _input.SelectAll(); };

        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    /// <summary>入れた文字。固定の引数は空白も含めてそのまま（空でもよい）。</summary>
    public string Value => _input.Text;

    private string? Problem()
    {
        if (_kind == PromptArgumentKind.Fixed) return null;
        if (InputText.TrimEdge(_input.Text).Length == 0) return "引数を入れてください。";
        var template = ArgumentTemplate.Parse(_input.Text);
        if (template.HasPrompt) return "${prompt} は書けません。";
        return template.Errors.FirstOrDefault()?.Message;
    }

    private void InsertMacro()
    {
        using var dialog = new MacroReferenceDialog(excludePrompt: true);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is not { Insert: { } insert } entry) return;
        var at = _input.SelectionStart;
        _input.SelectedText = insert;
        _input.Focus();
        _input.SelectionStart = at + entry.CaretOffset;
        _input.SelectionLength = 0;
    }
}
