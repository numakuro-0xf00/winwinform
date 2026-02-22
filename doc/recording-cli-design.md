# Recording パイプライン CLI設計

## 1. 設計方針

Unix哲学に基づく独立パイプライン型。各CLIツールは単一責務でNDJSONストリームを出力し、組み合わせて使用する。

```
wfth-record   — 入力イベント（マウス/KB）+ スクリーンショット → NDJSON
wfth-inspect  — UIAツリー変化を監視 → NDJSON（実装済み）
wfth-capture  — スクリーンショット単体撮影（デバッグ/独立利用）→ NDJSON + PNG
wfth-correlate — 複数NDJSONを時系列マージ → 統合ログJSON

※ スクリーンショット撮影の詳細設計 → capture-design.md
※ wfth-record が --capture オプションで撮影を統合（主要ユースケース）
```

---

## 2. wfth-record（入力イベント記録）

### 2.1 CLIインターフェース

```
wfth-record [options]

Target（いずれか1つ必須）:
  --process <name>      プロセス名で対象指定（部分一致、大文字小文字無視）
  --hwnd <handle>       ウィンドウハンドル（0xHHHH形式）
  --launch <path>       実行ファイルを起動してから記録開始

Options:
  --launch-args <args>  --launch時のコマンドライン引数
  --out <path>          出力ファイル（デフォルト: stdout）
  --filter <type>       mouse|keyboard|all（デフォルト: all）
  --no-mousemove        MouseMoveイベントを除外（ドラッグ中のMoveは記録する）
```

### 2.2 動作フロー

```
[--process / --hwnd の場合]
1. 対象ウィンドウを検索・取得
2. session/start マーカー出力
3. SetWindowsHookEx でグローバルフック設定
4. イベントをNDJSON出力（stdout or --out）
5. Ctrl+C → フック解除 → session/stop マーカー出力

[--launch の場合]
1. Process.Start() で対象アプリを起動
2. メインウィンドウ出現を待機（タイムアウト30秒）
3. hwnd を取得
4. 以降は上記と同じ
5. 記録停止後もアプリは終了しない（手動で閉じる）
```

### 2.3 対象ウィンドウ判定

グローバルフックで全入力を受けるが、対象ウィンドウへの入力のみを記録する。

```
判定ロジック:
  foreground = GetForegroundWindow()
  isTarget = foreground == targetHwnd
           || IsChild(targetHwnd, foreground)        // 子ウィンドウ
           || GetRootOwner(foreground) == targetHwnd  // モーダルダイアログ
```

モーダルダイアログ（SearchForm等）は `GetRootOwner` で親ウィンドウをたどることで対象に含める。

### 2.4 出力NDJSON形式

全イベントは `ts`（ISO 8601 UTC）と `type` を持つ。

#### セッションマーカー

```json
{"ts":"2026-02-22T14:30:00.000Z","type":"session","action":"start","process":"SampleApp","pid":12345,"hwnd":"0x001A0F32","cmdline":"C:\\path\\SampleApp.exe"}
{"ts":"2026-02-22T14:32:05.300Z","type":"session","action":"stop","reason":"user_interrupt","duration":125.3}
```

#### マウスイベント

```json
{"ts":"...","type":"mouse","action":"LeftDown","sx":450,"sy":320,"rx":230,"ry":180}
{"ts":"...","type":"mouse","action":"LeftUp","sx":450,"sy":320,"rx":230,"ry":180}
{"ts":"...","type":"mouse","action":"RightDown","sx":500,"sy":300,"rx":280,"ry":160}
{"ts":"...","type":"mouse","action":"RightUp","sx":500,"sy":300,"rx":280,"ry":160}
{"ts":"...","type":"mouse","action":"Move","sx":460,"sy":325,"rx":240,"ry":185,"drag":true}
{"ts":"...","type":"mouse","action":"WheelUp","sx":450,"sy":320,"rx":230,"ry":180,"delta":120}
{"ts":"...","type":"mouse","action":"WheelDown","sx":450,"sy":320,"rx":230,"ry":180,"delta":-120}
```

フィールド:
- `action`: LeftDown, LeftUp, RightDown, RightUp, MiddleDown, MiddleUp, Move, WheelUp, WheelDown
- `sx`, `sy`: スクリーン座標（絶対）
- `rx`, `ry`: 対象ウィンドウ相対座標
- `drag`: ドラッグ中のMoveの場合 true（任意フィールド）
- `delta`: ホイールの場合のみ

#### キーボードイベント（生イベント）

```json
{"ts":"...","type":"key","action":"down","vk":16,"key":"ShiftLeft","scan":42,"modifier":true}
{"ts":"...","type":"key","action":"down","vk":84,"key":"T","scan":20,"char":"T"}
{"ts":"...","type":"key","action":"up","vk":84,"key":"T","scan":20}
{"ts":"...","type":"key","action":"up","vk":16,"key":"ShiftLeft","scan":42,"modifier":true}
```

フィールド:
- `action`: down, up
- `vk`: 仮想キーコード
- `key`: キー名（人間可読）
- `scan`: スキャンコード
- `char`: 入力文字（印字可能キーのdown時のみ、任意フィールド）
- `modifier`: 修飾キーの場合 true（任意フィールド）

#### ウィンドウイベント

対象アプリの子ウィンドウ/モーダルダイアログの出現・消滅・フォーカス移動を記録。

```json
{"ts":"...","type":"window","action":"activated","hwnd":"0x002B1234","title":"検索","class":"SearchForm"}
{"ts":"...","type":"window","action":"deactivated","hwnd":"0x002B1234","title":"検索"}
{"ts":"...","type":"window","action":"closed","hwnd":"0x002B1234","title":"検索"}
```

### 2.5 アーキテクチャ

```
Program.cs (System.CommandLine)
  ├── Hooks/
  │   ├── NativeMethods.cs        — P/Invoke 定義
  │   ├── MouseHook.cs            — WH_MOUSE_LL グローバルフック
  │   ├── KeyboardHook.cs         — WH_KEYBOARD_LL グローバルフック
  │   └── WindowTracker.cs        — 対象ウィンドウ判定 + モーダル追跡
  ├── Events/
  │   ├── InputEvent.cs           — マウス/キーボードイベント基底
  │   ├── MouseEvent.cs           — マウスイベントDTO
  │   ├── KeyEvent.cs             — キーボードイベントDTO
  │   ├── WindowEvent.cs          — ウィンドウイベントDTO
  │   └── SessionEvent.cs         — セッション開始/終了マーカー
  ├── Output/
  │   └── NdJsonWriter.cs         — NDJSONシリアライザ
  └── RecordingSession.cs         — セッションライフサイクル管理
```

---

## 3. wfth-correlate（イベント統合）

### 3.1 CLIインターフェース

```
wfth-correlate [options] [session-dir]

Inputs（セッションディレクトリの規約ファイル名を自動検出、または明示指定）:
  --record <path>      wfth-record出力NDJSON（入力イベント + スクリーンショット混在）
  --uia <path>         UIAツリー変化NDJSON（wfth-inspect watch出力）
  --app-log <path>     アプリ内ロガーNDJSON（将来、IPC経由）

Options:
  -o, --output <path>  出力ファイル（デフォルト: stdout）
  --text-timeout <ms>  キー入力→TextInput集約タイムアウト（デフォルト: 500）
  --window <ms>        イベント相関の時間窓（デフォルト: 2000）
  --format <type>      json|ndjson（デフォルト: json）
```

### 3.2 動作フロー

```
1. record.ndjson を読み込み、type フィールドで入力系（mouse, key, window, session）と screenshot に分類
2. uia.ndjson, app-log.ndjson を読み込み
3. 全イベントをタイムスタンプでソート
4. 入力イベントを「操作アクション」に変換:
   a. マウス: LeftDown + LeftUp（近接）→ Click、Move(drag) → Drag
   b. キーボード: 連続キー入力 → TextInput に集約（--text-timeout で区切り）
   c. 特殊キー（Enter, Tab, Escape等）→ 独立アクション
5. 各アクションに対して時間窓（--window）内の関連イベントを紐付け:
   - UIA変化（アクション後 0〜window ms）
   - スクリーンショット（record.ndjson内のscreenshot型イベント、before/after）
   - アプリ内ログ（アクション前後 window ms）
6. 統合ログ（session.json）を出力
```

### 3.3 セッションディレクトリ規約

```
sessions/rec-20260222-143000/
├── record.ndjson      ← wfth-record 出力（入力イベント + スクリーンショットメタデータ）
├── uia.ndjson         ← wfth-inspect watch 出力
├── screenshots/       ← wfth-record --capture が保存するPNGファイル
│   ├── 0001_before.png
│   ├── 0001_after.png
│   ├── 0002_after.png
│   └── ...
└── session.json       ← wfth-correlate 出力
```

`wfth-correlate sessions/rec-20260222-143000/` でディレクトリを指定すると、規約ファイル名（record.ndjson, uia.ndjson）を自動検出する。存在しないファイルはスキップ（警告出力）。

### 3.4 入力イベントの集約ルール

#### マウスクリック判定

```
LeftDown(t=T) + LeftUp(t=T+Δ) where Δ < 300ms → Click
LeftDown(t=T) + Move(drag=true) + ... + LeftUp → DragAndDrop
RightDown + RightUp → RightClick
```

ダブルクリック: 2つのClickが500ms以内 → DoubleClick

#### テキスト入力集約

```
key(down, T) + key(down, T+50) + key(down, T+100) + [500ms無入力]
→ TextInput { text: "abc", startTs: T, endTs: T+100 }
```

修飾キー（Shift/Ctrl/Alt）は集約に含めない。
特殊キー（Enter, Tab, Escape, Delete, Backspace, F1-F12等）は集約を中断し独立アクションになる。

### 3.5 統合ログ出力形式

既存設計ドキュメント（セクション6）の形式に準拠。

```json
{
  "session": {
    "id": "rec-20260222-143000",
    "targetApp": "SampleApp.exe",
    "startedAt": "2026-02-22T14:30:00.000Z",
    "endedAt": "2026-02-22T14:32:05.300Z",
    "duration": 125.3
  },
  "actions": [
    {
      "seq": 1,
      "ts": "2026-02-22T14:30:05.123Z",
      "type": "Click",
      "input": {
        "button": "Left",
        "sx": 450, "sy": 320,
        "rx": 230, "ry": 180
      },
      "target": {
        "source": "UIA",
        "automationId": "btnSearch",
        "name": "検索",
        "controlType": "Button",
        "rect": {"x":420,"y":310,"w":80,"h":30}
      },
      "screenshots": {
        "before": "screenshots/0001_before.png",
        "after": "screenshots/0001_after.png"
      },
      "uiaDiff": {
        "added": [
          {"automationId":"","name":"検索","controlType":"Window"}
        ],
        "removed": [],
        "changed": []
      },
      "appLog": []
    },
    {
      "seq": 2,
      "ts": "2026-02-22T14:30:08.456Z",
      "type": "TextInput",
      "input": {
        "text": "田中",
        "duration": 0.3
      },
      "target": {
        "source": "UIA",
        "automationId": "txtSearchCondition",
        "name": "検索条件",
        "controlType": "Edit"
      },
      "screenshots": {
        "after": "screenshots/0002_after.png"
      },
      "uiaDiff": null,
      "appLog": [
        {"ts":"...","type":"PropertyChanged","control":"txtSearchCondition","property":"Text","value":"田中"}
      ]
    },
    {
      "seq": 3,
      "ts": "2026-02-22T14:30:10.789Z",
      "type": "SpecialKey",
      "input": {
        "key": "Enter"
      },
      "target": {
        "source": "UIA",
        "automationId": "txtSearchCondition",
        "controlType": "Edit"
      },
      "screenshots": {
        "before": "screenshots/0003_before.png",
        "after": "screenshots/0003_after.png"
      },
      "uiaDiff": {
        "changed": [
          {"automationId":"dgvResults","property":"RowCount","from":0,"to":1}
        ]
      },
      "appLog": []
    }
  ]
}
```

### 3.6 アーキテクチャ

```
Program.cs (System.CommandLine)
  ├── Readers/
  │   ├── NdJsonReader.cs            — NDJSON汎用リーダー
  │   ├── RecordEventReader.cs       — wfth-record出力パーサー（input + screenshot 分類）
  │   ├── UiaEventReader.cs          — wfth-inspect watch出力パーサー
  │   └── AppLogReader.cs            — アプリ内ロガー出力パーサー
  ├── Aggregation/
  │   ├── MouseClickAggregator.cs    — Down+Up → Click/DoubleClick/Drag
  │   ├── KeySequenceAggregator.cs   — 連続キー → TextInput
  │   └── ActionBuilder.cs          — 集約済みアクション生成
  ├── Correlation/
  │   ├── TimeWindowCorrelator.cs    — 時間窓ベースのイベント紐付け
  │   └── UiaTargetResolver.cs       — クリック座標→UIA要素の逆引き
  ├── Models/
  │   ├── CorrelatedAction.cs        — 統合済みアクション
  │   └── SessionLog.cs             — セッション全体の統合ログ
  └── Output/
      └── SessionJsonWriter.cs       — session.json出力
```

---

## 4. パイプライン使用例

### 基本: SampleAppを記録

```bash
SESSION=sessions/rec-$(date +%Y%m%d-%H%M%S)
mkdir -p $SESSION/screenshots

# ツール並列起動（wfth-record が --capture でスクリーンショットも統合）
wfth-record  --process SampleApp --capture \
             --capture-dir $SESSION/screenshots  > $SESSION/record.ndjson &
PID_RECORD=$!
wfth-inspect watch --process SampleApp           > $SESSION/uia.ndjson &
PID_INSPECT=$!

# 手動テスト実施...

# Ctrl+C or kill で停止
kill $PID_RECORD $PID_INSPECT
wait

# 統合ログ生成
wfth-correlate $SESSION/ -o $SESSION/session.json
```

### アプリ起動から記録

```bash
SESSION=sessions/rec-$(date +%Y%m%d-%H%M%S)
mkdir -p $SESSION/screenshots

# アプリ起動 + 記録開始（スクリーンショット付き）
wfth-record --launch "C:\path\SampleApp.exe" --capture \
            --capture-dir $SESSION/screenshots > $SESSION/record.ndjson &
PID_RECORD=$!

# wfth-record がstartマーカーを出力するまで待機してからinspect開始
sleep 2
PROC=SampleApp
wfth-inspect watch --process $PROC > $SESSION/uia.ndjson &

# ... テスト実施 → 停止 → correlate
```

### スクリーンショット単体（デバッグ用）

```bash
# 1回だけスナップショット
wfth-capture --process SampleApp --once --out-dir ./debug

# 変化監視モードで定期撮影
wfth-capture --process SampleApp --interval 1000 > captures.ndjson
```

---

## 5. 今後の設計課題

### 全体
- 各ツールの終了シグナル伝搬（1つ停止したら全停止するか）
- エラー発生時のリカバリ（入力フックのクリーンアップ等） → recording-reliability-design.md で設計済み
- 高DPI / マルチモニタ環境での座標正規化 → recording-reliability-design.md で設計済み
- セッションディレクトリの管理（一覧、削除、アーカイブ）
- CIヘッドレス環境での実行可能性 → recording-integration-design.md で設計済み

### 関連設計ドキュメント
- `capture-design.md` — スクリーンショットキャプチャ詳細設計（共有ライブラリ + CLIラッパー）
- `recording-reliability-design.md` — 信頼性・安定性設計
- `recording-data-quality-design.md` — データの質と量設計
- `recording-integration-design.md` — 外部連携設計
