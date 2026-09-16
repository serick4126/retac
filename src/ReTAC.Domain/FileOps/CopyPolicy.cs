namespace ReTAC.Domain.FileOps;

/// <summary>R-41-5: 同名衝突時の複写条件。既定は「新しい時に複写」。</summary>
public enum CopyCondition
{
    /// <summary>コピー元の更新日時が新しいときだけ複写する（R-41-4 の中核）。</summary>
    NewerOnly,
    Overwrite,
    Skip,
    RenameCopy,
}

/// <param name="Transfer">OS に渡して実際に転送するか。</param>
/// <param name="RenameTarget">別名で複写するか（Transfer が true のときのみ意味を持つ）。</param>
public readonly record struct TransferDecision(bool Transfer, bool RenameTarget);

/// <summary>
/// R-41-4: 同名衝突時の複写条件を本システムが判定し、実際に転送すべき対象だけを OS に渡す。
/// OS の衝突解決 UI に丸投げしない。
/// </summary>
public static class ConflictResolver
{
    public const CopyCondition Default = CopyCondition.NewerOnly;

    /// <param name="destinationLastWrite">宛先が存在しない場合は null（衝突なし）。</param>
    public static TransferDecision Decide(
        DateTime sourceLastWrite,
        DateTime? destinationLastWrite,
        CopyCondition condition)
    {
        if (destinationLastWrite is not { } dest)
            return new TransferDecision(Transfer: true, RenameTarget: false);

        return condition switch
        {
            CopyCondition.NewerOnly => new TransferDecision(sourceLastWrite > dest, false),
            CopyCondition.Overwrite => new TransferDecision(true, false),
            CopyCondition.Skip => new TransferDecision(false, false),
            CopyCondition.RenameCopy => new TransferDecision(true, true),
            _ => new TransferDecision(false, false),
        };
    }
}
