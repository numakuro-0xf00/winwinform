# wfth-correlate 分割設計（UNIX思想レビュー反映）

## 1. 背景

UNIX思想レビューにおいて、`wfth-correlate` が「一つのことをうまくやれ」原則の最大の違反として識別された。旧設計では以下の責務が1ツールに集約されていた:

1. 入力集約（MouseDown+Up → Click, キー列 → TextInput）
2. キーボードシーケンス集約
3. 時間窓イベント相関（UIA変化・スクリーンショット紐付け）
4. ノイズ分類（empty_click, window_move 等）
5. UIA フォールバック検出 + 参照画像抽出
6. テスト仕様ステップマッチング（`--spec`）

これは `cat` + `grep` + `sort` + `awk` + `wc` を1バイナリに詰め込んだようなもの。

## 2. 分割方針

旧 `wfth-correlate` を **3つの独立ツール** に分割し、パイプラインで合成する:

```
wfth-aggregate  — 生イベント → 集約アクション（Click, TextInput, DragAndDrop）
wfth-correlate  — 時間窓相関を中核とする後処理（UIA変化・スクリーンショット紐付け + ノイズ分類）
wfth-session    — NDJSON → モノリシック session.json 変換（将来）
```

### パイプライン合成例

```bash
# 基本パイプライン
wfth-aggregate < record.ndjson | wfth-correlate --uia uia.ndjson > session.ndjson

# モノリシック JSON が必要な場合
wfth-aggregate < record.ndjson | wfth-correlate --uia uia.ndjson | wfth-session > session.json

# jq で直接フィルタ
wfth-aggregate < record.ndjson | jq 'select(.type == "Click")'
```

## 3. wfth-aggregate（新設）

### 3.1 責務

生の入力イベント（mouse, key, window）を **操作アクション** に集約変換する。

- `LeftDown` + `LeftUp`（300ms以内）→ `Click`
- `LeftDown` + `Move(drag)` + `LeftUp` → `DragAndDrop`
- 2つの `Click`（500ms以内）→ `DoubleClick`
- `RightDown` + `RightUp` → `RightClick`
- 連続キー入力（`--text-timeout` で区切り）→ `TextInput`
- 特殊キー（Enter, Tab, Escape等）→ `SpecialKey`（集約中断）
- ノイズ分類は行わない（`wfth-correlate` が担当）

### 3.2 CLIインターフェース

```
wfth-aggregate [options]

Input:
  stdin                     wfth-record 出力 NDJSON

Options:
  --text-timeout <ms>       キー入力集約タイムアウト（デフォルト: 500）
  --click-timeout <ms>      Click判定タイムアウト（デフォルト: 300）
  --dblclick-timeout <ms>   DoubleClick判定タイムアウト（デフォルト: 500）
  --debug                   診断情報を stderr に出力
  --quiet                   stderr 出力を抑制
```

### 3.3 入出力

**入力**: wfth-record の生 NDJSON（stdin）
```json
{"ts":"...","type":"mouse","action":"LeftDown","sx":450,"sy":320,"rx":230,"ry":180}
{"ts":"...","type":"mouse","action":"LeftUp","sx":450,"sy":320,"rx":230,"ry":180}
{"ts":"...","type":"key","action":"down","vk":84,"key":"T","char":"T"}
```

**出力**: 集約済みアクション NDJSON（stdout）
```json
{"ts":"...","type":"Click","button":"Left","sx":450,"sy":320,"rx":230,"ry":180}
{"ts":"...","type":"TextInput","text":"T","startTs":"...","endTs":"..."}
```

### 3.4 アーキテクチャ

```
Program.cs (System.CommandLine)
  ├── Aggregation/
  │   ├── MouseClickAggregator.cs    — Down+Up → Click/DoubleClick/Drag
  │   ├── KeySequenceAggregator.cs   — 連続キー → TextInput
  │   └── ActionBuilder.cs           — 集約済みアクション生成
  └── Models/
      └── AggregatedAction.cs        — 集約済みアクションDTO
```

## 4. wfth-correlate（責務縮小）

### 4.1 新しい責務

時間窓ベースの **イベント相関を中核** に行う:
- 各アクションに対して時間窓（`--window`）内の UIA 変化を紐付け
- スクリーンショット（before/after）を紐付け
- アプリ内ログを紐付け
- ノイズ分類（`empty_click`, `window_move`, `duplicate_click` など）を実施
- （将来）`--spec` でテスト仕様ステップとの突合を実施

### 4.2 CLIインターフェース

```
wfth-correlate [options]

Input:
  stdin                     wfth-aggregate 出力 NDJSON（集約済みアクション）

Options:
  --uia <path>              UIA変化 NDJSON（wfth-inspect watch 出力）
  --app-log <path>          アプリ内ロガー NDJSON
  --screenshots <dir>       スクリーンショットディレクトリ
  --window <ms>             相関時間窓（デフォルト: 2000）
  --include-noise           ノイズ判定された操作も出力
  --noise-threshold <n>     confidence >= n をノイズと判定（デフォルト: 0.7）
  --explain                 各相関の判定根拠を注釈
  --debug                   診断情報を stderr に出力
  --quiet                   stderr 出力を抑制
```

注: `--spec` は将来機能として `wfth-correlate` 側に追加する（現時点では未実装）。

### 4.3 入出力

**入力**: wfth-aggregate の集約済み NDJSON（stdin）+ UIA/スクリーンショット（ファイル引数）

**出力**: 相関済みアクション NDJSON（stdout）— **デフォルト出力が NDJSON に変更**
```json
{"seq":1,"ts":"...","type":"Click","input":{...},"target":{...},"screenshots":{...},"uiaDiff":{...}}
```

### 4.4 `--explain` モード

各相関の判定根拠を `_explain` フィールドで注釈する:
```json
{
  "seq": 1,
  "type": "Click",
  "_explain": {
    "uiaMatch": "UIA change at +150ms within 2000ms window",
    "screenshotMatch": "before: 0001_before.png (-50ms), after: 0001_after.png (+200ms)",
    "targetSource": "UIA AutomationId=btnSearch matched by coordinate intersection"
  }
}
```

### 4.5 セッションディレクトリ規約の廃止

旧設計の暗黙ディレクトリ規約（`wfth-correlate session-dir/`）は廃止する。
UNIX原則に従い、全入力を明示的な引数で受け取る:

```bash
# 旧（暗黙規約）
wfth-correlate sessions/rec-20260222/

# 新（明示引数）
wfth-aggregate < sessions/rec-20260222/record.ndjson \
  | wfth-correlate --uia sessions/rec-20260222/uia.ndjson \
                   --screenshots sessions/rec-20260222/screenshots/ \
  > sessions/rec-20260222/session.ndjson
```

ラッパースクリプト `wfth-pipeline` でショートカットを提供することは可能だが、コアツールは明示的引数を基本とする。

## 5. wfth-session（将来検討）

NDJSON → モノリシック JSON 変換ツール。`jq -s` で代替可能だが、セッションメタデータ（duration計算等）の付加が必要な場合に独立ツールとして検討する。

```bash
# jq で代替
wfth-correlate ... | jq -s '{session: {...}, actions: .}' > session.json

# 専用ツール
wfth-correlate ... | wfth-session --process SampleApp > session.json
```

## 6. 移行計画

### Phase 1（設計変更 — 実施済み）
- `wfth-aggregate` スタブ作成
- `wfth-correlate` の設計ドキュメント更新
- 分割後のパイプライン設計を確定

### Phase 2（wfth-aggregate 実装）
- 旧 `wfth-correlate` の集約ロジック（`Aggregation/` ディレクトリ）を移植
- 単体テスト: 手作り NDJSON → 集約結果の検証
- `demo.sh` でのパイプライン実証

### Phase 3（wfth-correlate 実装）
- 時間窓相関ロジックのみを実装
- `--explain` モード実装
- 単体テスト: 手作り集約済み NDJSON + UIA NDJSON → 相関結果の検証

## 7. recording-cli-design.md からの変更点

| 項目 | 旧設計 | 新設計 |
|------|--------|--------|
| ツール数 | `wfth-correlate` 1本 | `wfth-aggregate` + `wfth-correlate` の2本 |
| 入力集約 | correlate 内部 | aggregate が担当 |
| デフォルト出力 | モノリシック JSON | NDJSON |
| ディレクトリ規約 | 暗黙検出 | 明示引数 |
| ノイズ分類 | correlate 内部 | correlate が担当（`--include-noise` で制御） |
| ノイズ閾値 | なし | `--noise-threshold` で調整可能 |
| 判定根拠 | 不可視 | `--explain` モード |
