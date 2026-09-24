using System.Runtime.CompilerServices;

// SettingsDialog の確定操作（OK・適用・キャンセル・ページ切り替え）を ShowDialog なしで
// テストするための internal な入口を、テストプロジェクトから呼べるようにする。
[assembly: InternalsVisibleTo("ReTAC.Domain.Tests")]
