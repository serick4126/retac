using System;
using System.Globalization;

namespace ReTAC.Updater.Core;

/// <summary>
/// R-109-5: 昇格したプロセスが progress.txt に書く 1 行。<b>表示と「止まっていないか」の判断にだけ使う</b>
/// （通常の権限から書き換えられるので、結果の判定には使わない）。
/// 形: 時刻 \t 段 \t ファイル（または詳細） \t やり直せる時刻。値の中のタブと改行は空白にする。
/// </summary>
public sealed class ProgressLine
{
    public const string EndStage = "終わり";

    private ProgressLine(string stage, string? file, string? retryAt)
    {
        Stage = stage;
        File = file;
        RetryAt = retryAt;
    }

    public string Stage { get; }

    /// <summary>処理中のファイル名。終わりの行では、表示に添える詳細。</summary>
    public string? File { get; }

    /// <summary>終わりの行で、レート制限のやり直せる時刻。</summary>
    public string? RetryAt { get; }

    public bool IsEnd => Stage == EndStage;

    public static string Format(string stage, string? file) =>
        string.Join("\t", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), Clean(stage), Clean(file), "");

    public static string End(string? detail, string? retryAt) =>
        string.Join("\t", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), EndStage, Clean(detail), Clean(retryAt));

    /// <summary>読めなければ null。</summary>
    public static ProgressLine? Parse(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var parts = raw!.TrimEnd('\r', '\n').Split('\t');
        if (parts.Length < 3 || parts[1].Length == 0) return null;
        return new ProgressLine(parts[1], Empty(parts[2]), parts.Length >= 4 ? Empty(parts[3]) : null);
    }

    private static string Clean(string? value) =>
        (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private static string? Empty(string value) => value.Length == 0 ? null : value;
}
