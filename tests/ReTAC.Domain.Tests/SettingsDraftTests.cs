using ReTAC.App;
using ReTAC.App.Rendering;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Keys;
using ReTAC.Domain.Navigation;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

/// <summary>
/// INV-SETTINGS-DRAFT: 下書きは共有の実体から独立していて、確定（CommitTo）を呼ぶまでは
/// AppSettings・キーマップ・共有のクイックアクセスを変えない。浅いコピーが 1 つでも混ざると
/// 実機確認だけでは気づけないため、ここで自動テストとして押さえる（R-102-3）。
/// </summary>
public class SettingsDraftTests
{
    private static readonly KeyBinding EditorKey = new(Vk.Letter('E'));
    private static readonly KeyBinding SpareKey = new(Vk.Letter('Z'), Ctrl: true);

    private static (AppSettings Settings, KeyMap KeyMap, QuickAccessList QuickAccess) Baseline()
    {
        var settings = new AppSettings();
        var keyMap = settings.ToKeyMap();
        var quickAccess = new QuickAccessList();
        quickAccess.Add(new QuickAccessEntry("仕事", @"D:\work"));
        quickAccess.Add(new QuickAccessEntry("エディタ", new ToolTarget(DefaultExternalTools.EditorId).Serialize(), BookmarkKind.Command));
        return (settings, keyMap, quickAccess);
    }

    [Fact]
    public void 下書きのキー割り当てを変えても元のKeyMapは変わらない()
    {
        var (settings, keyMap, quickAccess) = Baseline();
        var draft = SettingsDraft.From(settings, keyMap, Theme.Default, quickAccess);

        draft.KeyBindings[SpareKey] = new BuiltinTarget(CommandId.Rename);
        draft.KeyBindings[EditorKey] = null;

        Assert.Null(keyMap.Resolve(SpareKey));
        Assert.Equal(new ToolTarget(DefaultExternalTools.EditorId), keyMap.Resolve(EditorKey));
    }

    [Fact]
    public void 下書きで外部ツールを編集してもAppSettingsのExternalToolsは変わらない()
    {
        var (settings, keyMap, quickAccess) = Baseline();
        var originalTools = settings.ExternalTools;
        var originalFirst = originalTools[0];
        var draft = SettingsDraft.From(settings, keyMap, Theme.Default, quickAccess);

        // 追加
        draft.ExternalTools = [.. draft.ExternalTools, new ExternalTool { Id = 99, Name = "追加" }];
        // 変更（record なので with で別インスタンスになる）
        draft.ExternalTools = [.. draft.ExternalTools.Select(t => t.Id == DefaultExternalTools.EditorId ? t with { Name = "改名" } : t)];
        // 削除
        draft.RemoveTool(DefaultExternalTools.ViewerId);

        Assert.Same(originalTools, settings.ExternalTools);
        Assert.Equal("テキスト エディタ", originalFirst.Name);
        Assert.Equal(3, settings.ExternalTools.Count);
    }

    [Fact]
    public void 下書きのクイックアクセスを変えても共有の実体は変わらない()
    {
        var (settings, keyMap, quickAccess) = Baseline();
        var originalCount = quickAccess.Items.Count;
        var draft = SettingsDraft.From(settings, keyMap, Theme.Default, quickAccess);

        draft.QuickAccess.Add(new QuickAccessEntry("新規", @"D:\new"));   // 追加
        draft.QuickAccess.RemoveAt(0);                                     // 削除
        draft.QuickAccess.Move(0, 1);                                      // 並べ替え
        draft.QuickAccess.ShowTitles = false;                              // 設定 1
        draft.QuickAccess.FixMissingAutomatically = true;                  // 設定 2

        Assert.Equal(originalCount, quickAccess.Items.Count);
        Assert.True(quickAccess.ShowTitles);
        Assert.False(quickAccess.FixMissingAutomatically);
        Assert.NotSame(draft.QuickAccess, quickAccess);
    }

    [Fact]
    public void 下書きでツールを削除すると下書きの中のキー割り当てとクイックアクセスだけ整理される()
    {
        var (settings, keyMap, quickAccess) = Baseline();
        var draft = SettingsDraft.From(settings, keyMap, Theme.Default, quickAccess);

        draft.RemoveTool(DefaultExternalTools.EditorId);

        // 下書きの中は整理される（F-01 / INV-TOOLTARGET-FK）
        Assert.Null(draft.KeyBindings[EditorKey]);
        Assert.DoesNotContain(draft.QuickAccess.Items, e => e.Kind == BookmarkKind.Command);
        Assert.Equal([DefaultExternalTools.EditorId], draft.RemovedToolIds);

        // 共有の側は変わらない
        Assert.Equal(new ToolTarget(DefaultExternalTools.EditorId), keyMap.Resolve(EditorKey));
        Assert.Contains(quickAccess.Items, e => e.Kind == BookmarkKind.Command);
    }

    [Fact]
    public void 動作環境配色表示するドライブを下書きで変えてもAppSettingsはApplySettingsまで変わらない()
    {
        var (settings, keyMap, quickAccess) = Baseline();
        var draft = SettingsDraft.From(settings, keyMap, Theme.Default, quickAccess);

        draft.Resident = !settings.Resident;
        draft.StartMinimized = !settings.StartMinimized;
        draft.KeepLastFolder = !settings.KeepLastFolder;
        draft.SuppressMultipleToolLaunch = !settings.SuppressMultipleToolLaunch;
        draft.Theme = Theme.Default with { FontSize = 20f };
        draft.HiddenDrives = ['D'];
        draft.ShowDesktopButton = !settings.ShowDesktopButton;

        Assert.True(settings.Resident);
        Assert.False(settings.StartMinimized);
        Assert.True(settings.KeepLastFolder);
        Assert.True(settings.SuppressMultipleToolLaunch);
        Assert.Equal(16f, settings.ToTheme().FontSize);
        Assert.Empty(settings.HiddenDrives);
        Assert.True(settings.ShowDesktopButton);
    }

    [Fact]
    public void SuppressMultipleChangedは値が変わったときだけ上がる()
    {
        var draft = new SettingsDraft { SuppressMultipleToolLaunch = true };
        var raised = 0;
        draft.SuppressMultipleChanged += (_, _) => raised++;

        draft.SuppressMultipleToolLaunch = true;   // 同じ値
        Assert.Equal(0, raised);

        draft.SuppressMultipleToolLaunch = false;
        Assert.Equal(1, raised);
    }

    [Fact]
    public void ToolsChangedは一覧を差し替えるたびに上がる()
    {
        var draft = new SettingsDraft();
        var raised = 0;
        draft.ToolsChanged += (_, _) => raised++;

        draft.ExternalTools = [new ExternalTool { Id = 1, Name = "x" }];

        Assert.Equal(1, raised);
    }

    [Fact]
    public void CommitToは各項目をAppSettingsへ書き写す()
    {
        var (settings, keyMap, quickAccess) = Baseline();
        var draft = SettingsDraft.From(settings, keyMap, Theme.Default, quickAccess);

        draft.Resident = false;
        draft.Theme = Theme.Default with { FontSize = 20f };
        draft.HiddenDrives = ['D'];
        draft.ShowDesktopButton = false;
        draft.QuickAccess.ShowTitles = false;

        draft.CommitTo(settings, quickAccess);

        Assert.False(settings.Resident);
        Assert.Equal(20f, settings.ToTheme().FontSize);
        Assert.Equal(['D'], settings.ToHiddenDrives());
        Assert.False(settings.ShowDesktopButton);
        Assert.False(quickAccess.ShowTitles);
    }

    [Fact]
    public void CommitToはQuickAccessListの実体を差し替えず中身だけ入れ替える()
    {
        var (settings, keyMap, quickAccess) = Baseline();
        var draft = SettingsDraft.From(settings, keyMap, Theme.Default, quickAccess);
        draft.QuickAccess.RemoveAt(0);

        draft.CommitTo(settings, quickAccess);

        Assert.Single(quickAccess.Items);   // ReplaceAll で中身だけ変わる
    }

    [Fact]
    public void CommitToは消したツールを指すブックマークも外す()
    {
        var (settings, keyMap, quickAccess) = Baseline();
        settings.Bookmarks.Bar.Add(new Bookmark("エディタ起動", BookmarkKind.Command,
            new ToolTarget(DefaultExternalTools.EditorId).Serialize()));
        var draft = SettingsDraft.From(settings, keyMap, Theme.Default, quickAccess);

        draft.RemoveTool(DefaultExternalTools.EditorId);
        draft.CommitTo(settings, quickAccess);

        Assert.Empty(settings.Bookmarks.Bar);
    }

    [Fact]
    public void CommitToはツールと割り当てが変わったときだけ変化を伝える()
    {
        var (settings, keyMap, quickAccess) = Baseline();
        var draft = SettingsDraft.From(settings, keyMap, Theme.Default, quickAccess);

        var unchanged = draft.CommitTo(settings, quickAccess);
        Assert.False(unchanged.ExternalToolsChanged);
        Assert.False(unchanged.KeyBindingsChanged);

        draft.RemoveTool(DefaultExternalTools.ViewerId);
        draft.KeyBindings[SpareKey] = new BuiltinTarget(CommandId.Rename);
        var changed = draft.CommitTo(settings, quickAccess);
        Assert.True(changed.ExternalToolsChanged);
        Assert.True(changed.KeyBindingsChanged);
    }
}
