namespace ReTAC.Domain.Entries;

/// <summary>
/// R-117 / INV-THUMBNAIL-NO-CLOUD-DOWNLOAD: クラウドのプレースホルダー（OneDrive などのオンラインのみのファイル・フォルダ）の判定。
/// FileSystemInfo.Attributes は OS の生の値をそのまま持つので、列挙にない RECALL の 2 つのビットも残っている。
/// </summary>
public static class CloudFiles
{
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;

    /// <summary>中身を読むと取り込みが起きうる。サムネイルは OS のキャッシュにあるものだけを使い、作る要求を出さない。</summary>
    public static bool IsPlaceholder(FileAttributes attributes) =>
        (attributes & (RecallOnDataAccess | RecallOnOpen | FileAttributes.Offline)) != 0;
}
