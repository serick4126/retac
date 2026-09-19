using Microsoft.Win32;

namespace ReTAC.Shell;

/// <summary>
/// 外部ツールのパスの解決。名前だけ（<c>git.exe</c>）なら <c>PATH</c> と App Paths
/// （アプリがレジストリに登録する場所）から探す。「終了後もウィンドウを閉じない」はコンソールか GUI かを
/// 見分けるので、ReTAC が実体を知っている必要がある。
/// </summary>
public static class ExecutableResolver
{
    private const string DefaultPathExt = ".COM;.EXE;.BAT;.CMD";

    public static string? Resolve(string pathOrName) =>
        Resolve(pathOrName, Environment.GetEnvironmentVariable("PATH"), Environment.GetEnvironmentVariable("PATHEXT"), AppPath);

    /// <param name="appPath">名前（拡張子つき）から App Paths に登録されたパスを引く。テストで差し替える</param>
    /// <returns>見つかった実行ファイルのフルパス。見つからなければ null</returns>
    public static string? Resolve(string pathOrName, string? pathVariable, string? pathExt, Func<string, string?> appPath)
    {
        var name = pathOrName.Trim().Trim('"');
        if (name.Length == 0) return null;

        // フォルダを含む書き方は、そこに在るかどうかだけ
        if (name.Contains('\\') || name.Contains('/') || Path.IsPathRooted(name))
            return File.Exists(name) ? Path.GetFullPath(name) : null;

        var candidates = Path.HasExtension(name)
            ? new[] { name }
            : Extensions(pathExt).Select(ext => name + ext).ToArray();

        foreach (var dir in (pathVariable ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var candidate in candidates)
            {
                var full = Path.Combine(Environment.ExpandEnvironmentVariables(dir.Trim('"')), candidate);
                if (File.Exists(full)) return full;
            }
        }

        foreach (var candidate in candidates)
            if (appPath(candidate) is { } registered && File.Exists(registered)) return registered;

        return null;
    }

    /// <summary>Windows が実行する種類か。<c>build.ps1</c> や <c>script.py</c> は関連付けで開かれるだけ。</summary>
    public static bool IsExecutableType(string path) =>
        IsExecutableType(path, Environment.GetEnvironmentVariable("PATHEXT"));

    public static bool IsExecutableType(string path, string? pathExt) =>
        Extensions(pathExt).Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static string[] Extensions(string? pathExt) =>
        (string.IsNullOrWhiteSpace(pathExt) ? DefaultPathExt : pathExt)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? AppPath(string name)
    {
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var key = root.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{name}");
                if (key?.GetValue(null) is string value && value.Length > 0)
                    return Environment.ExpandEnvironmentVariables(value.Trim('"'));
            }
            // レジストリを読めない環境（制限されたアカウント等）では見つからなかったものとして扱う（V-03）
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
            }
        }
        return null;
    }
}

public enum ExecutableKind { Console, Gui, Unknown }

/// <summary>実行ファイルの見出し（PE ヘッダ）から、コンソールか GUI かを読む（F-03）。</summary>
public static class ExecutableKinds
{
    private const ushort Mz = 0x5A4D;          // "MZ"
    private const uint PeSignature = 0x00004550; // "PE\0\0"
    /// <summary>PE 署名（4）＋ COFF ヘッダ（20）＋ オプションヘッダ内の Subsystem の位置（68）。PE32 と PE32+ で同じ</summary>
    private const int SubsystemOffset = 0x5C;

    /// <returns>読めないもの（<c>.bat</c>・存在しない・壊れている）は Unknown。呼び出し側はコンソールとして扱う</returns>
    public static ExecutableKind Of(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 0x40 || reader.ReadUInt16() != Mz) return ExecutableKind.Unknown;

            stream.Position = 0x3C;
            var pe = reader.ReadInt32();
            if (pe <= 0 || pe + SubsystemOffset + 2 > stream.Length) return ExecutableKind.Unknown;

            stream.Position = pe;
            if (reader.ReadUInt32() != PeSignature) return ExecutableKind.Unknown;

            stream.Position = pe + SubsystemOffset;
            return reader.ReadUInt16() switch
            {
                2 => ExecutableKind.Gui,        // IMAGE_SUBSYSTEM_WINDOWS_GUI
                3 => ExecutableKind.Console,    // IMAGE_SUBSYSTEM_WINDOWS_CUI
                _ => ExecutableKind.Unknown,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return ExecutableKind.Unknown;
        }
    }
}
