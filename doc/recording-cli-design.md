# Recording パイプライン CLI設計

## 1. 設計方針

Unix哲学に基づく独立パイプライン型。各CLIツールは単一責務でNDJSONストリームを出力し、組み合わせて使用する。

```
wfth-record    — 入力イベント（マウス/KB）+ スクリーンショット → NDJSON
wfth-inspect   — UIAツリー変化を監視 → NDJSON（実装済み）
wfth-capture   — スクリーンショット単体撮影（デバッグ/独立利用）→ NDJSON + PNG
wfth-aggregate — 生イベント → 集約アクション（Click, TextInput等）→ NDJSON
wfth-correlate — 集約済みアクションに UIA変化・スクリーンショットを時間窓で紐付け → NDJSON

※ スクリーンショット撮影の詳細設計 → capture-design.md
※ wfth-record が --capture オプションで撮影を統合（主要ユースケース）
※ wfth-aggregate / wfth-correlate 分割の詳細 → correlate-split-design.md
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

## 3. wfth-aggregate + wfth-correlate（イベント集約・統合）

> **設計変更**: UNIX思想レビューに基づき、旧 `wfth-correlate` を `wfth-aggregate`（入力集約）と `wfth-correlate`（時間窓相関）に分割。詳細は `correlate-split-design.md` を参照。

### 3.1 パイプライン構成

```bash
# 基本パイプライン
wfth-aggregate < record.ndjson | wfth-correlate --uia uia.ndjson > session.ndjson

# jq でフィルタ
wfth-aggregate < record.ndjson | jq 'select(.type == "Click")'
```

### 3.2 wfth-aggregate CLIインターフェース

```
wfth-aggregate [options]

Input:
  stdin                     wfth-record 出力 NDJSON

Options:
  --text-timeout <ms>       キー入力集約タイムアウト（デフォルト: 500）
  --click-timeout <ms>      Click判定タイムアウト（デフォルト: 300）
  --debug                   診断情報を stderr に出力
  --quiet                   stderr 出力を抑制
```

### 3.3 wfth-correlate CLIインターフェース

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

**デフォルト出力は NDJSON**（旧設計のモノリシック JSON から変更）。

### 3.4 セッションディレクトリ規約

```
sessions/rec-20260222-143000/
├── record.ndjson      ← wfth-record 出力（入力イベント + スクリーンショットメタデータ）
├── uia.ndjson         ← wfth-inspect watch 出力
├── screenshots/       ← wfth-record --capture が保存するPNGファイル
│   ├── 0001_before.png
│   ├── 0001_after.png
│   ├── 0002_after.png
│   └── ...
└── session.ndjson     ← wfth-correlate 出力（NDJSON）
```

### 3.5 入力イベントの集約ルール（wfth-aggregate 担当）

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

### 3.6 統合ログ出力形式（wfth-correlate 出力）

NDJSON形式で1行1レコード（通常はアクション）:

```json
{"seq":1,"ts":"2026-02-22T14:30:05.123Z","type":"Click","input":{"button":"Left","sx":450,"sy":320,"rx":230,"ry":180},"target":{"source":"UIA","automationId":"btnSearch","name":"検索","controlType":"Button","rect":{"x":420,"y":310,"w":80,"h":30}},"screenshots":{"before":"screenshots/0001_before.png","after":"screenshots/0001_after.png"},"uiaDiff":{"added":[{"automationId":"","name":"検索","controlType":"Window"}]}}
{"seq":2,"ts":"2026-02-22T14:30:08.456Z","type":"TextInput","input":{"text":"田中","duration":0.3},"target":{"source":"UIA","automationId":"txtSearchCondition","name":"検索条件","controlType":"Edit"},"screenshots":{"after":"screenshots/0002_after.png"}}
{"seq":3,"ts":"2026-02-22T14:30:10.789Z","type":"SpecialKey","input":{"key":"Enter"},"target":{"source":"UIA","automationId":"txtSearchCondition","controlType":"Edit"},"screenshots":{"before":"screenshots/0003_before.png","after":"screenshots/0003_after.png"},"uiaDiff":{"changed":[{"automationId":"dgvResults","property":"RowCount","from":0,"to":1}]}}
```

セッション末尾の集計情報は NDJSON メタ行で出力する:

```json
{"type":"summary","summaryType":"correlation","metrics":{"totalActions":25,"withAppLog":20,"withoutAppLog":5}}
{"type":"summary","summaryType":"coverage","metrics":{"totalActions":25,"uiaResolved":22,"uiaFallback":3}}
```

`summary` メタ行の契約（正規定義）:
- `type` は固定で `"summary"`
- `summaryType` は `"correlation"` または `"coverage"`（将来拡張可）
- `metrics` は `summaryType` ごとのオブジェクト（未知キーは将来拡張として許容）
- 出力タイミングは通常アクション列の**末尾**
- 下流フィルタは `select(.type != "summary")` で通常アクションのみ抽出可能

### 3.7 アーキテクチャ

#### wfth-aggregate

```
Program.cs (System.CommandLine)
  ├── Aggregation/
  │   ├── MouseClickAggregator.cs    — Down+Up → Click/DoubleClick/Drag
  │   ├── KeySequenceAggregator.cs   — 連続キー → TextInput
  │   └── ActionBuilder.cs           — 集約済みアクション生成
  └── Models/
      └── AggregatedAction.cs        — 集約済みアクションDTO
```

#### wfth-correlate

```
Program.cs (System.CommandLine)
  ├── Readers/
  │   ├── UiaEventReader.cs          — wfth-inspect watch出力パーサー
  │   └── AppLogReader.cs            — アプリ内ロガー出力パーサー
  ├── Correlation/
  │   ├── TimeWindowCorrelator.cs    — 時間窓ベースのイベント紐付け
  │   └── UiaTargetResolver.cs       — クリック座標→UIA要素の逆引き
  └── Models/
      └── CorrelatedAction.cs        — 統合済みアクション
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

# 統合ログ生成（aggregate → correlate パイプライン）
wfth-aggregate < $SESSION/record.ndjson \
  | wfth-correlate --uia $SESSION/uia.ndjson \
                   --screenshots $SESSION/screenshots \
  > $SESSION/session.ndjson
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

# ... テスト実施 → 停止 → aggregate + correlate
```

### スクリーンショット単体（デバッグ用）

```bash
# 1回だけスナップショット
wfth-capture --process SampleApp --once --out-dir ./debug

# 変化監視モードで定期撮影
wfth-capture --process SampleApp --interval 1000 > captures.ndjson
```

### jq との連携（レシピ集）

```bash
# タイプでフィルタ
jq 'select(.type == "mouse")' < record.ndjson

# 複数ストリームのマージ＆ソート
jq -s 'sort_by(.ts)' record.ndjson uia.ndjson

# 集約済みアクションからクリックのみ抽出
wfth-aggregate < record.ndjson | jq 'select(.type == "Click")'

# ノイズ除去済みのみ
wfth-aggregate < record.ndjson \
  | wfth-correlate --uia uia.ndjson \
  | jq 'select(.type != "summary" and .noise == null)'
```

---

## 5. 全CLIツール共通規約

UNIX思想レビューに基づき、全ツールに適用する共通規約。

### 5.1 終了コード

| コード | 意味 | 例 |
|--------|------|-----|
| 0 | 成功 | — |
| 1 | 引数エラー | 必須オプション不足 |
| 2 | 対象未発見 | プロセス・UI要素が見つからない |
| 3 | 実行時エラー | UIA操作失敗、I/Oエラー |

定義: `WinFormsTestHarness.Common.Cli.ExitCodes`

### 5.2 診断フラグ

全ツールに以下のグローバルオプションを追加:
- `--debug`: 診断情報を stderr に出力（フック状態、キュー深度、処理速度等）
- `--quiet`: stderr 出力を抑制（エラーのみ出力）

### 5.3 出力規約

- **stdout**: データ出力のみ（NDJSON）。パイプライン合成可能
- **stderr**: エラー・警告・診断情報。`--quiet` で警告・情報を抑制、エラーは常に出力

### 5.4 NDJSON 出力のバッファリング規約

全 CLI ツールの NDJSON 出力は**行バッファモード**を使用する。

```csharp
// Program.cs の冒頭で設定
Console.Out.AutoFlush = true;
```

理由:
- `wfth-correlate` がタイムスタンプでストリームをマージする際、書き込みバッファリングのタイミングによりファイル上の行順序がタイムスタンプ順と異なる可能性がある
- 行バッファモードにより、1行（1イベント）の書き込み完了が即座にフラッシュされ、パイプライン合成時の順序保証を強化する
- correlate 側のタイムスタンプソートで最終的な順序は保証されるが、入力データの前提条件として行バッファを規約化する

### 5.5 共通ライブラリ

`WinFormsTestHarness.Common` — 詳細は `common-library-design.md` を参照。

---

## 6. 今後の設計課題

### 全体
- 各ツールの終了シグナル伝搬（1つ停止したら全停止するか）
- エラー発生時のリカバリ（入力フックのクリーンアップ等） → recording-reliability-design.md で設計済み
- 高DPI / マルチモニタ環境での座標正規化 → recording-reliability-design.md で設計済み
- セッションディレクトリの管理（一覧、削除、アーカイブ）
- CIヘッドレス環境での実行可能性 → recording-integration-design.md で設計済み

### `wfth-session` オーケストレーターCLI の検討

現在の Recording セッションでは `wfth-record` + `wfth-inspect` の2プロセス並列起動をユーザーがシェルスクリプトで手動管理する設計である。以下の課題を解消するため、`wfth-session` オーケストレーターCLI の導入を検討する:

- **プロセス間のシグナル伝搬**: 一方がクラッシュした場合の検知と全停止
- **セッションディレクトリの自動管理**: 作成・命名・後処理の自動化
- **初回ユーザーの敷居低減**: 1コマンドでRecordingセッション開始

想定インターフェース:
```bash
wfth-session start --process SampleApp --out-dir ./sessions
# 内部で wfth-record + wfth-inspect を子プロセスとして起動・監視
# Ctrl+C で一括停止 → 自動で wfth-aggregate | wfth-correlate を実行
```

シェルスクリプトのパイプライン操作も引き続きサポートし、後方互換性を保つ。MVP 後の対応で構わないが、設計の TODO として記録する。

### グローバルフックとEDR/セキュリティソフトの干渉リスク

`WH_MOUSE_LL` / `WH_KEYBOARD_LL` は低レベルグローバルフックであり、エンタープライズ環境のEDR（Endpoint Detection and Response）やセキュリティソフトがブロックする可能性がある。WinForms レガシーアプリが多い「現場」で遭遇する可能性が高いリスクとして認識しておく。

対策候補:
- フック設定失敗時のエラーメッセージに「セキュリティソフトの除外設定」を案内
- フックの代替手段（UI Automation イベントベースの入力監視）の将来検討
- 導入ガイドにEDR除外設定の手順を記載

### System.CommandLine のバージョンリスク

全 CLI ツールが `System.CommandLine 2.0.0-beta4.22272.1`（2022年のベータ版）を使用している。GA リリース時に API が変更される可能性がある。

対策:
- System.CommandLine の GA リリース状況を定期的に確認
- 代替 CLI ライブラリ（Cocona、Spectre.Console.Cli 等）も選択肢として記録
- ベータ版から GA 版へのマイグレーションコストが大きい場合はライブラリ切り替えも検討

### 関連設計ドキュメント
- `capture-design.md` — スクリーンショットキャプチャ詳細設計（共有ライブラリ + CLIラッパー）
- `correlate-split-design.md` — wfth-correlate 分割設計（UNIX思想レビュー反映）
- `common-library-design.md` — 共通ライブラリ設計
- `recording-reliability-design.md` — 信頼性・安定性設計
- `recording-data-quality-design.md` — データの質と量設計
- `recording-integration-design.md` — 外部連携設計
