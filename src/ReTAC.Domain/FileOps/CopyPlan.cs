using System.IO;

namespace ReTAC.Domain.FileOps;

/// <summary>R-125: 宛先の項目の種類。</summary>
public enum DestinationKind { Absent, File, Folder }

/// <summary>
/// R-125: 宛先の状態。計画は項目ごとに計画の時点の状態を持ち、OS が項目を転送する直前に今の状態と比べる。
/// 複写条件（R-41-4）の判断は計画の時点の宛先に対するものなので、違っていたら転送しない。
/// <b>計画の時点も直前も、親のフォルダの一覧から取る。</b>パスを直に問い合わせる方法と混ぜると、NTFS が一覧に古い日時・サイズを
/// 返す場合（書き込み中・ハードリンク）に、変わっていないのに違うと判定してしまう。
/// </summary>
public readonly record struct DestinationState(DestinationKind Kind, DateTime WriteTimeUtc = default, long Size = 0)
{
    public static readonly DestinationState Absent = new(DestinationKind.Absent);
    public static readonly DestinationState Folder = new(DestinationKind.Folder);

    public static DestinationState OfFile(FileInfo file) => new(DestinationKind.File, file.LastWriteTimeUtc, file.Length);

    /// <summary>一覧で得た項目の状態。</summary>
    public static DestinationState Of(FileSystemInfo info) => info is FileInfo file ? OfFile(file) : Folder;

    /// <summary>その名前だけを一覧する。隠し・システムの項目も含める（既定では飛ばされる）。* と ? 以外は特別に読まない。</summary>
    private static readonly EnumerationOptions ExactName = new()
    {
        MatchType = MatchType.Simple, MatchCasing = MatchCasing.CaseInsensitive, AttributesToSkip = 0, IgnoreInaccessible = false,
    };

    /// <summary>今の状態。親のフォルダをその名前で一覧して取る（1 往復）。親が無ければ「無い」。読めないときは例外をそのまま出す。</summary>
    public static DestinationState Of(string path)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        var (parent, name) = (Path.GetDirectoryName(trimmed), Path.GetFileName(trimmed));
        // ドライブのルートのように親の無いパスは一覧できない
        if (string.IsNullOrEmpty(parent) || name.Length == 0) return Directory.Exists(path) ? Folder : Absent;
        try
        {
            foreach (var info in new DirectoryInfo(parent).EnumerateFileSystemInfos(name, ExactName)) return Of(info);
            return Absent;
        }
        catch (DirectoryNotFoundException) { return Absent; }
    }
}

/// <summary>転送のために ReTAC が作る、または既にあって中をたどった、宛先のフォルダ。</summary>
/// <param name="Expected">計画の時点の状態。Folder なら既にあり、Absent なら作る。File なら同じ名前のファイルがある（作ろうとして失敗する。今までと同じ）</param>
/// <param name="Source">
/// 中をたどった転送元のフォルダ。移動のあと、空になっていたら消す対象（R-125）。
/// 作るだけのフォルダ（宛先そのもの）と、リンクの所（中をたどらない）は null
/// </param>
public sealed record PlannedFolder(string FullPath, DestinationState Expected, string? Source);

/// <summary>転送 1 件。</summary>
/// <param name="Source">転送元のファイル。WholeFolder のときはフォルダ</param>
/// <param name="DestinationFolder">転送先のフォルダ</param>
/// <param name="NewName">別名で複写する場合の名前。元の名前のままなら null</param>
public sealed record CopyPlanItem(string Source, string DestinationFolder, string? NewName = null)
{
    /// <summary>R-125: 計画の時点の、転送先（<see cref="Target"/>）の状態。File なら上書きする項目。</summary>
    public DestinationState Expected { get; init; } = DestinationState.Absent;

    /// <summary>R-125: フォルダごと 1 件として OS に渡す（宛先に同じ名前の項目が無く、配下に再解析ポイントも無い）。</summary>
    public bool WholeFolder { get; init; }

    /// <summary>転送先のパス。</summary>
    public string Target => Path.Combine(DestinationFolder, NewName ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(Source)));
}

/// <param name="Folders">先に作っておく必要のあるフォルダと、既にあって中をたどったフォルダ（浅い順）</param>
/// <param name="Items">実際に OS へ渡す転送</param>
/// <param name="Skipped">複写条件によって転送しないと判断した件数</param>
/// <param name="Conflicts">同名衝突が起きた件数</param>
public sealed record CopyPlan(IReadOnlyList<PlannedFolder> Folders, IReadOnlyList<CopyPlanItem> Items, int Skipped,
                              int Conflicts = 0);

/// <summary>
/// R-41-4: 同名衝突時の複写条件を自前で判定し、<b>実際に転送すべき対象だけ</b>を OS に渡すための計画を作る。
/// R-125: 宛先はフォルダごとに 1 回だけ一覧する（ファイルごとに存在を問い合わせると、ネットワークドライブでは
/// ファイルの数だけ往復して、転送が始まるまでが長くなる）。宛先に無いフォルダは中をたどらず、フォルダごと 1 件にする。
/// 宛先・転送元が読めないとき（権限・切断）は、例外をそのまま出す（呼び出し側がエラーとして知らせる）。
/// </summary>
public static class CopyPlanner
{
    /// <summary>衝突 1 件。16.8 節のダイアログは元と先の日時・サイズを並べて示す。</summary>
    public sealed record Conflict(string Source, string Destination,
        DateTime SourceTime, DateTime DestinationTime, long SourceSize, long DestinationSize);

    /// <summary>衝突条件を一括で決める場合（R-41-6 のチェックボックス）。</summary>
    public static CopyPlan Build(IEnumerable<string> sources, string destinationFolder, CopyCondition condition,
        Action<int>? scanned = null, bool moving = false) =>
        Build(sources, destinationFolder, _ => condition, scanned, moving);

    /// <param name="resolve">
    /// 衝突するたびに呼ばれ、その 1 件の複写条件を返す（R-41-5）。
    /// 「以降すべてに適用」は呼び出し側が同じ値を返し続けることで表す。
    /// </param>
    /// <param name="scanned">たどった転送元のフォルダの数（ステータスバーの「調べています」用）</param>
    /// <param name="moving">移動か。同じドライブの中の移動は、リンクがあってもフォルダごと渡す（<see cref="SameVolume"/>）</param>
    public static CopyPlan Build(IEnumerable<string> sources, string destinationFolder,
        Func<Conflict, CopyCondition> resolve, Action<int>? scanned = null, bool moving = false)
    {
        var builder = new Builder(resolve, scanned, moving);
        var (listing, exists) = Listing(destinationFolder);
        // 宛先のフォルダそのものが無ければ、フォルダごと渡す前に作る（OS は無いフォルダを宛先にできない）
        if (!exists) builder.Folders.Add(new PlannedFolder(destinationFolder, DestinationState.Absent, null));

        // ponytail: 一番上の項目は 1 件ずつ問い合わせる。ネットワークの転送元から数千件をマークして送るのが遅ければ、親のフォルダごとに一覧する
        foreach (var source in sources)
        {
            var trimmed = Path.TrimEndingDirectorySeparator(source);
            if (Directory.Exists(source))
            {
                // 名前の無い転送元（ドライブのルート）は、宛先そのものへ中身を入れる。フォルダの項目は足さない
                // （足すと、宛先が無いときに同じパスが「無い」と「フォルダ」の 2 件で計画に入る）
                if (Path.GetFileName(trimmed).Length == 0) builder.Walk(new DirectoryInfo(source), destinationFolder, listing);
                else builder.AddFolder(new DirectoryInfo(trimmed), destinationFolder, listing);
            }
            else if (File.Exists(source))
            {
                builder.AddFile(new FileInfo(source), destinationFolder, listing);
            }
        }

        return new CopyPlan(builder.Folders, builder.Items, builder.Skipped, builder.Conflicts);
    }

    /// <summary>
    /// 同じドライブ（パスのルートが同じ）か。同じドライブの中の移動は名前の付け替えで済み、OS はリンクの先をたどらない。
    /// ponytail: ルートで見るだけ。フォルダに別のボリュームをマウントしている場合は見分けない（その移動は今までも OS 任せ）
    /// </summary>
    public static bool SameVolume(string source, string destination) =>
        Path.GetPathRoot(source) is { Length: > 0 } root
        && string.Equals(root, Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase);

    /// <summary>フォルダの中のすべての項目。隠し・システムの項目も含める。</summary>
    private static readonly EnumerationOptions All = new() { AttributesToSkip = 0, IgnoreInaccessible = false };

    /// <summary>
    /// 宛先のフォルダを 1 回だけ一覧する。
    /// <b>「無い」と「読めない」を分ける</b>: 無ければ空の一覧（Exists は false）。読めない（権限・切断・フォルダでない）ときは例外をそのまま出す。
    /// 読めない宛先を「無い」として計画に入れると、あとで「転送先が変わった」と誤って知らせることになる。
    /// </summary>
    private static (Dictionary<string, FileSystemInfo> Entries, bool Exists) Listing(string folder)
    {
        var map = new Dictionary<string, FileSystemInfo>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var info in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", All)) map[info.Name] = info;
            return (map, true);
        }
        catch (DirectoryNotFoundException)
        {
            if (File.Exists(folder)) throw;   // フォルダでなくファイルがある。無いのとは違う
            return (map, false);
        }
    }

    private static DestinationState StateOf(Dictionary<string, FileSystemInfo> listing, string name) =>
        listing.TryGetValue(name, out var info) ? DestinationState.Of(info) : DestinationState.Absent;

    private sealed class Builder(Func<Conflict, CopyCondition> resolve, Action<int>? scanned, bool moving)
    {
        public List<PlannedFolder> Folders { get; } = [];
        public List<CopyPlanItem> Items { get; } = [];
        public int Skipped { get; private set; }
        public int Conflicts { get; private set; }
        private int _folders;

        /// <param name="parentListing">
        /// 転送先のフォルダの一覧。<b>この計画が転送する項目も足していく</b>（転送したあとの宛先の姿）。
        /// 同じ宛先になる項目が 2 つあるとき（別のフォルダの同じ名前・別名と転送元の名前の重なり）、後の項目を普通の衝突として判定するため
        /// </param>
        public void AddFolder(DirectoryInfo source, string destinationFolder, Dictionary<string, FileSystemInfo> parentListing)
        {
            var target = Path.Combine(destinationFolder, source.Name);
            var state = StateOf(parentListing, source.Name);

            // R-125: 同じドライブの中の移動は名前の付け替え。OS はリンクの先をたどらず、リンクそのものを移す。
            // 宛先に同じ名前の項目が無ければ、配下にリンクがあっても（このフォルダ自身がリンクでも）フォルダごと渡す（今までの動作）。
            // 1 件ずつに分けると、リンクが転送元に残り、転送元のフォルダも消えず、不完全な移動が「完了」になる
            var renames = moving && SameVolume(source.FullName, target);
            if (state.Kind == DestinationKind.Absent && renames) { AddWhole(); return; }

            // ジャンクション／シンボリックリンクは辿らない（理由は TransferGuards.CanDescend）。今までどおり、空のフォルダだけを作る
            if (source.Attributes.HasFlag(FileAttributes.ReparsePoint)) { Folders.Add(new PlannedFolder(target, state, null)); return; }

            // R-125: 宛先に同じ名前の項目が無ければ、中で衝突は起きない。配下にリンクが無ければフォルダごと OS に渡す
            // （コピーと、ドライブをまたぐ移動。リンクがあるのにフォルダごと渡すと、OS がリンクの先をコピーするおそれがある）
            if (state.Kind == DestinationKind.Absent && !HasReparsePointBelow(source)) { AddWhole(); return; }

            void AddWhole()
            {
                Items.Add(new CopyPlanItem(source.FullName, destinationFolder) { WholeFolder = true });
                parentListing[source.Name] = source;
            }

            Folders.Add(new PlannedFolder(target, state, source.FullName));
            Walk(source, target, state.Kind == DestinationKind.Folder ? Listing(target).Entries : new(StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>転送元のフォルダの中を、宛先のフォルダ（target）へ入れる。</summary>
        public void Walk(DirectoryInfo source, string target, Dictionary<string, FileSystemInfo> listing)
        {
            scanned?.Invoke(++_folders);
            // 転送元も 1 回の一覧で、ファイルとフォルダの両方を取る（属性・日時・サイズは一覧で得たものを使う）
            var children = source.EnumerateFileSystemInfos("*", All).ToList();
            foreach (var file in children.OfType<FileInfo>()) AddFile(file, target, listing);
            foreach (var directory in children.OfType<DirectoryInfo>()) AddFolder(directory, target, listing);
        }

        public void AddFile(FileInfo source, string destinationFolder, Dictionary<string, FileSystemInfo> listing)
        {
            var state = StateOf(listing, source.Name);

            // 宛先に同じ名前のファイルが無ければ衝突なし。同じ名前のフォルダがあるときも、今までどおり衝突にしない
            // （OS が転送の失敗として知らせる）。状態は持たせ、転送の直前に変わっていないかを確かめる
            if (state.Kind != DestinationKind.File)
            {
                Items.Add(new CopyPlanItem(source.FullName, destinationFolder) { Expected = state });
                if (state.Kind == DestinationKind.Absent) listing[source.Name] = source;
                return;
            }

            Conflicts++;
            // 一覧の項目は、宛先に元からあるファイルか、この計画が先に転送するファイル。どちらも日時とサイズを持つ
            var existing = (FileInfo)listing[source.Name];
            var condition = resolve(new Conflict(source.FullName, Path.Combine(destinationFolder, source.Name),
                source.LastWriteTime, existing.LastWriteTime, source.Length, existing.Length));
            var decision = ConflictResolver.Decide(source.LastWriteTime, existing.LastWriteTime, condition);

            if (!decision.Transfer) { Skipped++; return; }
            if (decision.RenameTarget)
            {
                var newName = UniqueName(listing, source.Name);
                Items.Add(new CopyPlanItem(source.FullName, destinationFolder, newName));   // 別名の宛先は「無い」状態
                listing[newName] = source;
                return;
            }
            Items.Add(new CopyPlanItem(source.FullName, destinationFolder) { Expected = state });
            listing[source.Name] = source;   // 上書きしたあとの宛先の姿
        }

        /// <summary>
        /// 配下（何段下でも）に再解析ポイントのフォルダがあるか。フォルダごとに 1 回の一覧で分かる。
        /// 読めないフォルダがあれば、確かめられないので「ある」とする（フォルダごと渡さない。1 件ずつたどる側が、読めないことをエラーとして出す）。
        /// ponytail: リンクを含む、宛先に無いフォルダでは、階層の数だけ同じ枝を読み直す。遅ければ 1 回の走査で印を付ける形にする
        /// </summary>
        private bool HasReparsePointBelow(DirectoryInfo folder)
        {
            try
            {
                scanned?.Invoke(++_folders);
                foreach (var child in folder.EnumerateDirectories("*", All))
                {
                    if (child.Attributes.HasFlag(FileAttributes.ReparsePoint)) return true;
                    if (HasReparsePointBelow(child)) return true;
                }
                return false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
        }
    }

    /// <summary>「名前を変更し複写」。エクスプローラーと同じ `名前 (2).拡張子` の作法。宛先の一覧から空いている番号を探す。</summary>
    private static string UniqueName(Dictionary<string, FileSystemInfo> listing, string name)
    {
        var baseName = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var n = 2; ; n++)
        {
            var candidate = $"{baseName} ({n}){extension}";
            if (!listing.ContainsKey(candidate)) return candidate;
        }
    }
}
