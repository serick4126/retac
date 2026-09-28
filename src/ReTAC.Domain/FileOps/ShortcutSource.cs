using System.IO;

namespace ReTAC.Domain.FileOps;

/// <summary>R-58 / R-111-3: ショートカットの名前の元（拡張子込み）と、作業フォルダ（リンク元のフォルダ。ルートなら null）。</summary>
public static class ShortcutSource
{
    public static (string Name, string? Folder) Of(string path)
    {
        var root = Path.GetPathRoot(path) ?? "";
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        if (root.Length > 0 && string.Equals(Path.TrimEndingDirectorySeparator(root).TrimEnd('\\'), trimmed.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            // ルート: C:\ → C、\\server\share → share
            var parts = trimmed.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            return (parts[^1].TrimEnd(':'), null);
        }
        return (Path.GetFileName(trimmed), Path.GetDirectoryName(trimmed));
    }
}
