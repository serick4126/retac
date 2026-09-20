using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>R-100: 現在のファイルビューと、その直下だけに出る検索バーを収容する。</summary>
public sealed class FileDisplayPanel : UserControl
{
    public FileListView CurrentView { get; }
    public IncrementalSearchBar SearchBar { get; }

    public FileDisplayPanel(FileListView currentView, IncrementalSearchBar searchBar)
    {
        CurrentView = currentView;
        SearchBar = searchBar;
        Dock = DockStyle.Fill;

        CurrentView.Dock = DockStyle.Fill;
        SearchBar.Dock = DockStyle.Bottom;
        Controls.Add(CurrentView);
        Controls.Add(SearchBar);
        SearchBar.VisibleChanged += (_, _) => ArrangeDocks();
        ArrangeDocks();
    }

    private void ArrangeDocks()
    {
        // 検索バーを表示し直しても、右側パネルの最下部から動かさない。
        Controls.SetChildIndex(CurrentView, 0);
        Controls.SetChildIndex(SearchBar, 1);
    }
}
