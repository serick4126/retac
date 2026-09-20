using System.Windows.Forms;

namespace ReTAC.App;

public readonly record struct CentralDisplayWidths(int VisibleLeft, int PreservedLeft);

/// <summary>R-95 / R-96: 左パネルとファイル表示パネルの左右配置を中央領域だけで管理する。</summary>
public sealed class CentralDisplayArea : UserControl
{
    private const int LeftMinimumLogical = 160;
    private const int RightMinimumLogical = 320;
    private const int SplitterLogical = 4;

    private readonly SplitContainer _split = new()
    {
        Dock = DockStyle.Fill,
        FixedPanel = FixedPanel.None,
        IsSplitterFixed = false,
        Orientation = Orientation.Vertical,
        Panel1MinSize = 0,
        Panel2MinSize = 0,
        TabStop = false,
    };
    private bool _arranging;
    private bool _userMoving;
    private int _savedLeftLogical;

    public FileDisplayPanel FileDisplay { get; }
    public Control? LeftContent { get; private set; }
    public bool LeftPanelVisible => !_split.Panel1Collapsed;
    public int SavedLeftWidth => _savedLeftLogical;

    /// <summary>利用者が境界を動かしたときだけ、96 DPI 論理幅を通知する。</summary>
    public event EventHandler<int>? LeftWidthChanged;

    public CentralDisplayArea(FileDisplayPanel fileDisplay, int savedLeftWidth = 280)
    {
        FileDisplay = fileDisplay;
        _savedLeftLogical = Math.Max(LeftMinimumLogical, savedLeftWidth);
        Dock = DockStyle.Fill;

        _split.Panel2.Controls.Add(FileDisplay);
        _split.Panel1Collapsed = true;
        _split.SplitterMoving += OnSplitterMoving;
        _split.SplitterMoved += OnSplitterMoved;
        Controls.Add(_split);
        ApplyScaleAndWidth();
    }

    public void ShowLeft(Control content)
    {
        if (!ReferenceEquals(LeftContent, content))
        {
            _split.Panel1.Controls.Clear();
            LeftContent = content;
            content.Dock = DockStyle.Fill;
            _split.Panel1.Controls.Add(content);
        }
        _split.Panel1Collapsed = false;
        ApplyScaleAndWidth();
    }

    public void HideLeft()
    {
        _split.Panel1Collapsed = true;
    }

    public void SetSavedLeftWidth(int logicalWidth)
    {
        _savedLeftLogical = Math.Max(LeftMinimumLogical, logicalWidth);
        ApplyScaleAndWidth();
    }

    public static CentralDisplayWidths DecideWidths(
        int totalWidth, int savedLeftWidth, int leftMinimum, int rightMinimum, int splitterWidth)
    {
        totalWidth = Math.Max(0, totalWidth);
        leftMinimum = Math.Max(0, leftMinimum);
        rightMinimum = Math.Max(0, rightMinimum);
        splitterWidth = Math.Max(0, splitterWidth);

        var preserved = Math.Max(leftMinimum, savedLeftWidth);
        var maximumLeft = Math.Max(0, totalWidth - rightMinimum - splitterWidth);
        return new CentralDisplayWidths(Math.Min(preserved, maximumLeft), preserved);
    }

    public static int ToDeviceWidth(int logicalWidth, int dpi) =>
        (int)(((long)Math.Max(0, logicalWidth) * Math.Max(1, dpi) + 48) / 96);

    public static int ToLogicalWidth(int deviceWidth, int dpi) =>
        (int)(((long)Math.Max(0, deviceWidth) * 96 + Math.Max(1, dpi) / 2) / Math.Max(1, dpi));

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyScaleAndWidth();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyScaleAndWidth();
    }

    private void ApplyScaleAndWidth()
    {
        if (_arranging || _split.Panel1Collapsed) return;
        _userMoving = false;
        var dpi = DeviceDpi;
        var splitter = ToDeviceWidth(SplitterLogical, dpi);
        var widths = DecideWidths(_split.ClientSize.Width, ToDeviceWidth(_savedLeftLogical, dpi),
            ToDeviceWidth(LeftMinimumLogical, dpi), ToDeviceWidth(RightMinimumLogical, dpi), splitter);

        _arranging = true;
        try
        {
            _split.SplitterWidth = Math.Max(1, splitter);
            var available = Math.Max(0, _split.ClientSize.Width - _split.SplitterWidth);
            _split.SplitterDistance = Math.Min(widths.VisibleLeft, available);
        }
        finally
        {
            _arranging = false;
        }
    }

    private void OnSplitterMoving(object? sender, SplitterCancelEventArgs e)
    {
        _userMoving = true;
        var dpi = DeviceDpi;
        var widths = DecideWidths(_split.ClientSize.Width, e.SplitX,
            ToDeviceWidth(LeftMinimumLogical, dpi), ToDeviceWidth(RightMinimumLogical, dpi), _split.SplitterWidth);
        e.SplitX = widths.VisibleLeft;
    }

    private void OnSplitterMoved(object? sender, SplitterEventArgs e)
    {
        if (_arranging || _split.Panel1Collapsed || !_userMoving) return;
        _userMoving = false;
        var dpi = DeviceDpi;
        var leftMinimum = ToDeviceWidth(LeftMinimumLogical, dpi);
        var maximumLeft = Math.Max(0,
            _split.ClientSize.Width - ToDeviceWidth(RightMinimumLogical, dpi) - _split.SplitterWidth);
        // 狭い窓による一時縮小は保存値へ反映しない（R-96）。
        if (maximumLeft < leftMinimum) return;

        var logical = Math.Max(LeftMinimumLogical, ToLogicalWidth(_split.SplitterDistance, dpi));
        if (logical == _savedLeftLogical) return;
        _savedLeftLogical = logical;
        LeftWidthChanged?.Invoke(this, logical);
    }
}
