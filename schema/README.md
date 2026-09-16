# スキーマ・マニフェスト

ReTAC の構造上の契約を、機械が読める形で置く場所。読み手は主に AI エージェントで、人間の可読性は目的にしていない。

## ファイル

| ファイル | 誰が書くか | 中身 |
|---|---|---|
| `manifest.json` | **自動生成** | 実装を写したもの。今どうなっているか |
| `invariants.json` | **手書き** | あるべき制約と設計方針。どうあるべきか |

**この 2 つを混ぜてはならない。** `manifest.json` を手で直しても次の再生成で消える。逆に、実装から導けないもの（「移行コードを書かない」のような方針）は `manifest.json` には絶対に現れない。

## コマンド

```
dotnet test                          # マニフェストが実装と一致するか検証する
RETAC_SCHEMA_UPDATE=1 dotnet test    # マニフェストを実装に合わせて書き直す
```

生成器と検証器は同じコード（`tests/ReTAC.Domain.Tests/SchemaManifest.cs`）である。別々に持つと、生成器と検証器がズレるという昔ながらの失敗をする。

## manifest.json は全文を読まない

148 KB ある。全文を読むとコンテキストを数万トークン食うので、必要な箇所だけ `grep -A` で引く。
整形済み JSON なのでそれで足りる。`invariants.json`（8 KB）は全文を読んでよい。

**`jq` を使う**（`winget install jqlang.jq`）。欲しい項目だけに絞れるので、
型 1 件を丸ごと読む 1,820 bytes が 195 bytes で済む。

```sh
M=schema/manifest.json

# 型の中身（実装中に要るのはたいていこれだけ）
jq -r '.types["ReTAC.Domain.Entries.Entry"] | "\(.kind) \(.file)",
       (.fields[] | "  \(.name): \(.type)\(if .nullable then "?" else "" end)\(if .required then " required" else "" end)")' $M

jq '.types["ReTAC.Domain.Entries.Entry"]' $M                       # 型 1 件の全部
jq -c '.settings.keys[] | select(.key=="KeyBindings")' $M          # 設定項目 1 件
jq -r '.specIndex["F-09"][] | "\(.file):\(.line)"' $M              # 仕様 ID の出現箇所
jq -r '.wiring.commands[] | select(.id=="OpenFile")' $M            # コマンド 1 件
jq -r '.types | keys[]' $M                                         # 型の名前だけ一覧
```

横断して調べるものは `jq` でないと、結局セクションを丸ごと読むことになる。

```sh
jq -r '.edges[] | select(.onDelete=="UNENFORCED")
       | "\(.from)#\(.fk.field) -> \(.to)"' $M                     # 型で守られない参照
jq -r '.edges[] | select(.to=="type:ReTAC.Domain.Tools.ExternalTool")
       | "\(.from)#\(.fk.field) [\(.type)]"' $M                    # ある型を指すエッジ
jq -r '.edges[] | select(.fk.unique==false) | "\(.from)#\(.fk.field)"' $M   # 重複を許すコレクション
jq -r '[.wiring.commands[] | select(.defaultKeys==[]) | .id] | join(", ")' $M  # 既定キーが無いコマンド
jq -r '.specIndex | to_entries | map("\(.key) (\(.value|length))") | join(" ")' $M  # 仕様 ID の一覧と件数
```

`invariants.json` も同じように引ける。

```sh
jq -r '.invariants[] | select(.status!="enforced") | "\(.status)\t\(.id)"' schema/invariants.json
jq -r '.settingsOwners["StartFolder"]' schema/invariants.json
```

### jq が無い環境では

`sed` の範囲指定で切り出す。型 1 件は 5〜75 行と幅があるので、`grep -A <n>` は窓を外す
（`-A 20` だと `Entry` が途中で切れる）。

```sh
sed -n '/^    "ReTAC.Domain.Entries.Entry": {/,/^    },\?$/p' schema/manifest.json  # 型 1 件
grep -n -A 8  '"key": "KeyBindings"' schema/manifest.json   # 設定項目 1 件
grep -n -A 10 '"F-09": \['           schema/manifest.json   # 仕様 ID の出現箇所
grep -n -B 2 -A 8 'UNENFORCED'       schema/manifest.json   # 型で守られない参照
```

横断して調べるものは、該当セクションを読んで自分で畳み込むしかない。`edges` と `settings` は小さいのでそれでよいが、`wiring`（25 KB）と `types`（48 KB）でそれをやるとコンテキストを大きく食う。

### セクションの大きさ

| セクション | 大きさ | 全部読んでよいか |
|---|---|---|
| `types` | 48 KB | ✕ 型ごとに引く |
| `specIndex` | 50 KB | ✕ ID ごとに引く |
| `wiring` | 25 KB | △ コマンド一覧が要るときだけ |
| `edges` | 8 KB | ○ |
| `settings` | 4.5 KB | ○ |

## manifest.json の読み方

| セクション | 中身 |
|---|---|
| `settings` | `retac.settings.json` の契約。キー・型・既定値・null 許容 |
| `types` | `ReTAC.Domain` の公開型。フィールド・型・null 許容・required・init/set |
| `edges` | 型どうしの参照。`1:1` / `0..1` / `1:N` / `N:1` |
| `wiring` | コマンド・キー枠・配色枠・既定ツールの横断表 |
| `specIndex` | 仕様 ID（`R-04` 形式）→ ソース上の全出現箇所 |

`edges` で注意して読むもの:

- `"onDelete": "UNENFORCED"` — 型システムが守らない参照。壊れても誰も教えてくれない
- `"fk": { "byValue": true }` — オブジェクト参照ではなく数値で指している。外部キー相当
- `"fk": { "unique": false }` — 同じものを複数入れられるコレクション

`specIndex` は影響範囲を追うためにある。`R-55` に関わるコードを変えるなら、まずここを引いて全出現箇所を確認する。1 箇所だけ直して他を放置しない。

## invariants.json の読み方

各項目は `status` を必ず持つ。

| `status` | 意味 | どうする |
|---|---|---|
| `enforced` | 実装済み。`enforcedBy` が実装箇所を指す | 壊さない |
| `accepted` | 制約が無くてよいと判断した。`reason` 必須 | 変えたいなら理由ごと更新する |
| `deferred` | 意図的に未実装。`reason` と `plannedShape` 必須 | **不具合と即断しない。** 実装するなら `plannedShape` のとおりに |
| `missing` | 実装漏れ | 直す。直すまでテストは失敗し続ける |

`settingsOwners` は設定項目ごとの編集手段。`dialog:` は設定画面、`runtime:` はアプリが自動で更新する値、`manual:` は設定ファイルの手編集。

## 実装漏れを見つける仕組み

lint L11 / L12 が、実装を走査して「制約が無い箇所」を自動で列挙する。

- **L11** — 一意キーを持ちうる record を `List` や配列で保持していて、重複禁止が無い箇所
- **L12** — `XxxId` という名前で他の型を値参照していて、参照整合性の強制が無い箇所

列挙された候補は `invariants.json` の `applies` に書いて分類しなければテストが通らない。**黙って通る経路は無い。** 新しい型や参照を足した瞬間に候補が増えるので、分類を保留したまま先に進むことはできない。

候補は機械的な推測なので、実際には参照でないものも挙がる（`NextExternalToolId` は次に振る番号のカウンタであって参照ではない）。そういうものは `accepted` にして `reason` にそう書く。理由を書かせること自体が目的で、面倒だからという理由で `accepted` を並べない。

## lint 一覧

| ID | 何を見るか |
|---|---|
| L1 | 全コマンドが表示名を持つか、除外理由が書かれているか |
| L2 | 既定のキーが、設定画面に出る枠の中に収まっているか |
| L3 | 既定のツールキーが、実在するツールを指しているか |
| L4 | 配色のキー空間が、設定ファイルと設定画面で一致しているか |
| L5 | 全ての設定項目に編集手段が記録されているか |
| L6 | 不変条件の参照先がマニフェスト上で解決するか |
| L7 | 不変条件の仕様 ID がコードに残っているか |
| L8 | エッジの参照先が解決し、重複や一意性の食い違いが無いか |
| L9 | null 許容でない設定項目に既定値があるか |
| L10 | 不変条件の分類が埋まっているか／実装漏れが残っていないか |
| L11 | 一意性の強制が無いコレクションが分類済みか |
| L12 | 型で守られない値参照が分類済みか |

## 対象範囲

`types` と `edges` が写すのは `ReTAC.Domain` の公開型と、外部契約である `ReTAC.App.AppSettings`。`ReTAC.Shell` は Win32 API のラッパーでデータ構造を持たないため含めない。WinForms の画面クラスも、状態が画面の都合に閉じているため含めない。
