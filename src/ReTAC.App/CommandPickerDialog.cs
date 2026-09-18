using System.Drawing;
using System.Windows.Forms;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Tools;

namespace ReTAC.App;

/// <summary>
/// R-89 / R-92: クイックアクセス・ブックマークに登録するコマンドを選ぶ。一覧はキー割り当ての画面と同じ（CommandCatalog）。
/// </summary>
public sealed class CommandPickerDialog : Form
{
    private readonly ListView _commands = new()
    {
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
        ShowGroups = true,
        Bounds = new Rectangle(12, 42, 410, 354),
    };
    private readonly TextBox _filter = new()
    {
        Bounds = new Rectangle(12, 12, 410, 23),
        PlaceholderText = "絞り込み（名前の一部を入力。「コピー」「外部ツール」など）",
    };

    private CommandPickerDialog(IReadOnlyList<ExternalTool> tools)
    {
        Text = "コマンドを追加";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(434, 446);

        _commands.Columns.Add("コマンド", 380 * DeviceDpi / 96);   // 列幅は AutoScaleMode.Dpi の対象外（R-66）
        CommandCatalog.Fill(_commands, "", tools, includeUnassigned: false);
        _filter.TextChanged += (_, _) => CommandCatalog.Fill(_commands, _filter.Text, tools, includeUnassigned: false);
        _commands.DoubleClick += (_, _) => { if (Selected is not null) { DialogResult = DialogResult.OK; Close(); } };

        var ok = new Button { Text = "追加", DialogResult = DialogResult.OK, Bounds = new Rectangle(236, 406, 90, 28) };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(332, 406, 90, 28) };
        Controls.AddRange([_filter, _commands, ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;

        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    private CommandTarget? Selected => _commands.SelectedItems.Count > 0 ? _commands.SelectedItems[0].Tag as CommandTarget : null;

    /// <returns>選んだコマンド。キャンセル・未選択なら null</returns>
    public static CommandTarget? Pick(IWin32Window owner, IReadOnlyList<ExternalTool> tools)
    {
        using var dialog = new CommandPickerDialog(tools);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Selected : null;
    }
}
