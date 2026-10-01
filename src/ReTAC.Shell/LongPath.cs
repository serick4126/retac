using System.Runtime.InteropServices;
using System.Text;

namespace ReTAC.Shell;

/// <summary>
/// R-125: 8.3 形式の短い名前を長い名前に直す。OS の転送は通知で長いパスを返すので、
/// 計画のパスが短いままだと、宛先の検査（計画の後に変わった宛先）と上書きの印が一致せず、どちらも効かない。
/// </summary>
public static class LongPath
{
    /// <summary>長いパスを返す。直せない（無い・読めない）ときは、そのまま返す。</summary>
    public static string Of(string path)
    {
        var buffer = new StringBuilder(path.Length + 260);
        var length = GetLongPathName(path, buffer, buffer.Capacity);
        if (length > buffer.Capacity)
        {
            buffer.Capacity = (int)length;
            length = GetLongPathName(path, buffer, buffer.Capacity);
        }
        return length == 0 || length > buffer.Capacity ? path : buffer.ToString();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetLongPathName(string shortPath, StringBuilder longPath, int bufferLength);
}
