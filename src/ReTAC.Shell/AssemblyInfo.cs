using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ReTAC.Domain.Tests")]
// R-97-2: DriveTreeView がマウス確定の純粋な判定（NameSpaceTreePolicy.ShouldCommit）を呼ぶため。
// ReTAC.App プロジェクトの AssemblyName は "ReTAC"（埋め込みリソース名を固定するため）。
[assembly: InternalsVisibleTo("ReTAC")]
