using System.Windows.Forms;
using ReTAC.Domain.Keys;

namespace ReTAC.App;

/// <summary>
/// R-74: マウスボタン3/4/5 の押下をウィンドウ全体で受ける。
/// FileListView / DriveBar / StatusBar の 3 箇所に同じ処理を書き写さず 1 経路に集約する。
/// 一覧の余白でもドライブバーの上でも同じように効かせるための選択。
/// 左（VK_LBUTTON）と右（VK_RBUTTON）は固定なので一切見ない。
/// </summary>
/// <param name="owner">この窓宛てのメッセージだけを扱う。</param>
/// <param name="press">割り当てを実行する。割り当てが無ければ false を返すこと。</param>
public sealed class MouseButtonFilter(Form owner, Func<ushort, bool> press) : IMessageFilter
{
    private const int WmMButtonDown = 0x0207;
    private const int WmMButtonUp = 0x0208;
    private const int WmMButtonDblClk = 0x0209;
    private const int WmXButtonDown = 0x020B;
    private const int WmXButtonUp = 0x020C;
    private const int WmXButtonDblClk = 0x020D;

    /// <summary>押下を握ったボタン。0 なら握っていない。</summary>
    private ushort _pressed;

    public bool PreFilterMessage(ref Message m)
    {
        var button = ButtonOf(m.Msg, m.WParam);
        if (button == 0) return false;

        switch (m.Msg)
        {
            // 2 打目（DBLCLK）も 1 回の押下として扱う。「戻る」を素早く 2 回押したら 2 つ戻る
            case WmMButtonDown or WmXButtonDown or WmMButtonDblClk or WmXButtonDblClk:
                if (!IsOwn(m.HWnd) || !press(button)) return false;
                _pressed = button;
                return true;

            // 押下を握ったボタンの UP を DefWindowProc へ渡すと、Windows が WM_APPCOMMAND
            // （APPCOMMAND_BROWSER_BACKWARD など）を重ねて送る。放置すると「戻る」が二重に走る
            case WmMButtonUp or WmXButtonUp:
                if (_pressed != button) return false;
                _pressed = 0;
                return true;

            default:
                return false;
        }
    }

    /// <summary>メッセージが指すマウスボタンの仮想キーコード。対象外なら 0。</summary>
    public static ushort ButtonOf(int msg, nint wParam) => msg switch
    {
        WmMButtonDown or WmMButtonUp or WmMButtonDblClk => Vk.MButton,
        // どちらのサイドボタンかは wParam の上位ワード（XBUTTON1 = 1 / XBUTTON2 = 2）。
        // 下位ワードは修飾キーとボタンの押下状態なので、マスクしてから見る
        WmXButtonDown or WmXButtonUp or WmXButtonDblClk =>
            (wParam >> 16 & 0xFFFF) == 1 ? Vk.XButton1 : Vk.XButton2,
        _ => 0,
    };

    /// <summary>
    /// この窓宛てか。<c>Form.ActiveForm</c> では見ない。NewWindow（0x830B）で MainForm は
    /// 複数あり得るので、どの窓で押したかを取り違えてはならない。
    /// モーダルダイアログの上ではダイアログの Form が返るため、自動的に素通しになる
    /// （キー割り当てがダイアログ表示中に効かないのと同じ扱い）。
    /// </summary>
    private bool IsOwn(nint hwnd) => Control.FromChildHandle(hwnd)?.FindForm() == owner;
}
