using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using ReTAC.Updater.Core;

namespace ReTAC.Updater;

/// <summary>
/// 引数。ReTAC からは「インストール先のフォルダ」1 つ（版をまたぐ約束）。単体実行では引数なし。
/// 開発用のビルドだけ、試験のための指定を受け付ける。Release のビルドでは「--」で始まる指定を読み飛ばす。
/// </summary>
internal sealed class Options
{
    public string? Install { get; private set; }

    /// <summary>開発用: ローカルの zip・その SHA-256・その版（X.Y.Z）。</summary>
    public string? LocalZip { get; private set; }
    public string? LocalZipSha256 { get; private set; }
    public string? LocalZipVersion { get; private set; }

    /// <summary>開発用: k 個入れ替えたところで止まる。</summary>
    public int? StopAfter { get; private set; }

    /// <summary>開発用: 照合・展開の後で止まる。</summary>
    public bool PauseAfterVerify { get; private set; }

    /// <summary>起動し直すときに引き継ぐ開発用の指定。</summary>
    public IReadOnlyList<string> DebugArgs { get; private set; } = Array.Empty<string>();

    public static Options Parse(IReadOnlyList<string> args)
    {
        var options = new Options();
        var debug = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
#if DEBUG
            if (arg == "--local-zip" && i + 3 < args.Count)
            {
                options.LocalZip = args[i + 1];
                options.LocalZipSha256 = args[i + 2];
                options.LocalZipVersion = args[i + 3];
                debug.AddRange(args.Skip(i).Take(4));
                i += 3;
                continue;
            }
            if (arg == "--debug-stop-after" && i + 1 < args.Count && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var k))
            {
                options.StopAfter = k;
                debug.AddRange(args.Skip(i).Take(2));
                i += 1;
                continue;
            }
            if (arg == "--debug-pause-after-verify")
            {
                options.PauseAfterVerify = true;
                debug.Add(arg);
                continue;
            }
#endif
            // 知らない指定の後ろは読まない（指定の値をインストール先と取り違えないため）
            if (arg.StartsWith("--", StringComparison.Ordinal)) break;
            options.Install ??= Path.GetFullPath(arg).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        options.DebugArgs = debug;
        return options;
    }

    /// <summary>開発用の「止まる」指定を付ける。止まっている間は、作業フォルダの debug-pause が消えるのを待つ。</summary>
    public void ApplyDebugHooks(ReplaceRequest request, string workFolder)
    {
        if (StopAfter is { } k) request.BeforeReplace = i => { if (i == k) Pause(workFolder); };
        if (PauseAfterVerify) request.AfterFetch = () => Pause(workFolder);
    }

    private static void Pause(string workFolder)
    {
        var marker = Path.Combine(workFolder, "debug-pause");
        File.WriteAllText(marker, "このファイルを消すと先へ進む");
        while (File.Exists(marker)) Thread.Sleep(200);
    }

    /// <summary>Windows のコマンドラインの規則で引数を 1 つの文字列にする（空白・引用符・末尾の \ を含むパスのため）。</summary>
    public static string Join(IEnumerable<string> args) => string.Join(" ", args.Select(Quote));

    private static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;
        var text = new StringBuilder("\"");
        var slashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\') { slashes++; continue; }
            text.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            slashes = 0;
            text.Append(c);
        }
        text.Append('\\', slashes * 2);
        return text.Append('"').ToString();
    }
}
