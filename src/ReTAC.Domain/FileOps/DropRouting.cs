using ReTAC.Domain.Entries;

namespace ReTAC.Domain.FileOps;

/// <summary>R-111-2: ドロップを受ける所。ブックマークの項目の間（ブックマークへの追加）は転送ではないので含めない。</summary>
public enum DropReceptacle { FileList, DriveTree, DesktopTree, DriveBar, AddressBar, BookmarkFolder }

/// <summary>宛先入りのダイアログを出すか、出さずに転送するか。</summary>
public enum DropRoute { Dialog, Direct }

public readonly record struct DropPlan(DropRoute Route, bool ShowMenu);

/// <summary>R-111-2: 右ボタンのメニューで選んだもの。Esc・メニューの外のクリックも Cancel。</summary>
public enum DropChoice { Cancel, Copy, Move, Link }

/// <summary>R-111-2: 右ボタンのメニューの太字の項目と、使える項目。</summary>
public readonly record struct DropMenuModel(DropAction Default, bool CopyEnabled, bool MoveEnabled, bool LinkEnabled);

/// <summary>R-110-1 / R-111-2: ReTAC の受け口に落とされたときの経路・宛先・右ボタンのメニュー。</summary>
public static class DropRouting
{
    /// <summary>
    /// INV-INPANEL-DROP-VIA-DIALOG / INV-RIGHT-DROP-SAME-ROUTE: 経路は受け口とドラッグ元で決まり、ボタンでは変わらない。
    /// ReTAC から始めたドラッグは、ファイルリストでもダイアログを経る（ツリーから始めたもの・同じプロセスの別のウィンドウから始めたものを含む。Q4 / Q6）。
    /// ファイルリストから同じリストへ落としたものは、宛先が同じフォルダなので DropRules が何もしない。
    /// ドライブバーは ReTAC からでもダイアログを出さない（既存の動作。揃えるなら別の課題）。
    /// </summary>
    /// <param name="fromReTAC">同じプロセスで始めたドラッグ（ファイルリスト・ツリー）</param>
    public static DropPlan Plan(DropReceptacle receptacle, bool fromReTAC, bool rightButton)
    {
        var route = receptacle switch
        {
            DropReceptacle.FileList => fromReTAC ? DropRoute.Dialog : DropRoute.Direct,
            DropReceptacle.DriveBar => DropRoute.Direct,
            _ => DropRoute.Dialog,
        };
        return new DropPlan(route, rightButton);
    }

    /// <summary>R-110-1: 落とした項目そのものが宛先になるか（設定オンで、フォルダか親フォルダの項目。Q2）。</summary>
    public static bool TargetsItem(bool inPanelDragDrop, Entry? hit) =>
        inPanelDragDrop && hit is { Kind: EntryKind.Folder or EntryKind.Parent };

    /// <summary>R-110-1: ファイルリストに落としたときの宛先。それ以外は今のフォルダ。</summary>
    public static string FileListDestination(bool inPanelDragDrop, Entry? hit, string currentFolder) =>
        TargetsItem(inPanelDragDrop, hit) ? hit!.FullPath : currentFolder;

    /// <summary>
    /// R-111-2: 宛先の不可と、太字の項目を分けて決める。
    /// メニューを出さない（null）のは、宛先が「不可」の所（同じフォルダ・自分自身・自分の中）か、ドラッグ元が何も許さないときだけ。
    /// 許さない項目は灰色にする。太字は左ボタンで落としたときに起きるほう（修飾キーなし。先頭の項目で決める。DropFeedback と同じ）。
    /// 左ボタンでは何も起きない（別のドライブで移動だけ・リンクだけ）ときは太字なし（Default = None）。
    /// </summary>
    public static DropMenuModel? Menu(string firstSource, string destination, bool copyAllowed, bool moveAllowed, bool linkAllowed)
    {
        var decided = DropRules.Decide(firstSource, destination, ctrl: false, shift: false);
        if (decided == DropAction.None || !(copyAllowed || moveAllowed || linkAllowed)) return null;
        return new DropMenuModel(DropRules.Allow(decided, copyAllowed, moveAllowed), copyAllowed, moveAllowed, linkAllowed);
    }

    /// <summary>
    /// R-111-2: 右ボタンのドラッグ中に受け口が返す効果。効果が None だと OLE は Drop を呼ばないので、
    /// 左ボタンでは何も起きない所でも、メニューが出る所なら落とせる効果を返す。太字があればそれ、無ければ使える項目の先頭。
    /// </summary>
    public static DropChoice RightDragEffect(DropMenuModel model) => model.Default switch
    {
        DropAction.Copy => DropChoice.Copy,
        DropAction.Move => DropChoice.Move,
        _ => model.CopyEnabled ? DropChoice.Copy : model.MoveEnabled ? DropChoice.Move : DropChoice.Link,
    };

    /// <summary>R-111-2: メニューで選んだ操作を、すべての項目に効く修飾キーに読み替える（Ctrl はコピー、Shift は移動）。</summary>
    public static (bool Ctrl, bool Shift) ModifiersFor(DropChoice choice) => choice switch
    {
        DropChoice.Copy => (true, false),
        DropChoice.Move => (false, true),
        _ => throw new ArgumentOutOfRangeException(nameof(choice)),
    };
}
