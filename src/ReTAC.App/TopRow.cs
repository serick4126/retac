using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>Q9: 上部の行の幅の計算。副作用を持たせずテストする。</summary>
public static class TopRowLayout
{
    /// <summary>アドレスバーの最小幅（96 dpi）。</summary>
    public const int MinAddressWidth = 200;

    public static bool ShouldCompact(int rowWidth, int fullDriveWidth, int minAddressWidth) =>
        rowWidth - fullDriveWidth < minAddressWidth;
}

/// <summary>
/// R-86: ドライブバーとアドレスバーを 1 行に並べる。行の高さと、ドライブバーを縮めるか（Q9）はここで決める。
/// MainForm は表示の切り替えだけを伝え、高さを経路ごとに設定しない（DPI・フォント・表示するドライブの変更でも、ここで計算し直す）。
/// 子の Dock は使わない。Left / Fill だとドライブバーが自分で決めた高さを行の高さに引き伸ばされ、高さの計算が循環する。
/// </summary>
public sealed class TopRow : Control
{
    private readonly DriveBar _drive;
    private readonly AddressBar _address;
    // R-77: 子の Visible は親が表示されていないと false を返すので、判断には自分のフィールドを使う
    private bool _showDrive, _showAddress;
    private bool _laying;

    public TopRow(DriveBar drive, AddressBar address)
    {
        _drive = drive;
        _address = address;
        Dock = DockStyle.Top;
        drive.Dock = DockStyle.None;
        Controls.Add(drive);
        Controls.Add(address);
        drive.LayoutNeeded += (_, _) => Relayout();
        drive.SizeChanged += (_, _) => Relayout();
        address.SizeChanged += (_, _) => Relayout();
    }

    /// <summary>両方隠すなら行ごと隠す。Dock の順序は呼び出し側（MainForm.ArrangeDocks）が決め直す。</summary>
    public void SetParts(bool drive, bool address)
    {
        _showDrive = drive;
        _showAddress = address;
        _drive.Visible = drive;
        _address.Visible = address;
        Visible = drive || address;
        PerformLayout();
    }

    private void Relayout()
    {
        if (!_laying) PerformLayout();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_laying) return;
        _laying = true;
        try
        {
            var min = LogicalToDeviceUnits(TopRowLayout.MinAddressWidth);
            // 縮めた後の幅ではなく、文字ありの幅で決める（縮める・戻すを繰り返さないため）
            _drive.Compact = _showDrive && _showAddress && TopRowLayout.ShouldCompact(ClientSize.Width, _drive.FullWidth, min);

            var height = Math.Max(_showDrive ? _drive.Height : 0, _showAddress ? _address.BarHeight : 0);
            if (Height != height) Height = height;

            if (!_showDrive)
            {
                // 一方だけ表示なら、その部品が行の幅を使う（R-86）
                _address.SetBounds(0, (height - _address.BarHeight) / 2, ClientSize.Width, _address.BarHeight);
                return;
            }

            var driveWidth = _drive.PreferredWidth;
            _drive.SetBounds(0, (height - _drive.Height) / 2, driveWidth, _drive.Height);
            var x = driveWidth + LogicalToDeviceUnits(4);
            // Q9: それでも足りなければ最小幅のまま右端で切る
            _address.SetBounds(x, (height - _address.BarHeight) / 2, Math.Max(ClientSize.Width - x, min), _address.BarHeight);
        }
        finally
        {
            _laying = false;
        }
    }
}
