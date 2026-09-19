using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;
using ReTAC.Domain.Commands;

namespace ReTAC.App;

/// <summary>
/// R-89: 組み込みコマンドとグループのアイコン。OS の記号フォントのグリフを文字色で描く（テーマと DPI に追従する）。
/// コードポイントは Segoe Fluent Icons と Segoe MDL2 Assets の両方にあるものだけ（実行時に有無を判定できないため、表で固定する）。
/// </summary>
public static class CommandGlyphs
{
    /// <summary>表に無いコマンドの絵（パズルのピース）。</summary>
    private const char Generic = '\uEA86';
    private const char Folder = '\uE8B7';
    private const char Star = '\uE735';

    // ponytail: 同梱フォント（Fluent UI System Icons）は入れていない。OS のフォントに素直な絵が無いものは汎用のグリフ。
    // 見分けが要るという指摘が出たら、Bundled の候補を足す
    private static readonly Dictionary<CommandId, char> Table = new()
    {
        [CommandId.OpenFile] = '\uE8E5',
        [CommandId.CopyToFolder] = '\uE8C8',
        [CommandId.MoveToFolder] = '\uE8DE',
        [CommandId.Delete] = '\uE74D',
        [CommandId.Rename] = '\uE8AC',
        [CommandId.ChangeAttributes] = '\uE8EC',
        [CommandId.CreateShortcut] = '\uE71B',
        [CommandId.CreateFolder] = '\uE8F4',
        [CommandId.Quit] = '\uE8BB',
        [CommandId.QuitAll] = '\uE7E8',
        [CommandId.ToggleAllMarks] = '\uE8B3',
        [CommandId.MarkByWildcard] = '\uE71C',
        [CommandId.GoParent] = '\uE74A',
        [CommandId.GoRoot] = '\uEDA2',
        [CommandId.GoBack] = '\uE72B',
        [CommandId.GoForward] = '\uE72A',
        [CommandId.FolderHistory] = '\uE81C',
        [CommandId.QuickAccess] = '\uE734',
        [CommandId.QuickAccessSettings] = '\uE713',
        [CommandId.QuickAccessAdd] = '\uE710',
        [CommandId.DirectJump] = '\uE8AD',
        [CommandId.SelectDrive] = '\uEDA2',
        [CommandId.DriveByNumberKey] = '\uEDA2',
        [CommandId.GoDesktop] = '\uE7F4',
        [CommandId.SortSettings] = '\uE8CB',
        [CommandId.Refresh] = '\uE72C',
        [CommandId.FileTypeSettings] = '\uE71C',
        [CommandId.ShowProperties] = '\uE946',
        [CommandId.ClipboardCopy] = '\uE8C8',
        [CommandId.ClipboardCut] = '\uE8C6',
        [CommandId.ClipboardPaste] = '\uE77F',
        [CommandId.CopyFileName] = '\uE8C8',
        [CommandId.CopyFileNameWithPath] = '\uE8C8',
        [CommandId.CopyFileNameOnly] = '\uE8C8',
        [CommandId.CopyFileNameWithPathSlash] = '\uE8C8',
        [CommandId.ExternalToolSettings] = '\uE713',
        [CommandId.ShowPopupMenu] = '\uE700',
        [CommandId.RunCommandLine] = '\uE756',
        [CommandId.ShowContextMenu] = '\uE712',
        [CommandId.ExternalToolQueue] = '\uE90F',
        [CommandId.IncrementalSearch] = '\uE721',
        [CommandId.ShowFolderBackgroundMenu] = '\uE712',
        [CommandId.ToggleDriveBar] = '\uEDA2',
        [CommandId.Undo] = '\uE7A7',
        [CommandId.ToggleAddressBar] = '\uE774',
        [CommandId.ToggleBookmarkBar] = '\uE728',
        [CommandId.BookmarkManage] = '\uE728',
        [CommandId.BookmarkAddCurrentFolder] = '\uE734',
        [CommandId.BookmarkAddCursorItem] = '\uE734',
        [CommandId.ColorAndFontSettings] = '\uE790',
        [CommandId.KeyAssignSettings] = '\uE765',
        [CommandId.VisibleDriveSettings] = '\uEDA2',
        [CommandId.EnvironmentSettings] = '\uE713',
        [CommandId.NewWindow] = '\uE78B',
        [CommandId.About] = '\uE946',
    };

    /// <summary>使う記号フォントの名前。どちらも無ければ null（アイコンを出さず、名前だけにする）。</summary>
    public static readonly string? FontName = FindFont();

    private static string? FindFont()
    {
        using var fonts = new InstalledFontCollection();
        var names = fonts.Families.Select(f => f.Name).ToHashSet();
        return new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" }.FirstOrDefault(names.Contains);
    }

    /// <summary>コマンドのグリフ。表に無ければ汎用。</summary>
    public static char GlyphOf(CommandId id) => Table.GetValueOrDefault(id, Generic);

    /// <summary>組み込みコマンドのアイコン。記号フォントが無ければ null。</summary>
    public static Bitmap? For(CommandId id, int size, Color color) => Draw(size, color, (GlyphOf(id), 1f, false));

    /// <summary>グループ: フォルダに星を重ねる。実際のフォルダ（シェルのアイコン）と見分けるため（R-89）。</summary>
    public static Bitmap? Group(int size, Color color) => Draw(size, color, (Folder, 1f, false), (Star, 0.6f, true));

    /// <param name="layers">グリフ・大きさの割合・右下に寄せるか</param>
    private static Bitmap? Draw(int size, Color color, params (char Glyph, float Scale, bool Corner)[] layers)
    {
        if (FontName is null) return null;
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        // 透明な下地に描くので GDI（TextRenderer）ではなく GDI+。GDI は透明度を扱えず、縁が黒くなる
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var brush = new SolidBrush(color);
        using var clear = new SolidBrush(Color.Transparent);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        foreach (var (glyph, scale, corner) in layers)
        {
            var box = size * scale;
            var bounds = corner ? new RectangleF(size - box, size - box, box, box) : new RectangleF(0, 0, box, box);
            using var font = new Font(FontName, box * 0.8f, GraphicsUnit.Pixel);
            if (corner)
            {
                // 下の絵の線と重ならないよう、同じ形を少し太らせて透明で抜いてから描く
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                using var fat = new Font(FontName, box * 0.8f, FontStyle.Bold, GraphicsUnit.Pixel);
                for (var dx = -1; dx <= 1; dx++)
                    for (var dy = -1; dy <= 1; dy++)
                        g.DrawString(glyph.ToString(), fat, clear, new RectangleF(bounds.X + dx, bounds.Y + dy, box, box), format);
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
            }
            g.DrawString(glyph.ToString(), font, brush, bounds, format);
        }
        return bitmap;
    }
}
