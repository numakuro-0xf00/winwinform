# Recording Engine データの質と量 設計

## 1. ノイズの除去

### 1.1 問題

記録される入力イベントには、テスト操作として意味のないノイズが混在する:

- **空クリック**: 何もないエリアへのクリック（ウィンドウ移動やフォーカス取得目的）
- **操作ミス**: 間違ったボタンのクリック → 即座にキャンセルや閉じる操作
- **迷いマウス**: クリック先を探してウィンドウ上をホバー
- **ウィンドウ操作**: タイトルバードラッグ（移動）、リサイズ、最小化/最大化
- **連打**: 反応が遅くて同じボタンを複数回クリック
- **IME操作**: 日本語入力の変換・確定に伴うキーイベント群

### 1.2 設計方針: 記録時は全保存、後処理でフィルタ

```
原則: wfth-record は全イベントを忠実に記録する（ノイズ除去しない）
      wfth-correlate がヒューリスティクスでノイズを分類・マーキングする
```

理由:
- 何がノイズかは文脈依存（ウィンドウ移動がテスト操作の場合もある）
- 生データを残しておけば、ヒューリスティクスの改善時に再処理できる
- AIエージェントにも生データを渡す選択肢を残す

### 1.3 wfth-correlate のノイズ分類ルール

#### A. ウィンドウ操作の検出

```
パターン:
  タイトルバー領域でのドラッグ → "window_move"
  ウィンドウ端でのドラッグ     → "window_resize"
  最小化/最大化/閉じるボタン   → "window_control"

判定方法:
  1. クリック座標が Non-Client Area（タイトルバー、枠）かどうか
     → UIA要素の ControlType が "TitleBar" or ClassName が特定値
  2. ドラッグ開始点がタイトルバー内 → window_move
  3. ドラッグ開始点がウィンドウ端（±8px） → window_resize
```

出力: アクションに `noise` フィールドを付与

```json
{
  "seq": 3,
  "type": "Drag",
  "noise": "window_move",
  "confidence": 0.95,
  "input": { ... }
}
```

#### B. 空クリックの検出

```
パターン:
  クリック後にUIの変化がない
  AND クリック先のUIA要素が操作可能コントロールでない

判定方法:
  1. uiaDiff が null または空
  2. appLog が空
  3. target の controlType が "Window", "Pane", "Group" 等の非操作系
  4. screenshots の before/after に有意な差分なし

  全て満たす → noise: "empty_click"
```

#### C. 操作ミスの検出

```
パターン A — 即キャンセル:
  Click(btnX) → [2秒以内] → Click(btnCancel) or KeyPress(Escape)
  → 先行する Click(btnX) に noise: "likely_mistake" を付与

パターン B — 連打:
  Click(btnX, t=T) → Click(btnX, t=T+200ms)
  同一要素への 500ms 以内の再クリック → 2回目以降に noise: "duplicate_click"

パターン C — Undo操作:
  TextInput("abc") → [直後] → KeyPress(Ctrl+Z)
  → TextInput に noise: "undone_input"
```

#### D. IME操作のグルーピング

```
日本語入力の典型シーケンス:
  key(down, vk=229)   ← IME処理中マーカー
  key(down, vk=84)    ← 't' キー（未確定）
  key(down, vk=65)    ← 'a' キー（未確定）
  key(down, vk=13)    ← Enter（確定）

判定方法:
  vk=229 (VK_PROCESSKEY) が出現したら IME モード開始
  確定操作（Enter or 直接入力への復帰）まで IME グループとしてまとめる

出力:
  {
    "type": "TextInput",
    "input": {
      "text": "田中",        ← 確定後の結果テキスト
      "method": "ime",       ← 入力方式
      "rawKeyCount": 12      ← 生キーイベント数（参考値）
    }
  }
```

注: IME確定後テキストは wfth-record 単体では取得困難。wfth-correlate が以下から推定:
1. UIA の Value パターン（対象コントロールの Text プロパティ変化）
2. アプリ内ロガーの TextChanged イベント
3. 上記がない場合は raw key から推定不可として `"text": null` とする

### 1.4 ノイズ分類の出力形式

```json
{
  "seq": 5,
  "type": "Click",
  "noise": "empty_click",
  "confidence": 0.8,
  "input": { ... },
  "target": { "source": "UIA", "controlType": "Pane", ... }
}
```

- `noise`: ノイズ分類名（null の場合は有効な操作）
- `confidence`: 分類の確信度（0.0〜1.0）

### 1.5 ノイズ分類一覧

| noise 値 | 説明 | 判定根拠 |
|-----------|------|----------|
| `null` | 有効な操作 | デフォルト |
| `window_move` | ウィンドウ移動 | タイトルバーでのドラッグ |
| `window_resize` | ウィンドウリサイズ | ウィンドウ端でのドラッグ |
| `window_control` | 最小化/最大化/閉じる | システムボタンクリック |
| `empty_click` | 空クリック | UI変化なし + 非操作コントロール |
| `duplicate_click` | 連打（重複） | 同一要素への500ms以内の再クリック |
| `likely_mistake` | 操作ミスの可能性 | 直後にキャンセル/Escapeが続く |
| `undone_input` | 取り消された入力 | 直後にCtrl+Zが続く |
| `hover_noise` | 意図しないホバー | 短時間のMouseMove群（クリックなし） |
| `scroll_noise` | 探索的スクロール | 短時間に上下に反復するホイール操作 |

### 1.6 AIエージェントへの提供方針

```
wfth-correlate のオプション:
  --include-noise     ノイズも含めて出力（デフォルト: 除外）
  --noise-threshold <n>  confidence がこの値以上のノイズを除外（デフォルト: 0.7）
```

デフォルトでは confidence ≧ 0.7 のノイズを除外した「クリーン」なアクション列を出力。
AIエージェントが判断に迷う場合は `--include-noise` で全データを参照可能。

---

## 2. スクリーンショットの保存戦略

### 2.1 問題

無制限にスクリーンショットを撮ると:
- 1操作あたり before/after で2枚 × 解像度1920x1080 × PNG ≒ 2〜5MB/枚
- 100操作の記録 → 200枚 → 400MB〜1GB
- テスト仕様書10ケース分 → 数GB

### 2.2 保存戦略の全体方針

```
階層的保存:
  Level 0: 撮影しない（--no-screenshot）
  Level 1: 操作後のみ、変化があった場合のみ（デフォルト）
  Level 2: 操作前後（before/after）、変化があった場合のみ
  Level 3: 全操作の前後を無条件に撮影（デバッグ用）
```

### 2.3 差分検知による撮影スキップ

```
既存設計（recording-cli-design.md）の差分検知を拡張:

1. 高速プリチェック:
   直前のスクリーンショットから 100ms 以内 → スキップ

2. サムネイル比較:
   元画像を 64x48 にリサイズ → ピクセル差分率計算
   差分率 < 2% → 無変化としてスキップ

3. 領域限定比較（最適化）:
   クリック周辺領域（200x200px）のみ比較
   → 全体比較より高速、局所的な変化も検知可能
```

### 2.4 解像度と圧縮

```
wfth-capture のオプション:
  --quality <level>   low|medium|high|full（デフォルト: medium）
  --max-width <px>    最大幅（デフォルト: 制限なし）
  --format <type>     png|jpg（デフォルト: png）
```

| quality | 解像度 | 圧縮 | 1枚あたり目安 | 用途 |
|---------|--------|------|---------------|------|
| low | 元画像の50% | JPEG 70% | 50〜150KB | CI/大量記録 |
| medium | 元画像の75% | PNG | 200〜500KB | 通常記録 |
| high | 元画像そのまま | PNG | 500KB〜2MB | 詳細検証 |
| full | 元画像そのまま | PNG無圧縮 | 2〜8MB | 画像認識リファレンス |

### 2.5 差分保存（高度な容量削減）

画面遷移がない場合、変化した領域のみをパッチとして保存する。

```
方式:
  1. 初回: フルスクリーンショットを保存（キーフレーム）
  2. 以降: 前回との差分領域を矩形で切り出して保存
  3. 画面遷移（差分率 > 30%）: 新しいキーフレームを保存

capture.ndjson の出力:
  {"ts":"...","file":"screenshots/0001_full.png","type":"keyframe","size":{"w":1920,"h":1080}}
  {"ts":"...","file":"screenshots/0002_diff.png","type":"diff","region":{"x":400,"y":300,"w":200,"h":100},"base":"0001"}
  {"ts":"...","file":"screenshots/0003_full.png","type":"keyframe","reason":"screen_transition"}
```

ただしこの方式は実装コストが高い。MVP では Level 1（変化時のみフル撮影）で十分。

### 2.6 保存ファイル命名規則

```
screenshots/
├── 0001_after.png         ← seq=1 の操作後
├── 0002_before.png        ← seq=2 の操作前（Level 2以上）
├── 0002_after.png         ← seq=2 の操作後
├── 0003_after.png         ← seq=3（beforeは前回afterと同一のためスキップ）
└── transition_0004.png    ← 画面遷移検知（操作に紐付かない自動検出）
```

### 2.7 容量見積もり

| 記録規模 | Level 1 (medium) | Level 2 (medium) | Level 3 (high) |
|----------|-------------------|-------------------|-----------------|
| 30操作（1テストケース） | 10〜20枚 / 5MB | 30〜50枚 / 15MB | 60枚 / 60MB |
| 100操作（3〜5テストケース） | 40〜60枚 / 20MB | 100〜150枚 / 50MB | 200枚 / 200MB |
| 500操作（全テストスイート） | 150〜250枚 / 80MB | 400〜600枚 / 200MB | 1000枚 / 1GB |

### 2.8 wfth-capture CLIインターフェース（暫定）

```
wfth-capture [options]

Target:
  --process <name>       プロセス名
  --hwnd <handle>        ウィンドウハンドル

Trigger:
  --watch-file <path>    入力イベントNDJSONファイルを監視してトリガー
  --watch-stdin          stdin からのイベント行でトリガー
  --interval <ms>        定期撮影（デフォルト: 無効）

Capture:
  --level <n>            撮影レベル 1|2|3（デフォルト: 1）
  --quality <q>          low|medium|high|full（デフォルト: medium）
  --max-width <px>       最大幅
  --diff-threshold <pct> 差分検知閾値パーセント（デフォルト: 2）

Output:
  --out-dir <dir>        スクリーンショット保存ディレクトリ（デフォルト: ./screenshots）
  --out <path>           メタデータNDJSON出力先（デフォルト: stdout）
```

---

## 3. UIAで取れない要素の検出と画像認識用リファレンス画像の自動抽出

### 3.1 問題

以下のケースでUIAによる要素特定ができない:

| ケース | 例 | 理由 |
|--------|-----|------|
| カスタム描画コントロール | グラフ、地図、独自レンダリング領域 | UIAツリーに子要素がない |
| サードパーティコントロール | DevExpress Grid、Infragistics Chart | UIA対応が不完全 |
| Name/AutomationId未設定 | 開発者がアクセシビリティ属性を設定していない | 特定手段がない |
| 仮想化リスト | 大量行のDataGridView | 表示外の行がUIAツリーに存在しない |
| オーナードロー | OwnerDraw で描画したListBox項目 | 個別項目のUIA要素がない |

### 3.2 UIA不可要素の検出

wfth-correlate がアクションごとにUIA特定の結果を評価し、不可要素を検出する。

```
検出基準:
  1. クリック座標に対して AutomationElement.FromPoint() が
     返した要素が「曖昧」な場合:
     - controlType が "Pane", "Window", "Custom" で
       automationId と name が両方空
     - クリック座標が要素の BoundingRectangle の中心から
       大きくずれている（子要素を特定できていない兆候）

  2. 同一操作パターンで UIA 結果が不安定な場合:
     - 同じ座標への繰り返しクリックで異なる要素が返される

  3. UIAツリー上に対応する要素が存在しない場合:
     - FromPoint() が親コンテナしか返さない
```

### 3.3 検出結果の出力

```json
{
  "seq": 7,
  "type": "Click",
  "input": { "sx": 500, "sy": 400, "rx": 280, "ry": 260 },
  "target": {
    "source": "coordinate_only",
    "fallbackReason": "uia_ambiguous",
    "uiaResult": {
      "automationId": "",
      "name": "",
      "controlType": "Pane",
      "className": "ChartControl"
    },
    "referenceImage": "ref_images/seq007_target.png",
    "referenceRegion": {"x": 240, "y": 220, "w": 80, "h": 80}
  }
}
```

`fallbackReason` の値:

| 値 | 意味 |
|----|------|
| `uia_ambiguous` | UIA要素が曖昧（コンテナ要素のみ取得） |
| `uia_empty` | automationId / name が両方空 |
| `uia_unstable` | 同一座標で結果が不安定 |
| `uia_not_found` | FromPoint() が null またはデスクトップ要素 |

### 3.4 リファレンス画像の自動抽出

UIA不可と判定された要素について、クリック周辺のスクリーンショットからリファレンス画像を自動切り出す。

```
抽出フロー:

  1. UIA不可と判定
  2. 操作直前のスクリーンショット（before）を使用
  3. クリック座標を中心に一定範囲を切り出し

切り出しサイズの決定:
  A. UIA の BoundingRectangle が取得できた場合
     → その矩形 + マージン（上下左右8px）で切り出し
  B. 取得できない場合
     → クリック座標を中心に 80x80px（デフォルト）で切り出し
     → コントラストエッジ検出でコントロール境界を推定（将来）
```

```
保存先:
  sessions/rec-20260222-143000/
  ├── screenshots/
  │   └── ...
  └── ref_images/          ← リファレンス画像ディレクトリ
      ├── seq007_target.png
      ├── seq012_target.png
      └── manifest.json    ← リファレンス画像一覧
```

### 3.5 リファレンス画像マニフェスト

```json
{
  "sessionId": "rec-20260222-143000",
  "images": [
    {
      "id": "seq007_target",
      "file": "ref_images/seq007_target.png",
      "sourceScreenshot": "screenshots/0007_before.png",
      "region": {"x": 240, "y": 220, "w": 80, "h": 80},
      "clickPoint": {"rx": 280, "ry": 260},
      "fallbackReason": "uia_ambiguous",
      "uiaContext": {
        "parentAutomationId": "pnlChart",
        "parentControlType": "Pane"
      },
      "capturedAt": "2026-02-22T14:30:25.000Z"
    }
  ]
}
```

### 3.6 リファレンス画像の品質確保

```
品質要件:
  1. 解像度: 元画像と同じ（リサイズしない）
  2. 形式: PNG（無損失）
  3. 余白: コントロール境界 + 8px マージン
  4. 一意性: 背景色やテキストが変わりやすい部分を含まない

品質リスク:
  - 状態依存: ホバー状態やフォーカス状態で見た目が変わる
    → 複数状態のリファレンスを保持（normal, hover, focused）
  - テキスト依存: ボタンのラベルが動的な場合
    → テキスト部分をマスクしたリファレンスも生成（将来）
  - テーマ依存: OS設定やアプリテーマで見た目が変わる
    → セッション単位でリファレンスを管理、テーマ変更時は再記録
```

### 3.7 画像認識への橋渡し（将来の Page Object 生成）

AIエージェントがテストコード（Page Object）を生成する際、UIA不可要素には画像認識ストラテジーを自動設定する:

```csharp
// AIが生成する Page Object
public class ChartFormPage : FormPage
{
    // UIA で特定可能
    public IElement SaveButton => Element(
        Strategy.ByAutomationId("btnSave"),
        Strategy.ByName("保存")
    );

    // UIA 不可 → 自動でリファレンス画像が割り当てられる
    public IElement ChartArea => Element(
        Strategy.ByImage("ref_images/seq007_target.png"),
        Strategy.ByRelativePosition("グラフ表示領域の中央")
    );
}
```

### 3.8 セッションサマリーでのUIA カバレッジレポート

wfth-correlate の出力に UIA カバレッジの統計を含める。
形式は NDJSON のメタ行とする（共通契約は `recording-cli-design.md` の「3.6 統合ログ出力形式」を参照）。

```json
{"seq":1,"type":"Click", ...}
{"seq":2,"type":"TextInput", ...}
{"type":"summary","summaryType":"coverage","metrics":{"totalActions":25,"uiaResolved":22,"uiaFallback":3,"fallbackBreakdown":{"uia_ambiguous":2,"uia_empty":1},"referenceImagesGenerated":3,"coveragePercent":88.0}}
```

テスト自動化の信頼性を事前に把握でき、UIA対応が必要な箇所を特定できる。

---

## 4. 実装優先度

| 機能 | MVP段階 | 理由 |
|------|---------|------|
| 差分検知による撮影スキップ | **MVP C** | 容量削減の基本 |
| quality/level オプション | **MVP C** | ユーザー設定可能にする最低限 |
| ノイズ分類（空クリック、連打） | **MVP D** | correlate の基本機能 |
| UIA不可検出 + リファレンス画像抽出 | **MVP D** | correlate + capture連携 |
| ウィンドウ操作の分類 | MVP D+ | あると便利だが必須ではない |
| IMEグルーピング | MVP D+ | 日本語環境では重要だが複雑 |
| 操作ミス検出 | 将来 | ヒューリスティクスの精度次第 |
| 差分保存（パッチ方式） | 将来 | 容量問題が顕在化してから |
| コントラストエッジ検出 | 将来 | 画像認識ライブラリ選定後 |
| 複数状態リファレンス | 将来 | 基本機能が安定してから |

---

## 5. パスワードフィールドのデータ保護

### 5.1 問題

Logger（`logger-architecture-design.md`）はパスワードフィールドの値をマスクするが、Recording パイプライン全体では以下の箇所にパスワードが残る可能性がある:

| データソース | パスワードの残存箇所 | 対策の必要性 |
|---|---|---|
| アプリ内ロガー（Logger） | TextChanged イベントの old/new 値 | **対策済み**（PasswordDetector でマスク） |
| wfth-record（キーボードイベント） | キー入力シーケンスの生データ | **要対策** |
| wfth-correlate（統合ログ） | TextInput アクションの text フィールド | **要対策** |
| スクリーンショット | パスワード入力欄の視覚的表示 | 将来検討 |

### 5.2 wfth-correlate でのパスワードマスク

wfth-correlate が統合ログを生成する際、以下の条件でパスワードフィールドへの入力テキストをマスクする:

```
判定条件:
  1. アクションの target が UIA で特定されている
  2. target の ControlType が "Edit"（TextBox）
  3. 以下のいずれかを満たす:
     a. アプリ内ロガーの ControlInfo で IsPasswordField = true
     b. UIA の IsPassword プロパティが true
     c. UIA の Name に "パスワード", "password", "暗証" 等のキーワードを含む

マスク処理:
  TextInput アクションの input.text を "***" に置換
  対応するキーボード生イベント列も masked: true をマーク
```

出力例:

```json
{
  "seq": 5,
  "type": "TextInput",
  "input": { "text": "***", "masked": true, "duration": 1.2 },
  "target": {
    "source": "UIA",
    "automationId": "txtPassword",
    "name": "パスワード",
    "controlType": "Edit",
    "isPassword": true
  }
}
```

### 5.3 スクリーンショットのパスワード領域ぼかし

スクリーンショットにパスワード入力欄が映る場合、その領域をぼかす処理は**将来検討**とする。

理由:
- WinForms の TextBox は PasswordChar 設定時に `●●●` 表示となるため、通常はスクリーンショットに平文パスワードが映ることはない
- PasswordChar 未設定でもビジネスロジック側でマスクしているケースがある
- 画像処理のコストと実装複雑性に対して、リスクが限定的

ただし、以下のケースではリスクがある:
- カスタム描画のパスワードフィールド（PasswordChar が機能しない）
- パスワード入力中の一時的な平文表示（「パスワードを表示」ボタン）

これらは `wfth-correlate --mask-screenshots` オプションとして将来実装を検討する。

### 5.4 実装優先度

| 機能 | MVP段階 | 理由 |
|------|---------|------|
| wfth-correlate でのパスワードテキストマスク | **MVP D** | correlate の基本機能と同時 |
| UIA IsPassword 判定 | **MVP D** | UIA プロパティで判定可能 |
| アプリ内ロガー連携判定 | MVP D+ | IPC 連携が前提 |
| スクリーンショットのパスワード領域ぼかし | 将来 | 画像処理実装が必要 |
