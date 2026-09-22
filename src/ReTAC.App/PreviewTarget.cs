using ReTAC.Domain.Entries;

namespace ReTAC.App;

/// <summary>R-99: プレビューの対象と、技術ゲートで決めた待ち時間。</summary>
public static class PreviewTarget
{
    /// <summary>キーボードでカーソルを動かしたときは、この間止まった位置だけを読む（Q31）。</summary>
    public const int KeyboardDelayMs = 250;

    /// <summary>要求の処理開始からこれを越えたら「読み込んでいます」を出す（Q67）。</summary>
    public const int LoadingNoticeMs = 500;

    /// <summary>終了時に、完了済みのワーカー 1 本を待つ上限。応答しないワーカーは待たない。</summary>
    public const int ShutdownWaitMs = 1000;

    /// <summary>
    /// R-99 / Q11: マークに関係なく、カーソル位置のファイル 1 件。フォルダ・親フォルダ行・カーソル無しは対象なし（null）。
    /// </summary>
    public static string? Of(Entry? cursor) => cursor is { Kind: EntryKind.File } ? cursor.FullPath : null;
}
