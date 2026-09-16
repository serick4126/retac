using ReTAC.App;
using ReTAC.Domain.Keys;

namespace ReTAC.Domain.Tests;

/// <summary>R-74: Win32 のメッセージからどのマウスボタンかを取り出す</summary>
public class MouseButtonFilterTests
{
    [Theory]
    [InlineData(0x0207, 0x0000_0000, Vk.MButton)]          // WM_MBUTTONDOWN
    [InlineData(0x0208, 0x0000_0000, Vk.MButton)]          // WM_MBUTTONUP
    [InlineData(0x0209, 0x0000_0000, Vk.MButton)]          // WM_MBUTTONDBLCLK
    [InlineData(0x020B, 0x0001_0000, Vk.XButton1)]         // WM_XBUTTONDOWN / XBUTTON1
    [InlineData(0x020B, 0x0002_0000, Vk.XButton2)]         // WM_XBUTTONDOWN / XBUTTON2
    [InlineData(0x020C, 0x0002_0000, Vk.XButton2)]         // WM_XBUTTONUP / XBUTTON2
    [InlineData(0x020D, 0x0001_0000, Vk.XButton1)]         // WM_XBUTTONDBLCLK / XBUTTON1
    [InlineData(0x0201, 0x0000_0000, 0)]                   // WM_LBUTTONDOWN は対象外（左は固定）
    [InlineData(0x0204, 0x0000_0000, 0)]                   // WM_RBUTTONDOWN は対象外（右は固定）
    [InlineData(0x020A, 0x0078_0000, 0)]                   // WM_MOUSEWHEEL は対象外（回転は固定）
    public void メッセージからボタンを取り出す(int msg, int wParam, int expected)
    {
        Assert.Equal((ushort)expected, MouseButtonFilter.ButtonOf(msg, wParam));
    }

    [Fact]
    public void サイドボタンの種別は上位ワードで見る()
    {
        // 下位ワード（修飾キーやボタンの押下状態）が立っていても取り違えない。
        // 64 ビットでの HIWORD の取り出しを間違えると、ここで落ちる
        Assert.Equal(Vk.XButton1, MouseButtonFilter.ButtonOf(0x020B, 0x0001_0004));
        Assert.Equal(Vk.XButton2, MouseButtonFilter.ButtonOf(0x020B, 0x0002_0028));
    }
}
