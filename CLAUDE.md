# ReTAC

Windows 用のファイラー。旧「卓駆★」（TAC32）の操作体系を .NET 10 / WinForms で作り直したもの。

| プロジェクト | 役割 |
|---|---|
| `src/ReTAC.Domain` | 純粋なドメイン型と判定ロジック。副作用なし。外部依存なし |
| `src/ReTAC.Shell` | Win32 シェル API（COM / P-Invoke）のラッパー |
| `src/ReTAC.App` | WinForms の画面・ダイアログ・設定の入出力 |
| `tests/ReTAC.Domain.Tests` | xunit。`dotnet test` で全部走る |

データベースは無い。永続化は実行ファイル横の `retac.settings.json` 1 ファイルだけ。

## docs/ は別リポジトリ

`docs/` は親リポジトリの `.gitignore` に入っており、その中が独立した git リポジトリになっている。
仕様書・計画書を足したり直したりしたら、**`docs/` へ降りてそちらでコミットする**。
親リポジトリで `git add docs/...` は通らない（`-f` を付けて押し込まない）。
コードと仕様書の両方を変更したときは、2 つのリポジトリでそれぞれコミットする。

## スキーマ・マニフェスト

このプロジェクトの構造上の契約は `schema/` にある。読み方は `schema/README.md`。

- `schema/manifest.json` — 自動生成。**手で編集しない**（再生成で失われる）
- `schema/invariants.json` — 手書き。コードから導けない設計方針と不変条件
- 検証: `dotnet test` ／ 更新: `RETAC_SCHEMA_UPDATE=1 dotnet test`

### manifest.json は全文を読まない

148 KB あり、全文を読むとそれだけでコンテキストを数万トークン食う。**必ず必要な箇所だけ引く。**
整形済み JSON なので `grep -A` で足りる。`invariants.json`（8 KB）は全文を読んでよい。

**`jq` を使う。** 欲しい項目だけに絞れるので、型 1 件を丸ごと読む 1,820 bytes が 195 bytes で済む。

```sh
M=schema/manifest.json

# 型の中身（実装中に要るのはたいていこれだけ）
jq -r '.types["ReTAC.Domain.Entries.Entry"] | "\(.kind) \(.file)",
       (.fields[] | "  \(.name): \(.type)\(if .nullable then "?" else "" end)\(if .required then " required" else "" end)")' $M

jq '.types["ReTAC.Domain.Entries.Entry"]' $M                       # 型 1 件の全部
jq -c '.settings.keys[] | select(.key=="KeyBindings")' $M          # 設定項目 1 件
jq -r '.specIndex["F-09"][] | "\(.file):\(.line)"' $M              # 仕様 ID の出現箇所
jq -r '.wiring.commands[] | select(.id=="OpenFile")' $M            # コマンド 1 件

# 横断して調べる（grep では全部読む羽目になるもの）
jq -r '.edges[] | select(.onDelete=="UNENFORCED")
       | "\(.from)#\(.fk.field) -> \(.to)"' $M                     # 型で守られない参照
jq -r '.edges[] | select(.to=="type:ReTAC.Domain.Tools.ExternalTool")
       | "\(.from)#\(.fk.field) [\(.type)]"' $M                    # ある型を指すエッジ
jq -r '.edges[] | select(.fk.unique==false) | "\(.from)#\(.fk.field)"' $M   # 重複を許すコレクション
jq -r '[.wiring.commands[] | select(.defaultKeys==[]) | .id] | join(", ")' $M  # 既定キーが無いコマンド
```

`jq` が無い環境では `sed` の範囲指定で切り出す。型 1 件は 5〜75 行と幅があるので、
`grep -A <n>` は窓を外す（`-A 20` だと `Entry` が途中で切れる）。

```sh
sed -n '/^    "ReTAC.Domain.Entries.Entry": {/,/^    },\?$/p' schema/manifest.json  # 型 1 件
grep -n -A 8  '"key": "KeyBindings"' schema/manifest.json   # 設定項目 1 件
grep -n -A 10 '"F-09": \['           schema/manifest.json   # 仕様 ID の出現箇所
grep -n -B 2 -A 8 'UNENFORCED'       schema/manifest.json   # 型で守られない参照
```

セクションの大きさの目安は `types` 48 KB / `specIndex` 50 KB / `wiring` 25 KB / `edges` 8 KB / `settings` 4.5 KB。
`settings` と `edges` は小さいので、必要ならセクションごと読んでよい。

### 実装前に必ず行うこと

1. 型・設定項目・コマンド・キー割り当てのいずれかを追加または変更する前に、
   `manifest.json` の該当箇所（上のやり方で引く）と `invariants.json` の全件を読む。
2. 既存の命名規則・カーディナリティ・既定値の方針と矛盾しないことを確認する。
   特に `edges` の `"onDelete": "UNENFORCED"` が付いた参照は、型システムが守ってくれない箇所である。
3. 触る対象に仕様 ID（`R-04` 形式）が付いている場合、`specIndex` でその ID の全出現箇所を引き、
   影響範囲を確認してから変更する。1 箇所だけ直して他を放置しない。

### 実装後に必ず行うこと

4. スキーマに影響する変更をしたら `RETAC_SCHEMA_UPDATE=1 dotnet test` を実行し、
   `manifest.json` の差分をコミットに含める。差分が意図と違う場合は実装側が間違っている。
5. 新しい不変条件を導入したら `invariants.json` に追記する。コードのコメントだけで済ませない。
6. lint が「制約の欠落」の候補を報告したら、`invariants.json` に次のいずれかで分類する。
   分類しないとテストは通らない。**面倒だからという理由で `accepted` にしない。**
   - `enforced` — 制約を実装した。`enforcedBy` に実装箇所を書く
   - `missing` — 実装漏れ。直すまでテストは失敗し続ける（それが目的）
   - `accepted` — 制約が無くてよいと判断した。`reason` は必須
   - `deferred` — 意図的に未実装。`reason` と、完全形を `plannedShape` に書く

### status: deferred を見つけたら

「実装漏れだ」と即断して直しにいかない。`reason` と `plannedShape` を読むこと。
段階的に縮小して実装した箇所であり、完全形は既に決まっている場合がある
（例: `settings:StartFolder` は「前回終了時のフォルダ」のみを実装した状態で、
完全形は固定パスとの選択制）。実装する場合は `plannedShape` のとおりに実装し、
異なる形にしたいときは先にユーザーへ確認する。

### マニフェストと実装が食い違っていたら

再生成して差分を見る。差分が出たということは実装が変わったということなので、
その変更が意図的かどうかをユーザーに確認する。
マニフェスト側を手で書き換えて辻褄を合わせてはならない。

## 破ってはならない方針

- **設定ファイルの移行コードを書かない。** 一般リリース前のため、旧設定との互換は考慮しない。
  `AppSettings` にバージョン欄を足さない。欠けたキーは既定値で埋まればよい。
- **既定値に好みを埋めない。** 運用想定外の選択肢は設定画面に置かず、コードに固定する。
  外部ツールのパスは空、クイックアクセスの初期登録は 0 件（B-05）。
- **設定 JSON に載る型に `required` を付けない**（S-14）。
- **卓駆のレジストリは読み書きしない**（R-20）。履歴も引き継がない。
- **設定ファイルは一時ファイル経由で置換する**（V-07）。直接上書きすると、書き込み中のクラッシュで
  設定が丸ごと消える。

## 書き方の約束

- 仕様 ID（`R-04` / `F-09` / `V-15` など）はコメントに残す。仕様書とコードを結ぶ索引になっている。
- ドメインの型は `sealed record` + `required init` が基本形。
- 検証はコンストラクタではなく static メソッドに置き、例外ではなく `null` / エラー文字列 /
  エラーの一覧 / `bool` を返す。
- コメントは「何をしているか」ではなく「なぜそうしたか」を書く。特に、素直に書くと壊れる理由を残す。

## 公開されるものの約束

このリポジトリはコミットがそのまま GitHub に公開される。一度 push した内容は履歴から消せない。

- コメント・コミットメッセージ・公開ファイルに、卓駆のヘルプ本文を引用しない。挙動は自分の言葉で書く。
- `docs/` の中のファイル名や章番号をコメントに書かない。仕様書への参照は仕様 ID だけで行う
  （`docs/` 内を ID で検索すれば該当箇所が引ける）。
- コミットメッセージに `Claude-Session:` 行を付けない。`Co-Authored-By:` 行は付けてよい。
- **`git push` と `gh release` は、利用者が明示的に指示したときだけ実行する。**
  push はリリースのときにだけ行う。作業途中のコミットは push しない。
