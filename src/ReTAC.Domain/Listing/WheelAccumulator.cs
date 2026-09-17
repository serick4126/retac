namespace ReTAC.Domain.Listing;

/// <summary>
/// R-76: ホイールの回転量の端数を持ち越す。1 ノッチ = 1 列。
/// 高精度ホイールやタッチパッドは 1 ノッチ未満の値を小分けに送る。整数の割り算で直接列数にすると、
/// その入力が毎回 0 になって捨てられ、横スクロールが効かない。
/// </summary>
public sealed class WheelAccumulator
{
    private int _remainder;

    /// <returns>今回スクロールする列数。<paramref name="delta"/> と同じ符号</returns>
    public int Add(int delta, int notch)
    {
        if (notch <= 0) return 0;
        // 逆向きに回したのに、前の端数を打ち消すまで動かないのは不自然なので捨てる
        if (_remainder != 0 && Math.Sign(delta) != Math.Sign(_remainder)) _remainder = 0;
        _remainder += delta;
        var steps = _remainder / notch;   // 0 方向への切り捨て。符号はそのまま残る
        _remainder -= steps * notch;
        return steps;
    }

    public void Reset() => _remainder = 0;
}
