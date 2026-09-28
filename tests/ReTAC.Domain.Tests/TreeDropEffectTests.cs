using ReTAC.Shell;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-111-2: NSTC の実装が普通のフォルダの上で UpdateDropFeedback を呼ぶことは無いが、
/// シェルが断る項目（PC など）の上や、呼び出し方が変わった場合の保険として、右ボタンのドラッグ中は
/// 左ボタンなら何も起きない所（別のドライブで移動だけ・リンクだけ）でも落とせる効果を返す
/// （0 のままだと OLE が OnDrop を呼ばず、右ドロップのメニューが出せない）。
/// </summary>
public class TreeDropEffectTests
{
    private const uint Copy = 1, Move = 2, Link = 4;

    [Fact]
    public void 右ボタンでリンクだけ許す別のドライブへはリンクの効果を返す()
    {
        Assert.Equal(Link, NameSpaceTreeHost.TreeDropEffect(@"C:\a\x.txt", @"D:\b", keyState: 0, allowed: Link, rightButton: true));
    }

    [Fact]
    public void 左ボタンでリンクだけ許す別のドライブへは0のまま()
    {
        Assert.Equal(0u, NameSpaceTreeHost.TreeDropEffect(@"C:\a\x.txt", @"D:\b", keyState: 0, allowed: Link, rightButton: false));
    }

    [Fact]
    public void 右ボタンで移動だけ許す別のドライブへは移動の効果を返す()
    {
        Assert.Equal(Move, NameSpaceTreeHost.TreeDropEffect(@"C:\a\x.txt", @"D:\b", keyState: 0, allowed: Move, rightButton: true));
    }

    [Fact]
    public void 左ボタンで移動だけ許す別のドライブへは0のまま()
    {
        Assert.Equal(0u, NameSpaceTreeHost.TreeDropEffect(@"C:\a\x.txt", @"D:\b", keyState: 0, allowed: Move, rightButton: false));
    }

    [Fact]
    public void 右ボタンでも仮想項目宛先nullなら0のまま()
    {
        Assert.Equal(0u, NameSpaceTreeHost.TreeDropEffect(@"C:\a\x.txt", null, keyState: 0, allowed: Copy | Move | Link, rightButton: true));
    }

    [Fact]
    public void 右ボタンでも全部許す同じドライブなら今までどおり移動()
    {
        Assert.Equal(Move, NameSpaceTreeHost.TreeDropEffect(@"C:\a\x.txt", @"C:\b", keyState: 0, allowed: Copy | Move | Link, rightButton: true));
    }
}
