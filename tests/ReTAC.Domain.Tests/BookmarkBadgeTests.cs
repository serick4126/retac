using System.Drawing;
using ReTAC.App;
using ReTAC.Domain.FileOps;
using ReTAC.Domain.Navigation;
using System.Windows.Forms;

namespace ReTAC.Domain.Tests;

/// <summary>R-140-1 / R-140-2: ドロップ時の動作の印・表示名・ツールチップ・編集ダイアログ。ハンドルは作らない。</summary>
public class BookmarkBadgeTests
{
    private static readonly Color Gray = Color.FromArgb(0x60, 0x60, 0x60), Green = Color.FromArgb(0x00, 0x80, 0x00), Red = Color.FromArgb(0xC8, 0x00, 0x00);

    private static Bookmark Folder(BookmarkDropMode mode = BookmarkDropMode.Standard) =>
        new("保存先", BookmarkKind.Folder, @"C:\work\保存先") { DropMode = mode };

    [Theory]
    [InlineData(16, 10, 4, 0)]
    [InlineData(20, 13, 5, 0)]
    [InlineData(24, 15, 5, 0)]
    [InlineData(32, 20, 8, 0)]
    public void Badge_Metrics(int size, int box, int stroke, int inset) =>
        Assert.Equal((box, stroke, inset), BookmarkBadge.Metrics(size));

    private static Bitmap Blank(int size) => new(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

    private static void AssertArgb(Color expected, Color actual, string where) =>
        Assert.True(expected.ToArgb() == actual.ToArgb(), $"{where}: expected {expected} actual {actual}");

    /// <summary>枠の内側の座標（0 起点）が線の上か。</summary>
    private static bool OnLine(int dx, int dy, int size, BookmarkDropMode mode)
    {
        var (box, stroke, inset) = BookmarkBadge.Metrics(size);
        var inner = box - 2;
        var mid = (inner - stroke) / 2;
        var length = inner - 2 * inset;
        var h = dy >= mid && dy < mid + stroke && dx >= inset && dx < inset + length;
        var v = mode == BookmarkDropMode.Copy && dx >= mid && dx < mid + stroke && dy >= inset && dy < inset + length;
        return h || v;
    }

    private static void AssertBadge(int size, BookmarkDropMode mode)
    {
        using var bmp = Blank(size);
        BookmarkBadge.Draw(bmp, mode, highContrast: false);
        var (box, _, _) = BookmarkBadge.Metrics(size);
        var (x0, y0) = (size - box, size - box);
        var lineColor = mode == BookmarkDropMode.Copy ? Green : Red;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var actual = bmp.GetPixel(x, y);
                var (dx, dy) = (x - x0, y - y0);
                if (dx < 0 || dy < 0) { Assert.Equal(0, actual.A); continue; }   // 枠の外は透明のまま
                var edge = dx == 0 || dy == 0 || dx == box - 1 || dy == box - 1;
                var expected = edge ? Gray : OnLine(dx - 1, dy - 1, size, mode) ? lineColor : Color.White;
                AssertArgb(expected, actual, $"({x},{y})");
            }
    }

    [Theory]
    [InlineData(BookmarkDropMode.Copy)]
    [InlineData(BookmarkDropMode.Move)]
    public void Badge_Pixels16(BookmarkDropMode mode)
    {
        AssertBadge(16, mode);
        using var bmp = Blank(16);
        BookmarkBadge.Draw(bmp, mode, false);
        // 枠は 6..15。横線は y 9..12・x 7..14（内側いっぱい）。縦線（コピーだけ）は x 9..12・y 7..14
        AssertArgb(Gray, bmp.GetPixel(6, 6), "左上");
        AssertArgb(Gray, bmp.GetPixel(15, 15), "右下");
        var line = mode == BookmarkDropMode.Copy ? Green : Red;
        AssertArgb(line, bmp.GetPixel(7, 9), "横線の左端");
        AssertArgb(line, bmp.GetPixel(14, 12), "横線の右端");
        AssertArgb(Color.White, bmp.GetPixel(7, 8), "横線の上");
        AssertArgb(mode == BookmarkDropMode.Copy ? Green : Color.White, bmp.GetPixel(9, 7), "縦線の上端");
        AssertArgb(mode == BookmarkDropMode.Copy ? Green : Color.White, bmp.GetPixel(12, 14), "縦線の下端");
    }

    [Theory]
    [InlineData(BookmarkDropMode.Copy)]
    [InlineData(BookmarkDropMode.Move)]
    public void Badge_Pixels24(BookmarkDropMode mode) => AssertBadge(24, mode);

    [Fact]
    public void Badge_Standard_ChangesNothing()
    {
        using var bmp = Blank(16);
        BookmarkBadge.Draw(bmp, BookmarkDropMode.Standard, false);
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++) Assert.Equal(0, bmp.GetPixel(x, y).A);
    }

    [Fact]
    public void Undefined_TreatedAsStandard()
    {
        var undefined = (BookmarkDropMode)7;
        using var bmp = Blank(16);
        BookmarkBadge.Draw(bmp, undefined, false);
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++) Assert.Equal(0, bmp.GetPixel(x, y).A);
        Assert.Equal("保存先", BookmarkItems.Shown(Folder(undefined), "保存先", iconShown: false));
        Assert.Null(BookmarkItems.DropModeLine(Folder(undefined)));
        Assert.Equal(DropRules.DecideFixed(BookmarkDropMode.Standard, @"C:\a\x.txt", @"C:\b", false, false, true, true),
            DropRules.DecideFixed(undefined, @"C:\a\x.txt", @"C:\b", false, false, true, true));
    }

    [Fact]
    public void Badge_HighContrast()
    {
        using var bmp = Blank(16);
        BookmarkBadge.Draw(bmp, BookmarkDropMode.Copy, highContrast: true);
        AssertArgb(SystemColors.WindowText, bmp.GetPixel(6, 6), "枠");
        AssertArgb(SystemColors.WindowText, bmp.GetPixel(7, 9), "線");
        AssertArgb(SystemColors.Window, bmp.GetPixel(7, 7), "中");
    }

    [Fact]
    public void Shown_PrefixOnlyWithoutIcon()
    {
        Assert.Equal("[+] 保存先", BookmarkItems.Shown(Folder(BookmarkDropMode.Copy), "保存先", iconShown: false));
        Assert.Equal("[\u2212] 保存先", BookmarkItems.Shown(Folder(BookmarkDropMode.Move), "保存先", iconShown: false));
        Assert.Equal("保存先", BookmarkItems.Shown(Folder(BookmarkDropMode.Copy), "保存先", iconShown: true));
        Assert.Equal("保存先", BookmarkItems.Shown(Folder(), "保存先", iconShown: false));
        var file = new Bookmark("f", BookmarkKind.File, @"C:\a.txt") { DropMode = BookmarkDropMode.Copy };
        Assert.Equal("f", BookmarkItems.Shown(file, "f", iconShown: false));
    }

    [Fact]
    public void Tooltip_HasDropLine()
    {
        Assert.Equal("ドロップ: 常にコピー", BookmarkItems.DropModeLine(Folder(BookmarkDropMode.Copy)));
        Assert.Equal("ドロップ: 常に移動", BookmarkItems.DropModeLine(Folder(BookmarkDropMode.Move)));
        Assert.Null(BookmarkItems.DropModeLine(Folder()));
    }

    [Fact]
    public void TreeNode_NameAndTooltip()
    {
        var b = Folder(BookmarkDropMode.Copy);
        Assert.Equal("保存先", BookmarkTreeView.NodeFor(b, "保存先", iconShown: true).Text);
        var failed = BookmarkTreeView.NodeFor(b, "保存先", iconShown: false);
        Assert.Equal("[+] 保存先", failed.Text);
        Assert.EndsWith(Environment.NewLine + "ドロップ: 常にコピー", failed.ToolTipText);
        Assert.StartsWith(b.Target, failed.ToolTipText);
        // 指定を変えて作り直すと、そのたびに名前・ツールチップが変わる
        var moved = BookmarkTreeView.NodeFor(b with { DropMode = BookmarkDropMode.Move }, "保存先", iconShown: false);
        Assert.Equal("[\u2212] 保存先", moved.Text);
        Assert.Contains("ドロップ: 常に移動", moved.ToolTipText);
        var standard = BookmarkTreeView.NodeFor(b with { DropMode = BookmarkDropMode.Standard }, "保存先", iconShown: false);
        Assert.Equal("保存先", standard.Text);
        Assert.Equal(b.Target, standard.ToolTipText);
    }

    [Theory]
    [InlineData(BookmarkKind.Folder, true)]
    [InlineData(BookmarkKind.File, false)]
    [InlineData(BookmarkKind.Group, false)]
    public void EntryDialog_DropModeOnlyForFolder(BookmarkKind kind, bool visible)
    {
        var preset = new Bookmark("n", kind, kind == BookmarkKind.Group ? "" : @"C:\x", kind == BookmarkKind.Group ? [] : null);
        using var dialog = new BookmarkEntryDialog(preset, @"C:\", [], _ => "");
        var combo = dialog.Controls.OfType<ComboBox>().SingleOrDefault();
        Assert.Equal(visible, combo is not null);
        Assert.Equal(BookmarkDropMode.Standard, dialog.Bookmark.DropMode);
        if (combo is null) return;
        Assert.Equal(["Windows 標準", "常にコピー", "常に移動"], combo.Items.Cast<string>());
        combo.SelectedIndex = 2;
        Assert.Equal(BookmarkDropMode.Move, dialog.Bookmark.DropMode);
        Assert.False(dialog.IsHandleCreated);
    }

    [Fact]
    public void EntryDialog_DropLabelDoesNotOverlapCombo()
    {
        var preset = new Bookmark("n", BookmarkKind.Folder, @"C:\x");
        using var dialog = new BookmarkEntryDialog(preset, @"C:\", [], _ => "");
        var label = dialog.Controls.OfType<Label>().Single(l => l.Text.StartsWith("ドロップ"));
        var combo = dialog.Controls.OfType<ComboBox>().Single();
        var labelBounds = new Rectangle(label.Location, label.PreferredSize);
        Assert.False(labelBounds.IntersectsWith(combo.Bounds), $"label {labelBounds} combo {combo.Bounds}");
        Assert.Equal(420, combo.Bounds.Right);   // 右端は他の欄とそろえる
        Assert.False(dialog.IsHandleCreated);
    }

    [Fact]
    public void EntryDialog_UndefinedDropModeSelectsStandard()
    {
        var preset = new Bookmark("n", BookmarkKind.Folder, @"C:\x") { DropMode = (BookmarkDropMode)7 };
        using var dialog = new BookmarkEntryDialog(preset, @"C:\", [], _ => "");
        Assert.Equal(0, dialog.Controls.OfType<ComboBox>().Single().SelectedIndex);
        Assert.False(dialog.IsHandleCreated);
    }
}
