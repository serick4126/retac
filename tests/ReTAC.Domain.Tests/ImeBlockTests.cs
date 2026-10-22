using System.Runtime.InteropServices;
using System.Windows.Forms;
using ReTAC.App;
using ReTAC.Domain.Tests;

namespace ReTAC.Domain.Tests;

/// <summary>
/// B-04: ファイルリストとドライブバーは IME を使わない（L の次の 1 打鍵がジャンプになる）。
/// DriveBar はコンストラクタでグラフィックスを作りハンドルが要るため、テストでは作れない。
/// ソースの配線だけ確かめる。実際の切り離しは実機（IME オンで L の次に D）で確かめる。
/// </summary>
public class ImeBlockTests
{
    private static string Src(string name) => File.ReadAllText(Path.Combine(SchemaManifest.RepoRoot(), "src", "ReTAC.App", name));

    [Theory]
    [InlineData("DriveBar.cs")]
    [InlineData("FileListView.cs")]
    public void SelfDrawnControls_DetachIme(string file)
    {
        var text = Src(file);
        Assert.Contains("ImeMode = ImeMode.Disable", text);
        Assert.Contains("ImeBlock.Detach(this)", text);
        Assert.DoesNotContain("ImmAssociateContext", text);   // P/Invoke は ImeBlock の 1 か所だけ
    }

    /// <summary>
    /// R-143-1 で専用の入力コンテキストを持たせないダイアログ。入力できる欄が無いか、IME を使わない窓だけを載せる。
    /// 新しいダイアログはここに載せるか <c>ImeBlock.OwnContext(this)</c> を呼ぶかを決めないとテストが通らない。
    /// </summary>
    private static readonly Dictionary<string, string> s_noOwnContext = new()
    {
        ["MainForm"] = "主ウィンドウ。アドレスバー・インクリメンタルサーチはファイルリストと同じ窓で長く使うので共有のまま",
        ["DriveSelectForm"] = "R-143: 1 打鍵がドライブの選択。IME を使わない",
        ["AboutDialog"] = "入力欄なし",
        ["AttributeDialog"] = "日付と時刻の選択だけ。IME を使わない",
        ["BookmarkDialog"] = "選ぶだけのコンボボックス",
        ["ChoiceDialog"] = "入力欄なし",
        ["ConfirmDialog"] = "読むだけの欄",
        ["ConflictDialog"] = "入力欄なし",
        ["ExternalToolQueueForm"] = "入力欄なし",
        ["FileTypeDialog"] = "入力欄なし",
        ["MacroReferenceDialog"] = "読むだけの欄",
        ["SortDialog"] = "入力欄なし",
    };

    [Fact]
    public void EveryDialog_OwnsImeContext_OrIsListedWithReason()
    {
        var dir = Path.Combine(SchemaManifest.RepoRoot(), "src", "ReTAC.App");
        var forms = new Dictionary<string, string>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs"))
        {
            var text = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(text, @"class\s+(\w+)(?:\([^)]*\))?\s*:\s*Form\b"))
                forms[m.Groups[1].Value] = text;
        }

        Assert.NotEmpty(forms);
        var missing = forms.Where(f => !f.Value.Contains("ImeBlock.OwnContext(this)") && !s_noOwnContext.ContainsKey(f.Key))
                           .Select(f => f.Key).ToList();
        Assert.True(missing.Count == 0, "R-143-1: ImeBlock.OwnContext(this) を呼ぶか s_noOwnContext に理由を書く: " + string.Join(", ", missing));
        Assert.Empty(s_noOwnContext.Keys.Where(k => !forms.ContainsKey(k)));   // 消えたダイアログを載せたままにしない
        Assert.Empty(s_noOwnContext.Keys.Where(k => forms.TryGetValue(k, out var t) && t.Contains("ImeBlock.OwnContext(this)")));
    }

    // ---- R-143-1: 実 OS の HIMC と TSF で確かめる。表示しないフォームを STA のスレッドで作る ----

    [DllImport("imm32.dll")] private static extern IntPtr ImmGetContext(IntPtr window);
    [DllImport("imm32.dll")] private static extern bool ImmReleaseContext(IntPtr window, IntPtr context);

    private static IntPtr Himc(Control control)
    {
        var context = ImmGetContext(control.Handle);
        if (context != IntPtr.Zero) ImmReleaseContext(control.Handle, context);
        return context;
    }

    private sealed class TestDialog : Form
    {
        public TestDialog() { ImeBlock.OwnContext(this); ShowInTaskbar = false; }
        public void RaiseActivated() => OnActivated(EventArgs.Empty);
        public void RaiseDeactivate() => OnDeactivate(EventArgs.Empty);
        public void Recreate() => RecreateHandle();
    }

    private sealed class RecreatableTextBox : TextBox { public void Recreate() => RecreateHandle(); }

    public enum Addition { BeforeFormHandle, UncreatedAfter, PrecreatedDirect, PrecreatedIntoPanel, PrecreatedSubtreeDirect }

    [Theory]
    [InlineData(Addition.BeforeFormHandle)]
    [InlineData(Addition.UncreatedAfter)]
    [InlineData(Addition.PrecreatedDirect)]
    [InlineData(Addition.PrecreatedIntoPanel)]
    [InlineData(Addition.PrecreatedSubtreeDirect)]
    public void InputAddedInAnyOrder_UsesTheDialogContext(Addition addition) => Sta.Run(() =>
    {
        using var outside = new TextBox();
        var shared = Himc(outside);
        using var dialog = new TestDialog();
        var input = new TextBox();
        if (addition == Addition.BeforeFormHandle) dialog.Controls.Add(input);
        _ = dialog.Handle;
        switch (addition)
        {
            case Addition.UncreatedAfter:
                dialog.Controls.Add(input);
                break;
            case Addition.PrecreatedDirect:
                _ = input.Handle;
                dialog.Controls.Add(input);
                break;
            case Addition.PrecreatedIntoPanel:
                var panel = new Panel();
                dialog.Controls.Add(panel);
                _ = panel.Handle;
                _ = input.Handle;
                panel.Controls.Add(input);
                break;
            case Addition.PrecreatedSubtreeDirect:
                var container = new Panel();
                container.Controls.Add(input);
                _ = container.Handle;
                _ = input.Handle;
                dialog.Controls.Add(container);
                break;
        }

        Assert.NotEqual(IntPtr.Zero, shared);
        Assert.NotEqual(shared, Himc(dialog));   // 陽性対照: 専用と共有は別物
        Assert.Equal(Himc(dialog), Himc(input));
    });

    [Fact]
    public void RecreatedHandles_KeepTheDialogContext() => Sta.Run(() =>
    {
        using var outside = new TextBox();
        var shared = Himc(outside);
        using var dialog = new TestDialog();
        var input = new RecreatableTextBox();
        dialog.Controls.Add(input);
        _ = dialog.Handle;

        input.Recreate();
        Assert.Equal(Himc(dialog), Himc(input));

        dialog.Recreate();
        Assert.NotEqual(shared, Himc(dialog));
        Assert.Equal(Himc(dialog), Himc(input));
    });

    /// <summary>TSF が使えない環境では、合格に見えないようスキップとして数える。</summary>
    private sealed class TsfFactAttribute : FactAttribute
    {
        public TsfFactAttribute()
        {
            ImeBlock.ImeState? state = null;
            Sta.Run(() => state = ImeBlock.Tsf.Read());
            if (state is null) Skip = "TSF のキーボードの状態を読めない環境。R-143-1 の引き継ぎは未確認";
        }
    }

    private static readonly ImeBlock.ImeState s_on = new(true, 9);
    private static readonly ImeBlock.ImeState s_off = new(false, 0);

    private static void Drain() => Application.DoEvents();

    [TsfFact]
    public void FirstActivation_WritesTheRememberedState() => Sta.Run(() =>
    {
        ImeBlock.s_last = s_on;
        ImeBlock.Tsf.Write(s_off);
        using var dialog = new TestDialog();
        _ = dialog.Handle;

        dialog.RaiseActivated();
        Drain();

        Assert.Equal(s_on, ImeBlock.Tsf.Read());
    });

    [TsfFact]
    public void DeactivatedBeforeTheQueuedWrite_LeavesTheNewStateAlone_AndWritesOnReactivation() => Sta.Run(() =>
    {
        ImeBlock.s_last = s_on;
        ImeBlock.Tsf.Write(s_off);
        using var dialog = new TestDialog();
        _ = dialog.Handle;

        dialog.RaiseActivated();
        dialog.RaiseDeactivate();
        Drain();

        Assert.Equal(s_off, ImeBlock.Tsf.Read());   // 移った先の状態を書き換えない
        Assert.Equal(s_on, ImeBlock.s_last);         // 書き戻す前の初期値を覚えない

        dialog.RaiseActivated();
        Drain();
        Assert.Equal(s_on, ImeBlock.Tsf.Read());     // 戻ってきたら引き継ぐ
    });

    [TsfFact]
    public void QueuedWriteOfAnotherDialog_DoesNotOverwriteTheActiveDialog() => Sta.Run(() =>
    {
        ImeBlock.s_last = s_on;
        using var active = new TestDialog();
        using var stale = new TestDialog();
        _ = active.Handle;
        _ = stale.Handle;
        active.RaiseActivated();
        Drain();

        stale.RaiseActivated();
        stale.RaiseDeactivate();
        active.RaiseActivated();
        ImeBlock.Tsf.Write(s_off);   // 利用者がアクティブなダイアログでオフにした
        Drain();

        Assert.Equal(s_off, ImeBlock.Tsf.Read());
    });

    [TsfFact]
    public void DisposedBeforeTheQueuedWrite_DoesNotWrite() => Sta.Run(() =>
    {
        ImeBlock.s_last = s_on;
        ImeBlock.Tsf.Write(s_off);
        var dialog = new TestDialog();
        _ = dialog.Handle;

        dialog.RaiseActivated();
        dialog.Dispose();
        Drain();

        Assert.Equal(s_off, ImeBlock.Tsf.Read());
    });

    [TsfFact]
    public void HandleRecreatedBeforeTheQueuedWrite_DoesNotWrite_AndWritesOnTheNextActivation() => Sta.Run(() =>
    {
        ImeBlock.s_last = s_on;
        ImeBlock.Tsf.Write(s_off);
        using var dialog = new TestDialog();
        _ = dialog.Handle;

        dialog.RaiseActivated();
        dialog.Recreate();
        Drain();
        Assert.Equal(s_off, ImeBlock.Tsf.Read());

        dialog.RaiseActivated();
        Drain();
        Assert.Equal(s_on, ImeBlock.Tsf.Read());
    });

    /// <summary>入れ子: 親から子を開くと子が親の状態を引き継ぎ、子を閉じると子の状態が残る（親は書き戻し直さない）。</summary>
    [TsfFact]
    public void NestedDialogs_CarryTheStateOfTheDialogThatWasActiveLast() => Sta.Run(() =>
    {
        ImeBlock.s_last = null;
        using var parent = new TestDialog();
        using var child = new TestDialog();
        _ = parent.Handle;
        _ = child.Handle;

        parent.RaiseActivated();
        Drain();
        ImeBlock.Tsf.Write(s_off);   // 親でオフにした
        parent.RaiseDeactivate();     // 子を開く
        Assert.Equal(s_off, ImeBlock.s_last);

        child.RaiseActivated();
        Drain();
        Assert.Equal(s_off, ImeBlock.Tsf.Read());
        ImeBlock.Tsf.Write(s_on);    // 子でオンにした
        child.RaiseDeactivate();      // 子を閉じる
        Assert.Equal(s_on, ImeBlock.s_last);

        parent.RaiseActivated();
        Drain();
        Assert.Equal(s_on, ImeBlock.Tsf.Read());
    });
}
