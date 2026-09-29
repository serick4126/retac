using ReTAC.App;
using Xunit;

namespace ReTAC.Domain.Tests;

public class ShortcutDialogTests
{
    // R-111-3: 作成先は切らずに全部見せ、長いほど高くなる
    [Fact]
    public void LongDestinationIsShownInFullAndGrows()
    {
        var longPath = @"C:\" + string.Join(@"\", Enumerable.Repeat("VeryLongFolderNameWithoutSpaces", 8));
        using var shortDlg = new ShortcutDialog(@"C:\a");
        using var longDlg = new ShortcutDialog(longPath);
        Assert.Equal("作成先: " + longPath, longDlg.DestinationBox!.Text);
        Assert.True(longDlg.DestinationBox.Height > shortDlg.DestinationBox!.Height);
        Assert.True(longDlg.ClientSize.Height > shortDlg.ClientSize.Height);
    }

    [Theory]
    [InlineData(@"C:")]
    [InlineData(@"C:\VeryLongFolderNameWithoutSpaces\VeryLongFolderNameWithoutSpaces\VeryLongFolderNameWithoutSpaces\VeryLongFolderNameWithoutSpaces")]
    public void DestinationDoesNotOverlapButtons(string path)
    {
        using var dlg = new ShortcutDialog(path);
        var ok = dlg.Controls.OfType<System.Windows.Forms.Button>().First(b => b.Text == "OK");
        Assert.True(dlg.DestinationBox!.Bottom <= ok.Top);
    }
}
