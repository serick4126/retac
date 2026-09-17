using System.Drawing;
using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>
/// R-77: ドライブバーを非表示にしているときの `L`。ドライブバーだけを載せた枠なしのモーダル。
/// DriveBar がキー操作を部品の中で完結させているので、選ばれた・取り消されたの通知で閉じるだけでよい。
/// 本体のドライブバーを付け替えず、別のインスタンスを作る（親を移すとレイアウトとイベントの付け外しが絡む）。
/// </summary>
internal sealed class DriveSelectForm : Form
{
    private readonly DriveBar _bar = new() { AllowDrop = false };
    private bool _decided;

    private string? SelectedPath { get; set; }

    private DriveSelectForm(IReadOnlySet<char> hiddenDrives, bool showDesktop, string currentFolder)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        // 枠なしだと背景との境目が見えない。余白 1px に背景色を見せて枠線の代わりにする
        Padding = new Padding(1);
        BackColor = SystemColors.ControlDark;

        _bar.SetVisibility(hiddenDrives, showDesktop);
        // 大きさは Dock を変える前に読む。Fill にするとフォームの大きさで上書きされる
        ClientSize = new Size(_bar.PreferredWidth + 2, _bar.Height + 2);
        _bar.Dock = DockStyle.Fill;
        Controls.Add(_bar);

        _bar.PathSelected += (_, path) => Finish(path);
        _bar.Cancelled += (_, _) => Finish(null);
        // 右クリックのシェルのメニューは出さない。メニューの追跡と Deactivate の順序で閉じ方が不安定になる
        // Focus は表示前には効かない
        Shown += (_, _) => _bar.EnterKeyboardSelection(currentFolder);
        // 外をクリックした・別のウィンドウへ切り替えた
        Deactivate += (_, _) => Finish(null);
    }

    /// <summary>①② で閉じる途中にも Deactivate が来る。最初に決まった結果だけを採る。</summary>
    private void Finish(string? path)
    {
        if (_decided) return;
        _decided = true;
        SelectedPath = path;
        Close();
    }

    /// <returns>選ばれたパス。取り消されたら null</returns>
    public static string? Pick(IWin32Window owner, Control center, IReadOnlySet<char> hiddenDrives,
                               bool showDesktop, string currentFolder)
    {
        using var form = new DriveSelectForm(hiddenDrives, showDesktop, currentFolder);
        var area = center.RectangleToScreen(center.ClientRectangle);
        var bounds = new Rectangle(
            area.X + (area.Width - form.Width) / 2,
            area.Y + (area.Height - form.Height) / 2,
            form.Width, form.Height);
        var screen = Screen.FromControl(center).WorkingArea;
        bounds.X = Math.Clamp(bounds.X, screen.Left, Math.Max(screen.Left, screen.Right - bounds.Width));
        bounds.Y = Math.Clamp(bounds.Y, screen.Top, Math.Max(screen.Top, screen.Bottom - bounds.Height));
        form.Bounds = bounds;
        form.ShowDialog(owner);
        return form.SelectedPath;
    }
}
