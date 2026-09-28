using System.Reflection;
using System.Runtime.InteropServices;

namespace ReTAC.Domain.Tests;

/// <summary>
/// INV-NO-INTERNET（R-109）: ReTAC 自身のコード（App / Domain / Shell）は通信の API を使わない。
/// インターネットへ出るのはアップデータ（ReTAC.Updater）だけ。
/// 参照するアセンブリと P/Invoke の呼び先で確かめる。COM 経由の通信などはここでは見えないので、レビューで見る。
/// </summary>
public class NoInternetTests
{
    private static readonly Assembly[] ReTacAssemblies =
    [
        typeof(ReTAC.App.MainForm).Assembly,
        typeof(ReTAC.Domain.Commands.CommandId).Assembly,
        typeof(ReTAC.Shell.ShellFileOperation).Assembly,
    ];

    private static readonly string[] NetworkAssemblies =
    [
        "System.Net.Http", "System.Net.Requests", "System.Net.WebClient", "System.Net.Sockets",
        "System.Net.WebSockets.Client", "System.Net.Mail", "System.Net.Quic",
    ];

    private static readonly string[] NetworkLibraries = ["wininet", "winhttp", "ws2_32", "urlmon", "webio"];

    public static TheoryData<string> AssemblyNames() => [.. ReTacAssemblies.Select(a => a.GetName().Name!)];

    private static Assembly ByName(string name) => ReTacAssemblies.Single(a => a.GetName().Name == name);

    [Theory]
    [MemberData(nameof(AssemblyNames))]
    public void 参照するのはReTACと共有フレームワークのアセンブリだけ(string assembly)
    {
        var ownNames = ReTacAssemblies.Select(a => a.GetName().Name).ToHashSet();
        var shared = SharedFrameworkRoot();

        var outside = ByName(assembly).GetReferencedAssemblies()
            .Where(r => !ownNames.Contains(r.Name))
            .Where(r => !Assembly.Load(r).Location.StartsWith(shared, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Name)
            .ToList();

        // ライブラリを足すなら、通信しないことを確かめてから、ここに理由付きの許可を加える
        Assert.True(outside.Count == 0, $"共有フレームワークの外のアセンブリを参照している: {string.Join(", ", outside)}");
    }

    [Theory]
    [MemberData(nameof(AssemblyNames))]
    public void 通信のアセンブリを参照しない(string assembly)
    {
        var found = ByName(assembly).GetReferencedAssemblies()
            .Select(r => r.Name!)
            .Where(n => NetworkAssemblies.Contains(n, StringComparer.OrdinalIgnoreCase))
            .ToList();

        Assert.True(found.Count == 0, $"通信のアセンブリを参照している: {string.Join(", ", found)}");
    }

    [Theory]
    [MemberData(nameof(AssemblyNames))]
    public void PInvokeで通信のDLLを呼ばない(string assembly)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
                                 | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        // LibraryImport も、生成された DllImport のメソッドとしてここに現れる
        var found = ByName(assembly).GetTypes()
            .SelectMany(t => t.GetMethods(all))
            .Where(m => m.Attributes.HasFlag(MethodAttributes.PinvokeImpl))
            .Select(m => (Method: m, Library: m.GetCustomAttribute<DllImportAttribute>()?.Value ?? ""))
            .Where(x => NetworkLibraries.Contains(LibraryName(x.Library)))
            .Select(x => $"{x.Method.DeclaringType?.FullName}.{x.Method.Name} → {x.Library}")
            .ToList();

        Assert.True(found.Count == 0, $"通信の DLL を呼んでいる: {string.Join(", ", found)}");
    }

    [Fact]
    public void PInvokeの呼び先を読めている()
    {
        // 検出の仕組みが働いていることの確かめ。ReTAC.Shell は shell32 を呼んでいるので、0 件なら判定が壊れている
        var libraries = typeof(ReTAC.Shell.ShellFileOperation).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.Attributes.HasFlag(MethodAttributes.PinvokeImpl))
            .Select(m => LibraryName(m.GetCustomAttribute<DllImportAttribute>()?.Value ?? ""));

        Assert.Contains("shell32", libraries);
    }

    private static string LibraryName(string library) =>
        Path.GetFileNameWithoutExtension(library).ToLowerInvariant();

    /// <summary>共有フレームワークの置き場（…\dotnet\shared\）。Microsoft.NETCore.App と Microsoft.WindowsDesktop.App がこの下にある。</summary>
    private static string SharedFrameworkRoot()
    {
        // …\dotnet\shared\Microsoft.NETCore.App\<版>\System.Private.CoreLib.dll から 2 段上がる
        var coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return Directory.GetParent(coreDir)!.Parent!.FullName + Path.DirectorySeparatorChar;
    }
}
