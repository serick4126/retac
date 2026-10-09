using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ReTAC.Shell;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-97-5: 描画を止めて戻すときのスクロール位置の戻し方を、本物の SysTreeView32（WinForms の TreeView）で確かめる。
/// 名前空間ツリーの中も同じ部品。横を左端の場合しか戻していなかった（コードレビュー 2026-10-09: 50 を覚えて 90 のまま）。
/// </summary>
public class TreeViewScrollTests
{
    [Theory]
    [InlineData(30, 50, false)]
    [InlineData(30, 50, true)]
    [InlineData(5, 0, true)]
    [InlineData(40, 120, true)]
    public void 覚えた縦横の位置へ戻す(int vertical, int horizontal, bool redrawOff)
    {
        Sta.Run(() =>
        {
            using var form = new Form
            {
                StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000),
                Size = new Size(240, 200), ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None,
            };
            var tree = new TreeView { Dock = DockStyle.Fill, Scrollable = true };
            for (var i = 0; i < 100; i++) tree.Nodes.Add($"node-{i:D3}-" + new string('x', 120));
            form.Controls.Add(tree);
            form.Show();
            Application.DoEvents();

            tree.TopNode = tree.Nodes[vertical];
            SendMessage(tree.Handle, WM_HSCROLL, (IntPtr)((horizontal << 16) | SB_THUMBPOSITION), IntPtr.Zero);
            var held = TreeViewScroll.Capture(tree.Handle);
            Assert.Equal(new TreeViewScroll.Position(vertical, horizontal), held);

            // ツリーが自分で動かした（ここでは先頭と左端へ）
            if (redrawOff) SendMessage(tree.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            tree.TopNode = tree.Nodes[0];
            SendMessage(tree.Handle, WM_HSCROLL, (IntPtr)SB_LEFT, IntPtr.Zero);

            TreeViewScroll.Restore(tree.Handle, held);
            if (redrawOff) SendMessage(tree.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);

            Assert.Equal(held, TreeViewScroll.Capture(tree.Handle));
            Assert.Equal(vertical, tree.TopNode!.Index);
        });
    }

    private const uint WM_HSCROLL = 0x0114;
    private const uint WM_SETREDRAW = 0x000B;
    private const int SB_THUMBPOSITION = 4;
    private const int SB_LEFT = 6;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
