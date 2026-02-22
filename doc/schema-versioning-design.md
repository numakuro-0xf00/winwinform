# 統合ログスキーマ バージョニング設計

## 1. 背景

Recording パイプラインは複数の NDJSON フォーマットを介してデータを受け渡す。これらのスキーマは AIエージェント、後段ツール、CI パイプラインなど複数のコンシューマーが依存する公開契約である。

スキーマを変更する際のルールがないと:
- 既存の AIエージェントプロンプトが壊れる
- 古い session.ndjson が新しいツールで読めない（逆も然り）
- どのバージョンのツールで生成されたデータか判別できない

---

## 2. バージョニング対象

### 2.1 パイプライン上のスキーマ一覧

```
wfth-record  ──→  record.ndjson  ──→  wfth-aggregate  ──→  aggregated NDJSON
                                                                    │
                                                                    ▼
wfth-inspect ──→  uia.ndjson     ──→  wfth-correlate   ──→  session.ndjson
                                            ▲
app-logger   ──→  app-log.ndjson ──────────┘
```

| スキーマ名 | 出力元 | 主なコンシューマー | バージョニング対象 |
|-----------|--------|-------------------|------------------|
| record | wfth-record | wfth-aggregate | 内部契約 |
| uia | wfth-inspect | wfth-correlate | 内部契約 |
| app-log | アプリ内ロガー | wfth-correlate | 内部契約 |
| aggregated | wfth-aggregate | wfth-correlate, jq | 中間契約 |
| **session** | **wfth-correlate** | **AIエージェント, テスト生成** | **公開契約** |

### 2.2 バージョニング戦略

```
公開契約（session スキーマ）:
  - 厳密にバージョニングする
  - 後方互換ルールを厳守
  - バージョン番号を必ず埋め込む

内部契約（record, uia, app-log, aggregated）:
  - 同一リリース内で整合性を保証
  - ツールバージョン（ビルド番号）で追跡
  - 異なるバージョン間の互換性は保証しない
```

**理由**: 内部契約はパイプライン内で同一バージョンのツールが生成・消費する前提。異なるバージョンの wfth-record 出力を新しい wfth-aggregate に流す運用は想定しない。一方、session.ndjson は永続保存されて将来の AI エージェントや解析ツールが参照するため、厳密な互換性管理が必要。

---

## 3. バージョン表記

### 3.1 形式

公開契約には **整数バージョン** を使用する。

```
schemaVersion: 1
```

- 単一整数（1, 2, 3, ...）
- 後方互換でない変更（breaking change）が入るたびにインクリメント
- 後方互換な変更（additive change）ではインクリメントしない

### 3.2 SemVer を採用しない理由

```
SemVer (major.minor.patch) の場合:
  - minor / patch の区別が NDJSON スキーマでは不明確
  - コンシューマーは「読めるか読めないか」の2値判定で十分
  - 整数のほうが比較ロジックが単純（schemaVersion >= 2）
```

### 3.3 バージョンの埋め込み

#### session.ndjson（公開契約）

`session/start` マーカーの最初の行に `schemaVersion` を含める。

```json
{"ts":"...","type":"session","action":"start","schemaVersion":1,"process":"SampleApp","pid":12345,"toolVersion":"1.2.0","pipeline":{"aggregate":"1.2.0","correlate":"1.2.0"}}
```

フィールド:
- `schemaVersion`: スキーマバージョン（必須、整数）
- `toolVersion`: wfth-correlate のバージョン（参考情報）
- `pipeline`: パイプライン各ツールのバージョン（参考情報）

#### 内部契約（record.ndjson 等）

`session/start` マーカーに `toolVersion` を含める（スキーマバージョンは不要）。

```json
{"ts":"...","type":"session","action":"start","toolVersion":"1.2.0","process":"SampleApp","pid":12345}
```

---

## 4. 互換性ルール

### 4.1 後方互換な変更（schemaVersion 不変）

以下の変更は既存コンシューマーを壊さないため、バージョンをインクリメントしない:

| 変更種別 | 例 | 理由 |
|---------|-----|------|
| **新規オプショナルフィールドの追加** | `noise` フィールドの追加 | 存在しないフィールドは無視すればよい |
| **新規イベントタイプの追加** | `type:"DragAndDrop"` の追加 | 未知の type は無視すればよい |
| **既存 enum への値追加** | `noise` に `"scroll_noise"` 追加 | 未知の値は無視すればよい |
| **オプショナルフィールドのネスト拡張** | `target.rect` にサブフィールド追加 | 既存フィールドは変わらない |
| **`_explain` 等の診断フィールド追加** | `_` プレフィックス付きフィールド | コンシューマーは `_` 始まりを無視する規約 |

### 4.2 後方互換でない変更（schemaVersion インクリメント）

以下の変更は既存コンシューマーを壊すため、バージョンをインクリメントする:

| 変更種別 | 例 | 影響 |
|---------|-----|------|
| **必須フィールドの削除** | `seq` フィールドの削除 | コンシューマーがフィールド参照で失敗 |
| **フィールド名の変更** | `ts` → `timestamp` | 既存のパーサーが値を取得できない |
| **フィールドの型変更** | `seq` を string に変更 | 型チェックや比較ロジックが壊れる |
| **フィールドのセマンティクス変更** | `rx/ry` を物理→論理座標に変更 | 値の解釈が変わる |
| **既存イベントタイプの名称変更** | `Click` → `SingleClick` | type ベースのフィルタが壊れる |
| **必須フィールドの新規追加** | 新しい必須フィールドの追加 | 古いツールの出力にフィールドがない |

### 4.3 判断基準のフローチャート

```
変更を加えたい
  │
  ├─ 新しいフィールド/タイプを追加するだけ？
  │    YES → 後方互換（バージョン不変）
  │    NO ↓
  │
  ├─ 既存フィールドの削除・改名・型変更？
  │    YES → Breaking（バージョンインクリメント）
  │    NO ↓
  │
  ├─ 既存フィールドの意味（セマンティクス）が変わる？
  │    YES → Breaking（バージョンインクリメント）
  │    NO ↓
  │
  └─ 後方互換（バージョン不変）
```

---

## 5. コンシューマー側の対応ガイドライン

### 5.1 AIエージェント向け

AIエージェント（プロンプト・コード生成パイプライン）が session.ndjson を消費する際のルール:

```
1. schemaVersion を確認する
   - サポート範囲外の場合はエラーを返す
   - 「schemaVersion >= 1 かつ <= 3 をサポート」のように範囲指定

2. 未知のフィールドは無視する
   - JSON パーサーで unknown properties を許容する設定
   - 新しいフィールドが追加されても壊れない

3. 未知のイベントタイプは無視する
   - type が既知でない行はスキップする
   - ログに警告を出す（デバッグ用）

4. _ プレフィックスのフィールドは無視する
   - _explain, _debug 等は診断情報であり、処理に使わない

5. null / 欠損フィールドを想定する
   - オプショナルフィールドは存在しない場合がある
   - appLog: [] （空配列）はアプリ内ロガー未接続を意味する
```

### 5.2 コード例（C# コンシューマー）

```csharp
// session.ndjson のパース
class SessionReader
{
    private const int MinSchemaVersion = 1;
    private const int MaxSchemaVersion = 1; // 現在サポートする最大バージョン

    public async IAsyncEnumerable<CorrelatedAction> ReadAsync(
        Stream input, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(input);
        string? line;
        bool versionChecked = false;

        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            var json = JsonDocument.Parse(line);
            var root = json.RootElement;

            // 最初の session/start 行でバージョン確認
            if (!versionChecked &&
                root.TryGetProperty("type", out var type) &&
                type.GetString() == "session")
            {
                var version = root.TryGetProperty("schemaVersion", out var sv)
                    ? sv.GetInt32()
                    : 1; // schemaVersion 未記載は v1 として扱う

                if (version < MinSchemaVersion || version > MaxSchemaVersion)
                    throw new UnsupportedSchemaException(version,
                        MinSchemaVersion, MaxSchemaVersion);

                versionChecked = true;
                continue;
            }

            // 未知の type はスキップ
            if (!IsKnownType(root))
            {
                Log.Debug($"Skipping unknown type: {root.GetProperty("type")}");
                continue;
            }

            // デシリアライズ（未知フィールドは無視される）
            var action = JsonSerializer.Deserialize<CorrelatedAction>(line,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    // 未知プロパティを無視（デフォルト動作）
                });

            if (action != null)
                yield return action;
        }
    }
}
```

### 5.3 jq ユーザー向け

```bash
# schemaVersion の確認
head -1 session.ndjson | jq '.schemaVersion'

# バージョン互換性チェック
SCHEMA_VERSION=$(head -1 session.ndjson | jq -r '.schemaVersion // 1')
if [ "$SCHEMA_VERSION" -gt 1 ]; then
  echo "Warning: schema version $SCHEMA_VERSION may not be fully supported"
fi

# 未知の type を除外してフィルタ
jq 'select(.type | IN("Click","TextInput","DoubleClick","SpecialKey","RightClick","DragAndDrop"))' \
  < session.ndjson
```

---

## 6. スキーマ定義（session.ndjson v1）

### 6.1 共通フィールド

全イベント行が持つフィールド:

| フィールド | 型 | 必須 | 説明 |
|-----------|-----|------|------|
| `seq` | integer | Yes | 連番（1始まり、session 行は除く） |
| `ts` | string (ISO 8601) | Yes | タイムスタンプ（UTC） |
| `type` | string | Yes | イベントタイプ |

### 6.2 session 行（メタデータ）

| フィールド | 型 | 必須 | 説明 |
|-----------|-----|------|------|
| `type` | `"session"` | Yes | 固定 |
| `action` | string | Yes | `start`, `stop`, `pause`, `resume` |
| `schemaVersion` | integer | Yes (start) | スキーマバージョン（start 行のみ） |
| `process` | string | Yes (start) | 対象プロセス名 |
| `pid` | integer | Yes (start) | 対象プロセスID |
| `hwnd` | string | Yes (start) | ウィンドウハンドル (0xHHHH) |
| `toolVersion` | string | No | wfth-correlate バージョン |
| `pipeline` | object | No | パイプライン各ツールのバージョン |
| `reason` | string | No (stop) | 停止理由 |
| `duration` | number | No (stop) | セッション時間（秒） |
| `pauseDuration` | number | No (resume) | 一時停止していた時間（秒） |

### 6.3 アクション行

| フィールド | 型 | 必須 | 説明 |
|-----------|-----|------|------|
| `seq` | integer | Yes | 連番 |
| `ts` | string | Yes | タイムスタンプ |
| `type` | string | Yes | `Click`, `TextInput`, `DoubleClick`, `RightClick`, `DragAndDrop`, `SpecialKey` |
| `input` | object | Yes | 入力データ（type により構造が異なる） |
| `target` | object | No | 操作対象（UIA情報、座標等） |
| `screenshots` | object | No | 関連スクリーンショット |
| `uiaDiff` | object | No | UIAツリーの変化 |
| `appLog` | array | No | 対応するアプリ内ロガーイベント |
| `noise` | string | No | ノイズ分類（null = 有効な操作） |
| `confidence` | number | No | ノイズ分類の確信度 (0.0〜1.0) |

### 6.4 input オブジェクト（type 別）

**Click / DoubleClick / RightClick:**
```json
{"button":"Left","sx":450,"sy":320,"rx":230,"ry":180}
```

**TextInput:**
```json
{"text":"田中","duration":0.3,"method":"direct"}
```

**SpecialKey:**
```json
{"key":"Enter"}
```

**DragAndDrop:**
```json
{"button":"Left","startSx":100,"startSy":200,"endSx":300,"endSy":400,"startRx":80,"startRy":160,"endRx":280,"endRy":360}
```

### 6.5 target オブジェクト

```json
{
  "source": "UIA",
  "automationId": "btnSearch",
  "name": "検索",
  "controlType": "Button",
  "className": "System.Windows.Forms.Button",
  "rect": {"x":420,"y":310,"w":80,"h":30},
  "fallbackReason": null,
  "referenceImage": null
}
```

| フィールド | 型 | 必須 | 説明 |
|-----------|-----|------|------|
| `source` | string | Yes | `UIA`, `coordinate_only` |
| `automationId` | string | No | UIA AutomationId |
| `name` | string | No | UIA Name |
| `controlType` | string | No | UIA ControlType |
| `className` | string | No | UIA ClassName |
| `rect` | object | No | 要素の BoundingRectangle |
| `fallbackReason` | string | No | UIA 取得失敗の理由 |
| `referenceImage` | string | No | リファレンス画像パス |

### 6.6 screenshots オブジェクト

```json
{
  "before": "screenshots/0001_before.png",
  "after": "screenshots/0001_after.png"
}
```

### 6.7 uiaDiff オブジェクト

```json
{
  "added": [{"automationId":"","name":"検索","controlType":"Window"}],
  "removed": [],
  "changed": [{"automationId":"dgvResults","property":"RowCount","from":0,"to":1}]
}
```

### 6.8 アンダースコアプレフィックス規約

`_` で始まるフィールドは診断・デバッグ用であり、コンシューマーは無視すべき:

```json
{
  "seq": 1,
  "type": "Click",
  "_explain": {
    "uiaMatch": "UIA change at +150ms within 2000ms window",
    "targetSource": "UIA AutomationId=btnSearch matched by coordinate intersection"
  },
  "_debug": {
    "aggregateInputCount": 2,
    "correlationCandidates": 3
  }
}
```

---

## 7. バージョン移行手順

### 7.1 新バージョンのリリースプロセス

```
1. スキーマ変更の PR を作成
   - この文書のセクション6を更新
   - 変更がbreakingかadditiveかを明記

2. Breaking の場合:
   a. schemaVersion をインクリメント（例: 1 → 2）
   b. 移行ガイドを作成（旧 → 新のフィールドマッピング）
   c. wfth-correlate に旧バージョン出力モード（--schema-version 1）を追加
      → 最低1リリース期間は旧バージョン出力をサポート
   d. コンシューマー側の MaxSchemaVersion を更新

3. Additive の場合:
   a. schemaVersion は不変
   b. 新フィールドの説明をセクション6に追加
   c. コンシューマーは変更不要（未知フィールドは無視される）
```

### 7.2 旧バージョン出力サポート

Breaking change 時、wfth-correlate は `--schema-version` オプションで旧バージョンのスキーマで出力できる:

```bash
# 新バージョン（デフォルト）
wfth-aggregate < record.ndjson | wfth-correlate --uia uia.ndjson > session_v2.ndjson

# 旧バージョン互換
wfth-aggregate < record.ndjson | wfth-correlate --uia uia.ndjson --schema-version 1 > session_v1.ndjson
```

旧バージョンサポートは **直前の1バージョンのみ**。例: v3 リリース時に v2 出力をサポート、v1 サポートは打ち切り。

---

## 8. スキーマ検証

### 8.1 検証ツール（将来検討）

```bash
# session.ndjson のスキーマ検証（将来の wfth-validate）
wfth-validate session.ndjson
# → OK: 25 actions, schema v1, no errors
# → WARN: 2 actions have unknown fields (compatible)
# → ERROR: schemaVersion 3 is not supported (max: 2)
```

### 8.2 検証ルール

```
Level 1 — 構造検証:
  - 各行が有効な JSON であること
  - 必須フィールド（seq, ts, type）が存在すること
  - 最初の行が session/start で schemaVersion を含むこと

Level 2 — 型検証:
  - 各フィールドが期待される型であること
  - ts が ISO 8601 形式であること
  - seq が正の整数で単調増加であること

Level 3 — 意味検証:
  - screenshots のファイルパスが実在すること
  - seq にギャップがないこと（SystemGap を除く）
  - session/start と session/stop が対になっていること
```

---

## 9. 内部契約のバージョン追跡

### 9.1 ツールバージョンの埋め込み

内部契約（record.ndjson, uia.ndjson 等）には `toolVersion` を埋め込む:

```json
{"ts":"...","type":"session","action":"start","toolVersion":"1.2.0","tool":"wfth-record","process":"SampleApp"}
```

### 9.2 パイプラインの整合性チェック

wfth-correlate は入力の `toolVersion` を確認し、大きなバージョン差がある場合に警告を出す:

```
stderr: Warning: record.ndjson was generated by wfth-record 1.0.0,
        but wfth-correlate is 2.3.0. Output may be unreliable.
```

この警告はエラーではなく、処理は続行する。`--quiet` で抑制可能。

---

## 10. 実装優先度

| 機能 | 優先度 | 理由 |
|------|--------|------|
| session/start に schemaVersion 埋め込み | **MVP** | コンシューマーがバージョン判定できる最低限 |
| session/start に toolVersion 埋め込み | **MVP** | デバッグ・互換性調査の基本 |
| セクション6のスキーマ定義（この文書自体） | **MVP** | 契約の明文化 |
| コンシューマー向けガイドライン（セクション5） | **MVP** | 正しい実装を誘導 |
| _ プレフィックス規約 | **MVP** | wfth-correlate --explain と同時 |
| --schema-version 出力オプション | 将来 | v2 リリース時に初めて必要 |
| wfth-validate 検証ツール | 将来 | スキーマ安定後 |
| 内部契約の整合性チェック | 低 | 同一リリースで使う前提 |
