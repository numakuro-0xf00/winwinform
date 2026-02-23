# MVP 実装手順書

各 MVP の詳細な実装手順。設計レビュー（`done/design-critical-review-2026-02-22.md`）の指摘事項を反映済み。

## 依存関係と実装順序

```
MVP A (wfth-inspect)  ✅ 完了
  │
  ├── MVP B (wfth-record)      ← グローバルフック + 入力記録
  │     │
  │     └── MVP C (wfth-capture) ← スクリーンショット (record --capture で統合)
  │
  ├── MVP D-1 (wfth-aggregate) ← 生イベント集約
  │     │
  │     └── MVP D-2 (wfth-correlate) ← 時間窓相関
  │
  ├── Logger (WinFormsTestHarness.Logger) ← アプリ内ロガー
  │
  └── MVP E (WinFormsTestHarness.Core)   ← テスト実行フレームワーク
```

**推奨実装順**: MVP B → MVP C → Logger → MVP D-1 → MVP D-2 → MVP E

理由:
- MVP B が記録の基盤。スクリーンショットなしの入力記録だけで価値がある
- MVP C は MVP B の `--capture` 統合が主要ユースケース
- Logger はアプリ内イベントを提供し、MVP D-2 の相関入力になる
- MVP D は B/C/Logger の出力を消費する後段処理
- MVP E は全パイプラインの成果物（session.ndjson）を活用するフレームワーク

---

## 前提: 設計レビュー指摘の解消（実装開始前）

実装前に以下の設計不整合を解消する。`done/design-critical-review-2026-02-22.md` 参照。

### Step 0-1: wfth-correlate CLI 契約の統一

- `recording-cli-design.md` の stdin ベース I/F を正とする
- `capture-design.md` の `--record` 参照を削除
- `spec-parser-design.md` の `$SESSION/` ディレクトリ指定例を明示引数に更新
- 全ドキュメントのパイプライン例を以下に統一:
  ```
  wfth-aggregate < record.ndjson | wfth-correlate --uia uia.ndjson > session.ndjson
  ```

### Step 0-2: 出力フォーマットの統一

- 正規出力は **NDJSON**（`session.ndjson`）
- JSON 集約は `jq -s` または将来の `wfth-session` で変換
- `capture-design.md` と `spec-parser-design.md` の `session.json` 参照を `session.ndjson` に修正

### Step 0-3: ノイズ分類責務の確定

- **correlate が担当**に統一（UIA変化との突合が必要なため aggregate 単独では判定不可能）
- `recording-cli-design.md` の `--no-denoise` を aggregate から削除
- `recording-data-quality-design.md` のノイズ分類セクションを correlate 担当として明記

### Step 0-4: IPC 接続保護の追加

- パイプ名を `WinFormsTestHarness_{pid}_{sessionNonce}` に変更（予測困難化）
- ACL を同一ユーザー SID に限定する設計を `recording-integration-design.md` に追記
- hello/challenge/response ハンドシェイクを追記

### Step 0-5: フック生存監視の誤検知抑制

- `GetLastInputInfo` による入力活動チェックを監視ロジックに追加
- 「入力あり かつ コールバック未着」のみ異常判定
- 自己テストパルスによる最終確認ステップを追加
- `recording-reliability-design.md` を更新

### Step 0-6: キュー劣化方針の定義

- `BoundedChannel<T>(capacity: 4096)` を採用
- 80% で lossy イベント間引き、95% で lossy イベント優先破棄
- 破棄発生時に `system` イベントを NDJSON 出力
- `recording-reliability-design.md` に追記

### Step 0-7: 全体設計書の更新

- `winforms-e2e-test-platform-design.md` の EventCorrelator セクションに「分割後の構成は `correlate-split-design.md` を参照」注記を追加

---

## MVP B: wfth-record（入力イベント記録）

**ゴール**: WinFormsアプリへのマウス/キーボード入力をグローバルフックでキャプチャし、NDJSON で出力する。

### 前提条件
- WinFormsTestHarness.Common が実装済み（✅）

### Step B-1: プロジェクト設定

**対象ファイル**: `src/WinFormsTestHarness.Record/WinFormsTestHarness.Record.csproj`

1. csproj を更新:
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <OutputType>Exe</OutputType>
       <TargetFramework>net8.0-windows</TargetFramework>
       <PackAsTool>true</PackAsTool>
       <ToolCommandName>wfth-record</ToolCommandName>
       <ApplicationManifest>app.manifest</ApplicationManifest>
     </PropertyGroup>
     <ItemGroup>
       <ProjectReference Include="..\WinFormsTestHarness.Common\WinFormsTestHarness.Common.csproj" />
       <PackageReference Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />
     </ItemGroup>
   </Project>
   ```
2. `app.manifest` を作成（Per-Monitor V2 DPI Awareness 設定）

### Step B-2: P/Invoke 定義

**作成ファイル**: `src/WinFormsTestHarness.Record/Hooks/NativeMethods.cs`

以下の Win32 API を定義:
- `SetWindowsHookEx` (WH_MOUSE_LL=14, WH_KEYBOARD_LL=13)
- `UnhookWindowsHookEx`
- `CallNextHookEx`
- `GetForegroundWindow`
- `GetWindowThreadProcessId`
- `GetAncestor` (GA_ROOTOWNER=3)
- `IsChild`
- `GetWindowRect`
- `SetCursorPos`
- `MonitorFromPoint`
- `GetDpiForMonitor`
- `GetLastInputInfo`
- `IsHungAppWindow`
- `SendMessageTimeout`
- `SetWinEventHook` / `UnhookWinEvent`
- `IsWindowEnabled`
- `GetWindow` (GW_OWNER=4)
- `GetClassName`
- `GetWindowText`
- 関連する構造体: `MSLLHOOKSTRUCT`, `KBDLLHOOKSTRUCT`, `POINT`, `RECT`, `LASTINPUTINFO`, `MONITORINFO`
- デリゲート型: `LowLevelMouseProc`, `LowLevelKeyboardProc`, `WinEventDelegate`

### Step B-3: イベントモデル定義

**作成ファイル**: `src/WinFormsTestHarness.Record/Events/` 配下

1. `InputEvent.cs` — 全イベント共通の基底クラス
   ```csharp
   public abstract class InputEvent
   {
       public string Ts { get; set; }  // ISO 8601 UTC
       public string Type { get; set; }
   }
   ```

2. `MouseEvent.cs` — マウスイベント DTO
   - プロパティ: `Action`, `Sx`, `Sy`, `Rx`, `Ry`, `Drag`, `Delta`, `Dpi`, `Monitor`
   - Action 値: LeftDown, LeftUp, RightDown, RightUp, MiddleDown, MiddleUp, Move, WheelUp, WheelDown

3. `KeyEvent.cs` — キーボードイベント DTO
   - プロパティ: `Action`(down/up), `Vk`, `Key`, `Scan`, `Char`, `Modifier`

4. `WindowEvent.cs` — ウィンドウイベント DTO
   - プロパティ: `Action`(activated/deactivated/closed), `Hwnd`, `Title`, `Class`, `Modal`

5. `SessionEvent.cs` — セッションマーカー DTO
   - プロパティ: `Action`(start/stop), `Process`, `Pid`, `Hwnd`, `Cmdline`, `Reason`, `Duration`, `Monitors`

6. `SystemEvent.cs` — システムイベント DTO
   - プロパティ: `Action`(hook_recovered/hook_lost/app_hung/app_responsive/queue_pressure/queue_overflow/ipc_disconnected), 追加メタデータ

### Step B-4: ウィンドウ追跡

**作成ファイル**: `src/WinFormsTestHarness.Record/Hooks/WindowTracker.cs`

1. コンストラクタで `SetWinEventHook` を設定（対象 PID のみ、EVENT_OBJECT_SHOW〜EVENT_OBJECT_DESTROY）
2. `BelongsToTarget(IntPtr hwnd)` メソッド実装:
   - 同一 PID チェック → 追跡済みウィンドウチェック → ルート所有元チェック → 子ウィンドウチェック
3. ウィンドウイベント（activated/closed）を `Channel<WindowEvent>` に投入
4. `IsModalDialog` 判定: 所有元ウィンドウが `IsWindowEnabled == false`
5. `IDisposable` で `UnhookWinEvent`

### Step B-5: グローバルフック実装

**作成ファイル**: `src/WinFormsTestHarness.Record/Hooks/MouseHook.cs`

1. `SetWindowsHookEx(WH_MOUSE_LL, callback, hMod, 0)` でグローバルフック設定
2. コールバック内の処理（**10μs 以内で完了**すること）:
   - `WindowTracker.BelongsToTarget(GetForegroundWindow())` で対象判定
   - 対象外なら即座に `CallNextHookEx` して return
   - 対象なら `Channel<MouseEvent>.Writer.TryWrite()` でキュー投入
   - 全体を try-catch で囲み、例外を絶対にスローしない
   - `CallNextHookEx` を必ず呼んで return
3. `--no-mousemove` オプション対応: ドラッグ中でない Move は投入しない
4. `IDisposable` で `UnhookWindowsHookEx`

**作成ファイル**: `src/WinFormsTestHarness.Record/Hooks/KeyboardHook.cs`

1. `SetWindowsHookEx(WH_KEYBOARD_LL, callback, hMod, 0)` でグローバルフック設定
2. コールバック処理は MouseHook と同パターン
3. 仮想キーコード→キー名変換テーブル
4. 印字可能キーの `Char` フィールド生成（ToUnicode API）
5. 修飾キー（Shift/Ctrl/Alt/Win）の `Modifier` フラグ

### Step B-6: 座標変換

**作成ファイル**: `src/WinFormsTestHarness.Record/Hooks/CoordinateConverter.cs`

1. `ToWindowRelative(screenX, screenY, hwnd)` — スクリーン座標→ウィンドウ相対座標
2. `GetMonitorInfo(screenX, screenY)` — DPI、モニタインデックス取得
3. DPI Aware プロセスなので `GetWindowRect` は物理ピクセルを返す

### Step B-7: フック生存監視

**作成ファイル**: `src/WinFormsTestHarness.Record/Hooks/HookHealthMonitor.cs`

1. 3秒間隔の監視タイマー
2. `RecordActivity()` — コールバック到着時に `Interlocked.Exchange` で最終活動時刻を更新
3. `Check()` メソッド:
   - フックハンドルが `IntPtr.Zero` でないか確認
   - `GetLastInputInfo` で OS 全体の最終入力時刻を取得
   - 5秒以上入力なし → `AliveIdle`（正常）
   - 入力あり かつ 2秒以上コールバック未着 → 自己テストパルス送信
   - パルス後 150ms 待機 → まだ未着なら `PossiblyDead`
4. `PossiblyDead` 時: `UnhookWindowsHookEx` → `SetWindowsHookEx` で再設定（最大3回リトライ）
5. 復旧/失敗時に `SystemEvent` を出力

### Step B-8: アプリ健全性監視

**作成ファイル**: `src/WinFormsTestHarness.Record/Hooks/AppHealthMonitor.cs`

1. 3秒間隔で `IsHungAppWindow` / `SendMessageTimeout(WM_NULL)` で対象アプリの応答を確認
2. ハング検知時: `SystemEvent(app_hung)` を出力、入力イベントに `app_hung: true` フラグ付与
3. 復帰時: `SystemEvent(app_responsive)` を出力

### Step B-9: 有界キューとライタースレッド

**作成ファイル**: `src/WinFormsTestHarness.Record/RecordingSession.cs`

1. `BoundedChannel<InputEvent>(capacity: 4096)` を作成
2. ライタースレッド: `channel.Reader.ReadAllAsync()` でデキュー → `NdJsonWriter` で stdout 出力
3. 劣化モード:
   - キュー使用率 80% 以上: Move/Wheel イベントを間引き（N個を1個に圧縮）
   - キュー使用率 95% 以上: Move/Wheel を優先破棄、Down/Up/Key/Window を保持
   - 破棄発生時: `SystemEvent(queue_overflow)` を出力
4. `Console.Out.AutoFlush = true` を設定（行バッファモード）

### Step B-10: セッションライフサイクル

**対象ファイル**: `src/WinFormsTestHarness.Record/RecordingSession.cs`（続き）

1. 開始フロー:
   - `--process` / `--hwnd`: 対象ウィンドウ検索・取得
   - `--launch`: `Process.Start()` → メインウィンドウ出現待機（30秒タイムアウト）
   - `SessionEvent(start)` を出力（プロセス名, PID, hwnd, コマンドライン, モニタ構成）
   - WindowTracker / MouseHook / KeyboardHook / HookHealthMonitor / AppHealthMonitor を起動
2. 停止フロー:
   - `Console.CancelKeyPress` で Ctrl+C を捕捉
   - 対象プロセス終了を `process.Exited` イベントで検知
   - フック解除 → 残りキューのドレイン → `SessionEvent(stop)` を出力（理由, 継続時間）
3. クリーンアップ保証:
   - `AppDomain.CurrentDomain.UnhandledException` でフック解除
   - フッククラスに `CriticalFinalizerObject` を検討

### Step B-11: CLI エントリーポイント

**対象ファイル**: `src/WinFormsTestHarness.Record/Program.cs`

1. `System.CommandLine` でルートコマンド定義:
   - `--process <name>` / `--hwnd <handle>` / `--launch <path>`: 対象指定（いずれか1つ必須）
   - `--launch-args <args>`: launch 時の引数
   - `--out <path>`: 出力ファイル（デフォルト: stdout）
   - `--filter <type>`: mouse|keyboard|all（デフォルト: all）
   - `--no-mousemove`: 非ドラッグ Move を除外
   - `--debug` / `--quiet`: 共通診断フラグ（Common の CommonOptions を使用）
2. メッセージポンプ: `Application.Run()` でメッセージループ維持（低レベルフックに必須）
3. 終了コード: `ExitCodes` を使用

### Step B-12: テスト

**作成ファイル**: `tests/WinFormsTestHarness.Tests/Record/` 配下

1. `WindowTrackerTests.cs`:
   - `BelongsToTarget` のロジックテスト（モック or SampleApp を利用）
   - モーダルダイアログ判定テスト
2. `CoordinateConverterTests.cs`:
   - ウィンドウ相対座標計算の正確性
3. `RecordingSessionTests.cs`:
   - キュー劣化モードの動作確認（手動でキューを充填）
   - セッションマーカーの出力形式検証
4. `EventModelTests.cs`:
   - 各イベント DTO の JSON シリアライズ/デシリアライズ
   - NDJSON フォーマット準拠の確認
5. **統合テスト**: SampleApp を `--launch` で起動し、手動操作の記録 → NDJSON 出力を検証

### デモ検証

```bash
# SampleApp を起動して記録
wfth-record --launch samples/SampleApp/bin/E2ETest/net8.0-windows/SampleApp.exe \
  --out demo/record-demo.ndjson

# 出力の確認
cat demo/record-demo.ndjson | jq '.type' | sort | uniq -c
```

---

## MVP C: wfth-capture（スクリーンショットキャプチャ）

**ゴール**: ウィンドウのスクリーンショット撮影ライブラリと CLI ラッパーを実装し、wfth-record に `--capture` オプションとして統合する。

### 前提条件
- MVP B（wfth-record）の基盤が実装済み

### Step C-1: Capture ライブラリのプロジェクト変更

**対象ファイル**: `src/WinFormsTestHarness.Capture/WinFormsTestHarness.Capture.csproj`

1. OutputType を削除して classlib に変更:
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <TargetFramework>net8.0-windows</TargetFramework>
       <UseWindowsForms>true</UseWindowsForms>
     </PropertyGroup>
   </Project>
   ```
2. 既存の `Program.cs` を削除

### Step C-2: コア撮影クラス

**作成ファイル**: `src/WinFormsTestHarness.Capture/ScreenCapturer.cs`

1. コンストラクタ: `IntPtr hwnd`, `CaptureOptions options`
2. `Capture(string? triggeredBy)` メソッド:
   - `NativeMethods.GetWindowRect(hwnd)` でウィンドウ領域取得
   - `Graphics.CopyFromScreen()` でスクリーン領域をキャプチャ
   - ウィンドウが最小化されている場合は `PrintWindow` API にフォールバック
   - `CaptureResult` を返す
3. `GetWindowRect()` メソッド: 現在のウィンドウ領域を返す
4. `IDisposable` 実装

**作成ファイル**: `src/WinFormsTestHarness.Capture/CaptureOptions.cs`

- `Quality`: CaptureQuality enum (Low/Medium/High/Full)
- `MaxWidth`: int (0=制限なし)
- `Format`: ImageFormat (Png/Jpeg)
- `JpegQuality`: int (1-100, デフォルト70)

**作成ファイル**: `src/WinFormsTestHarness.Capture/CaptureResult.cs`

- Timestamp, TriggeredBy, FilePath, Width, Height, FileSize, DiffRatio, Skipped, ReuseFrom, Bitmap

### Step C-3: 差分検知

**作成ファイル**: `src/WinFormsTestHarness.Capture/DiffDetector.cs`

1. `HasChanged(Bitmap current)` — 変化有無を bool で返す
2. `CalculateDiffRatio(Bitmap current)` — 差分率を 0.0〜1.0 で返す
3. アルゴリズム:
   - 元画像を 64x48 にリサイズ（高速比較用サムネイル）
   - 各ピクセルのグレースケール輝度を比較
   - 差異ピクセル数 / 全ピクセル数 = 差分率
   - 差分率 > Threshold (デフォルト 0.02) → 変化あり
4. 最適化: サムネイルのハッシュ値を保持し、完全一致なら即座にスキップ

### Step C-4: ファイル保存

**作成ファイル**: `src/WinFormsTestHarness.Capture/CaptureFileWriter.cs`

1. コンストラクタ: `string outputDir`
2. `Save(CaptureResult result, string suffix)` — 連番ファイル名（`0001_before.png` 等）で保存
3. `Interlocked.Increment` でスレッドセーフな連番管理
4. Quality に応じたエンコーダー選択（PNG/JPEG）

### Step C-5: 撮影戦略（before/after 制御）

**作成ファイル**: `src/WinFormsTestHarness.Capture/CaptureStrategy.cs`

1. `CaptureBeforeInput(string eventDescription)`:
   - Level < BeforeAfter なら null 返却
   - 直前の after と差分なしならスキップ（`ReuseFrom` 設定）
2. `CaptureAfterInputAsync(string eventDescription, int delayMs = 300)`:
   - Level < AfterOnly なら null 返却
   - `Task.Delay(delayMs)` で UI 反応待ち
   - 差分なしならスキップ

**作成ファイル**: `src/WinFormsTestHarness.Capture/CaptureLevel.cs`

- `None = 0`, `AfterOnly = 1`, `BeforeAfter = 2`, `All = 3`

### Step C-6: wfth-record への --capture 統合

**対象ファイル**: `src/WinFormsTestHarness.Record/WinFormsTestHarness.Record.csproj`

1. `WinFormsTestHarness.Capture` への ProjectReference を追加

**対象ファイル**: `src/WinFormsTestHarness.Record/Program.cs`

2. CLI オプション追加:
   - `--capture`: スクリーンショット有効化
   - `--capture-level <n>`: 0|1|2|3（デフォルト: 1）
   - `--capture-quality <q>`: low|medium|high|full（デフォルト: medium）
   - `--capture-dir <dir>`: 保存ディレクトリ（デフォルト: ./screenshots）
   - `--capture-delay <ms>`: after 撮影待機（デフォルト: 300）
   - `--diff-threshold <pct>`: 差分検知閾値（デフォルト: 2）

**対象ファイル**: `src/WinFormsTestHarness.Record/RecordingSession.cs`

3. ライタースレッドにスクリーンショット連動を追加:
   - MouseDown 検知 → `CaptureStrategy.CaptureBeforeInput()` → screenshot NDJSON 行出力
   - MouseUp 検知 → マウスイベント出力 → `CaptureStrategy.CaptureAfterInputAsync()` → screenshot NDJSON 行出力
   - screenshot NDJSON 形式: `{"ts":"...","type":"screenshot","timing":"before|after","file":"...","w":...,"h":...,"size":...,"diff":...}`

### Step C-7: wfth-capture CLI ラッパー

**対象ファイル**: `src/WinFormsTestHarness.Capture.Cli/WinFormsTestHarness.Capture.Cli.csproj`

1. csproj 確認:
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <OutputType>Exe</OutputType>
       <TargetFramework>net8.0-windows</TargetFramework>
       <PackAsTool>true</PackAsTool>
       <ToolCommandName>wfth-capture</ToolCommandName>
     </PropertyGroup>
     <ItemGroup>
       <ProjectReference Include="..\WinFormsTestHarness.Capture\WinFormsTestHarness.Capture.csproj" />
       <ProjectReference Include="..\WinFormsTestHarness.Common\WinFormsTestHarness.Common.csproj" />
       <PackageReference Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />
     </ItemGroup>
   </Project>
   ```

**対象ファイル**: `src/WinFormsTestHarness.Capture.Cli/Program.cs`

2. CLI オプション実装:
   - Target: `--process <name>` / `--hwnd <handle>`
   - Mode: `--once` / `--interval <ms>`
   - Capture: `--quality`, `--diff-threshold`, `--no-diff`
   - Output: `--out-dir`, `--out`
3. `--once` モード: 1枚撮影 → NDJSON メタデータ出力 → 終了
4. `--interval` モード: 定期ループ → 差分検知 → 変化時のみ撮影・出力

### Step C-8: テスト

**作成ファイル**: `tests/WinFormsTestHarness.Tests/Capture/` 配下

1. `DiffDetectorTests.cs`:
   - 同一画像 → 差分率 0.0, HasChanged = false
   - 異なる画像 → 差分率 > 0, HasChanged = true
   - 閾値境界のテスト
2. `CaptureFileWriterTests.cs`:
   - 連番ファイル名の生成規則
   - suffix (before/after/periodic) の正確性
3. `CaptureStrategyTests.cs`:
   - Level=None → 常に null
   - Level=AfterOnly → before は null, after は撮影
   - Level=BeforeAfter → 両方撮影
   - 差分なし → スキップ（ReuseFrom 設定）
4. `ScreenCapturerTests.cs`:
   - SampleApp のウィンドウを撮影 → Bitmap が非 null、サイズ > 0

### デモ検証

```bash
# 単発撮影
wfth-capture --process SampleApp --once --out-dir ./demo/screenshots

# 定期撮影（変化時のみ）
wfth-capture --process SampleApp --interval 1000 > demo/captures.ndjson

# record + capture 統合
SESSION=demo/session-$(date +%Y%m%d-%H%M%S)
mkdir -p $SESSION/screenshots
wfth-record --process SampleApp --capture --capture-dir $SESSION/screenshots > $SESSION/record.ndjson
```

---

## Logger: WinFormsTestHarness.Logger（アプリ内ロガー）

**ゴール**: WinFormsアプリに組み込む NuGet パッケージ形式のインストルメンテーション・ロガー。`#if E2E_TEST` で制御し、IPC 経由で Recording Engine にイベントを送信する。

### 前提条件
- Common ライブラリが実装済み（✅）
- MVP B の IPC サーバー側は Logger と並行して設計可能

### Step L-1: ビルド構成変更

**対象ファイル**: `Directory.Build.props`（リポジトリルート）

1. `E2ETestEnabled` MSBuild プロパティの追加:
   ```xml
   <PropertyGroup Condition="'$(E2ETestEnabled)' == 'true'">
     <DefineConstants>$(DefineConstants);E2E_TEST</DefineConstants>
   </PropertyGroup>
   ```
2. 既存の `E2ETest` 構成定義を確認・保持

**対象ファイル**: `src/WinFormsTestHarness.Logger/WinFormsTestHarness.Logger.csproj`

3. csproj 更新:
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <TargetFramework>net8.0-windows</TargetFramework>
       <UseWindowsForms>true</UseWindowsForms>
     </PropertyGroup>
   </Project>
   ```

### Step L-2: 基盤クラス

**作成ファイル**: `src/WinFormsTestHarness.Logger/Internal/PreciseTimestamp.cs`

1. `Stopwatch.GetTimestamp()` ベースの高精度タイムスタンプ
2. `NowIso8601` プロパティ: `yyyy-MM-ddTHH:mm:ss.ffffffZ` 形式
3. Common の PreciseTimestamp と同一アルゴリズム（Logger は Common に依存しない独立パッケージのため複製）

**作成ファイル**: `src/WinFormsTestHarness.Logger/LoggerConfig.cs`

1. 全プロパティをデフォルト値付きで定義:
   - `RecordingEnginePid` (int?), `PipeConnectTimeoutMs` (3000), `MaxQueueSize` (10000)
   - `FlushIntervalMs` (100), `FallbackFilePath` (null → %TEMP% 自動生成)
   - `MaxFallbackFileSizeBytes` (50MB), `ReconnectIntervalMs` (5000), `MaxReconnectAttempts` (10)
   - `AutoWatchControls` (true), `TrackForms` (true), `MaxControlDepth` (20)
2. `static LoggerConfig Default => new()` プロパティ

### Step L-3: ログエントリモデル

**作成ファイル**: `src/WinFormsTestHarness.Logger/Models/LogEntry.cs`

1. `sealed class LogEntry`: 全 JSON フィールドを `[JsonPropertyName]` 付きで定義
   - ts, type, control, event, prop, old, new, form, owner, modal, result, masked, row, text, message
2. ファクトリメソッド群:
   - `EventEntry(ControlInfo info, string eventName)` — type="event"
   - `PropertyChanged(ControlInfo info, string prop, object? old, object? @new, bool masked)` — type="prop"
   - `FormOpen(string formName, string? ownerName, bool modal)` — type="form_open"
   - `FormClose(string formName, string? dialogResult)` — type="form_close"
   - `Custom(string message)` — type="custom"
3. `Sanitize(object? value)` — Delegate/Type の安全変換、500文字トランケート
4. `MaskValue(object? value)` — `***` マスク

**作成ファイル**: `src/WinFormsTestHarness.Logger/Models/ControlInfo.cs`

1. イミュータブルクラス: `Name`, `ControlTypeName`, `FormName`, `IsPasswordField`
2. UIスレッドで1回だけ生成、以降はスナップショットとして使用

### Step L-4: トランスポート層（Sink）

**作成ファイル**: `src/WinFormsTestHarness.Logger/Sinks/ILogSink.cs`

```csharp
internal interface ILogSink
{
    void Write(LogEntry entry);
    bool IsConnected { get; }
}
```

**作成ファイル**: `src/WinFormsTestHarness.Logger/Sinks/JsonFileLogSink.cs`

1. ファイルパス: デフォルト `%TEMP%/WinFormsTestHarness/logs/applog_{datetime}_{pid}.ndjson`
2. `Write(LogEntry entry)`: lock 内で JSON シリアライズ → StreamWriter.WriteLine
3. ファイルローテーション: `_currentFileSize >= _maxFileSize` → リネーム → 新規ファイル
4. `AutoFlush = true`
5. `IDisposable` 実装

**作成ファイル**: `src/WinFormsTestHarness.Logger/Sinks/IpcLogSink.cs`

1. パイプ名解決: `LoggerConfig.RecordingEnginePid` → 環境変数 `WFTH_RECORDER_PID` → IPC 無効
2. パイプ名形式: `WinFormsTestHarness_{pid}_{sessionNonce}`（sessionNonce は環境変数 `WFTH_SESSION_NONCE` から取得）
3. 接続: `NamedPipeClientStream.Connect(timeout)`
4. ハンドシェイク:
   - サーバーからの `sync_request` 受信
   - `sync_response` を返送（clientTs 付き）
   - `sync_ack` 受信（clockOffset 情報）
5. 書き込み: `JsonSerializer.Serialize(entry)` → `StreamWriter.WriteLine(json)`
6. 切断検知: `IOException` → `_connected = false`
7. 再接続: 5秒間隔、最大10回。`TryReconnectIfDue()` を FlushQueue 前に呼び出し
8. `IDisposable` 実装

### Step L-5: パイプライン（バックグラウンドキュー）

**作成ファイル**: `src/WinFormsTestHarness.Logger/Internal/LogPipeline.cs`

1. `ConcurrentQueue<LogEntry>` + `System.Threading.Timer`（100ms周期）
2. `Enqueue(LogEntry entry)`:
   - `ConcurrentQueue.Enqueue(entry)` — lock-free, O(1)
   - キュー溢れ時（`_queueCount >= _maxQueueSize`）: `TryDequeue` で古いエントリを破棄
3. `FlushQueue()` — Timer コールバック（ThreadPool）:
   - `while (TryDequeue(out var entry))` → `WriteTo(entry)`
4. `WriteTo(LogEntry entry)`:
   - try: `_primarySink.Write(entry)` (IpcLogSink)
   - catch: `_fallbackSink.Write(entry)` (JsonFileLogSink)
5. `Dispose()`: Timer 停止 → 残りキューのフラッシュ → Sink の Dispose

### Step L-6: コントロール自動監視

**作成ファイル**: `src/WinFormsTestHarness.Logger/Internal/PasswordDetector.cs`

1. `IsPasswordField(Control control)`: TextBox.PasswordChar / UseSystemPasswordChar 判定

**作成ファイル**: `src/WinFormsTestHarness.Logger/Internal/ControlWatcher.cs`

1. `WatchRecursive(Control parent, int depth)`: 再帰的にコントロールツリーを走査
2. `WatchControl(Control control)`: 型に応じたイベントハンドラ登録
   - 全コントロール: Click, GotFocus, VisibleChanged, EnabledChanged
   - TextBox: TextChanged, KeyDown(Enter/Tab)
   - ComboBox: SelectedIndexChanged
   - CheckBox/RadioButton: CheckedChanged
   - DataGridView: SelectionChanged, CellClick
   - ListBox: SelectedIndexChanged
   - NumericUpDown/DateTimePicker: ValueChanged
   - TabControl: SelectedIndexChanged
3. **重要**: イベントハンドラは匿名ラムダではなく `_eventHandlers` ディクショナリに保持（解除のため）
4. `parent.ControlAdded` → 動的に追加されるコントロールも監視
5. `parent.ControlRemoved` → ハンドラ解除（メモリリーク防止）
6. 名前なしコントロール: `_{TypeName}_{HashCode:X8}` 形式のフォールバック名

### Step L-7: フォーム追跡

**作成ファイル**: `src/WinFormsTestHarness.Logger/Internal/FormTracker.cs`

1. `Start()`: `Application.Idle` にハンドラ登録
2. `ScanOpenForms()` — Idle 発火時:
   - `Application.OpenForms` を列挙
   - 未追跡フォームを検出 → `LogEntry.FormOpen()` をエンキュー
   - `ControlWatcher.WatchRecursive(form)` で全コントロールを監視
   - `form.FormClosed` にハンドラ登録 → `LogEntry.FormClose()` をエンキュー
3. `Stop()`: `Application.Idle` からアンフック
4. `IDisposable` 実装

### Step L-8: エントリーポイント

**作成ファイル**: `src/WinFormsTestHarness.Logger/TestLogger.cs`

1. 全メソッド本体を `#if E2E_TEST` ... `#endif` で囲む
2. `Attach(LoggerConfig? config)`:
   - 2回目以降は no-op
   - IpcLogSink + JsonFileLogSink → LogPipeline → FormTracker + ControlWatcher 初期化
   - 全体を try-catch（No-Throw 保証）
3. `LogEvent(string controlName, string eventName, object? value)` — 手動記録
4. `LogPropertyChanged(string controlName, string propertyName, object? oldValue, object? newValue)` — 手動プロパティ記録
5. `Log(string message)` — カスタムメッセージ
6. `Detach()`: FormTracker.Dispose → LogPipeline.Dispose → フラグリセット

### Step L-9: SampleApp 統合

**対象ファイル**: `samples/SampleApp/SampleApp.csproj`

1. Logger への ProjectReference を追加

**対象ファイル**: `samples/SampleApp/Program.cs`

2. `#if E2E_TEST` ブロックで `TestLogger.Attach()` 呼び出しを追加

### Step L-10: テスト

**作成ファイル**: `tests/WinFormsTestHarness.Tests/Logger/` 配下

1. `LogEntryTests.cs`:
   - ファクトリメソッドの JSON 出力形式検証
   - Sanitize のエッジケース（Delegate, 長文字列, null）
   - MaskValue の動作
2. `LogPipelineTests.cs`:
   - Enqueue → FlushQueue → Sink.Write が呼ばれる
   - キュー溢れ時の古いエントリ破棄
   - Primary Sink 失敗時の Fallback Sink 動作
3. `ControlInfoTests.cs`:
   - 名前なしコントロールのフォールバック名生成
   - PasswordDetector の判定
4. `JsonFileLogSinkTests.cs`:
   - ファイル出力の NDJSON フォーマット
   - ファイルローテーション
5. **検証方法**: `dotnet build -c E2ETest` → SampleApp 起動 → 操作 → `%TEMP%/WinFormsTestHarness/logs/` にNDJSON 出力確認

### 検証チェックリスト

- [ ] `dotnet build -c E2ETest` — Logger 有効ビルド成功
- [ ] `dotnet build -c Release` — Logger メソッド本体が空（ビルド成功）
- [ ] `dotnet build -c Release -p:E2ETestEnabled=true` — Release 最適化 + Logger 有効
- [ ] SampleApp E2ETest ビルドで操作 → ローカルファイルに NDJSON 出力
- [ ] パスワードフィールドの値がマスクされている

---

## MVP D-1: wfth-aggregate（生イベント集約）

**ゴール**: wfth-record の生イベント（MouseDown+Up, キー列）を操作アクション（Click, TextInput, DragAndDrop等）に集約変換する。

### 前提条件
- Common ライブラリが実装済み（✅）
- MVP B の出力 NDJSON 形式が確定済み

### Step D1-1: プロジェクト設定

**対象ファイル**: `src/WinFormsTestHarness.Aggregate/WinFormsTestHarness.Aggregate.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>wfth-aggregate</ToolCommandName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\WinFormsTestHarness.Common\WinFormsTestHarness.Common.csproj" />
    <PackageReference Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />
  </ItemGroup>
</Project>
```

### Step D1-2: 集約済みアクション モデル

**作成ファイル**: `src/WinFormsTestHarness.Aggregate/Models/AggregatedAction.cs`

1. `type` フィールドの値:
   - `Click` — LeftDown+Up (300ms以内)
   - `DoubleClick` — 2つの Click (500ms以内)
   - `RightClick` — RightDown+Up
   - `DragAndDrop` — LeftDown + Move(drag) + LeftUp
   - `TextInput` — 連続キー入力（text-timeout で区切り）
   - `SpecialKey` — Enter, Tab, Escape, Delete, Backspace, F1-F12 等
   - `WheelScroll` — ホイール操作
   - `screenshot` — スクリーンショットイベント（パススルー）
   - `session` — セッションマーカー（パススルー）
   - `system` — システムイベント（パススルー）
   - `window` — ウィンドウイベント（パススルー）

2. 出力例:
   ```json
   {"ts":"...","type":"Click","button":"Left","sx":450,"sy":320,"rx":230,"ry":180}
   {"ts":"...","type":"TextInput","text":"田中","startTs":"...","endTs":"...","sx":450,"sy":320,"rx":230,"ry":180}
   {"ts":"...","type":"SpecialKey","key":"Enter","sx":450,"sy":320,"rx":230,"ry":180}
   {"ts":"...","type":"DragAndDrop","startSx":100,"startSy":200,"endSx":300,"endSy":400}
   ```

### Step D1-3: マウスクリック集約

**作成ファイル**: `src/WinFormsTestHarness.Aggregate/Aggregation/MouseClickAggregator.cs`

1. 状態マシン:
   - `Idle` → LeftDown 受信 → `PendingUp`（座標・タイムスタンプを保持）
   - `PendingUp` → LeftUp 受信（Δ < click-timeout）→ `Click` アクション出力
   - `PendingUp` → Move(drag) 受信 → `Dragging` 状態へ遷移
   - `PendingUp` → タイムアウト → Down 単体イベントとして出力
   - `Dragging` → LeftUp 受信 → `DragAndDrop` アクション出力
2. DoubleClick 検出: 直前の Click から 500ms 以内に次の Click → `DoubleClick` に置換
3. RightClick: RightDown+RightUp → 即座に `RightClick`
4. **座標**: Down 時の座標をアクションの座標とする

### Step D1-4: キーシーケンス集約

**作成ファイル**: `src/WinFormsTestHarness.Aggregate/Aggregation/KeySequenceAggregator.cs`

1. 状態マシン:
   - 印字可能キーの down → バッファに追加、タイマーリセット
   - text-timeout (500ms) 経過で無入力 → バッファを `TextInput` アクションとして出力
   - 特殊キー（Enter, Tab, Escape, Delete, Backspace, F1-F12）→ バッファフラッシュ + `SpecialKey` アクション出力
   - 修飾キー（Shift, Ctrl, Alt）→ 集約に含めない（状態追跡のみ）
2. `char` フィールドを連結して `text` を構成
3. `startTs` は最初のキー、`endTs` は最後のキー

### Step D1-5: アクションビルダー

**作成ファイル**: `src/WinFormsTestHarness.Aggregate/Aggregation/ActionBuilder.cs`

1. `MouseClickAggregator` と `KeySequenceAggregator` を統合
2. stdin から NDJSON を1行ずつ読み込み:
   - `type == "mouse"` → MouseClickAggregator に渡す
   - `type == "key"` → KeySequenceAggregator に渡す
   - `type == "screenshot" | "session" | "system" | "window"` → パススルー出力
3. 集約済みアクションを stdout に NDJSON 出力
4. EOF 到達時: 両 Aggregator のバッファをフラッシュ

### Step D1-6: CLI エントリーポイント

**対象ファイル**: `src/WinFormsTestHarness.Aggregate/Program.cs`

1. System.CommandLine でオプション定義:
   - `--text-timeout <ms>` (デフォルト: 500)
   - `--click-timeout <ms>` (デフォルト: 300)
   - `--dblclick-timeout <ms>` (デフォルト: 500)
   - `--debug` / `--quiet`
2. stdin → ActionBuilder → stdout のパイプライン実行
3. `Console.Out.AutoFlush = true`

### Step D1-7: テスト

**作成ファイル**: `tests/WinFormsTestHarness.Tests/Aggregate/` 配下

1. `MouseClickAggregatorTests.cs`:
   - LeftDown + LeftUp (100ms) → Click
   - LeftDown + LeftUp (400ms, > timeout) → 個別イベント
   - LeftDown + Move(drag) + LeftUp → DragAndDrop
   - 2x Click (300ms間隔) → DoubleClick
   - RightDown + RightUp → RightClick
2. `KeySequenceAggregatorTests.cs`:
   - "abc" 入力 (各50ms間隔) + 500ms無入力 → TextInput { text: "abc" }
   - "ab" + Enter → TextInput { text: "ab" } + SpecialKey { key: "Enter" }
   - Shift+T → TextInput { text: "T" }（修飾キーは含めない）
3. `ActionBuilderTests.cs`:
   - 手作り NDJSON 入力 → 期待される集約済み NDJSON 出力の一致検証
   - screenshot / session / system イベントのパススルー確認

### デモ検証

```bash
# デモデータで動作確認
wfth-aggregate < demo/record.ndjson > demo/aggregated.ndjson

# 型別カウント
cat demo/aggregated.ndjson | jq '.type' | sort | uniq -c

# jq との連携
wfth-aggregate < demo/record.ndjson | jq 'select(.type == "Click")'
```

---

## MVP D-2: wfth-correlate（時間窓相関）

**ゴール**: 集約済みアクションに UIA 変化・スクリーンショット・アプリ内ログを時間窓で紐付け、統合ログ（session.ndjson）を生成する。

### 前提条件
- MVP D-1 (wfth-aggregate) が実装済み
- MVP A (wfth-inspect watch) の UIA NDJSON 出力形式が確定済み

### Step D2-1: プロジェクト設定

**対象ファイル**: `src/WinFormsTestHarness.Correlate/WinFormsTestHarness.Correlate.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>wfth-correlate</ToolCommandName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\WinFormsTestHarness.Common\WinFormsTestHarness.Common.csproj" />
    <PackageReference Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />
  </ItemGroup>
</Project>
```

### Step D2-2: 補助データ読み込み

**作成ファイル**: `src/WinFormsTestHarness.Correlate/Readers/UiaEventReader.cs`

1. `--uia <path>` で指定された NDJSON ファイルを全行読み込み
2. タイムスタンプでソート済みの `List<UiaChangeEvent>` を返す
3. wfth-inspect watch の出力形式をパース

**作成ファイル**: `src/WinFormsTestHarness.Correlate/Readers/AppLogReader.cs`

1. `--app-log <path>` で指定された NDJSON ファイルを全行読み込み
2. タイムスタンプでソート済みの `List<AppLogEntry>` を返す
3. Logger の出力形式（type: event/prop/form_open/form_close/custom）をパース

**作成ファイル**: `src/WinFormsTestHarness.Correlate/Readers/ScreenshotIndex.cs`

1. `--screenshots <dir>` で指定されたディレクトリの PNG ファイル一覧を取得
2. ファイル名パターン（`0001_before.png`, `0001_after.png`）からメタデータを構築
3. stdin の screenshot NDJSON 行からもメタデータを収集
4. タイムスタンプ → ファイルパスの逆引きインデックスを構築

### Step D2-3: UIA ターゲット解決

**作成ファイル**: `src/WinFormsTestHarness.Correlate/Correlation/UiaTargetResolver.cs`

1. クリック座標（rx, ry）から、時間窓内の UIA スナップショットで要素を逆引き
2. BoundingRectangle に座標が含まれる要素を特定
3. 複数候補がある場合は最小面積の要素を選択（最も具体的な要素）
4. 結果: `{ source: "UIA", automationId, name, controlType, rect }`
5. UIA で見つからない場合: `{ source: "coordinate", description: "..." }`

### Step D2-4: 時間窓相関エンジン

**作成ファイル**: `src/WinFormsTestHarness.Correlate/Correlation/TimeWindowCorrelator.cs`

1. stdin から集約済みアクションを1行ずつ読み込み
2. 各アクションに対して:
   - **UIA 変化**: アクション時刻の -50ms 〜 +window ms の UIA 変化イベントを紐付け
   - **スクリーンショット**: 最も近い before/after を紐付け
   - **アプリ内ログ**: 時間窓内のログを因果関係ヒューリスティクスで紐付け（AutomationId 一致優先）
   - **ターゲット要素**: UiaTargetResolver で特定
3. 紐付け結果を `CorrelatedAction` として出力

### Step D2-5: ノイズ分類

**作成ファイル**: `src/WinFormsTestHarness.Correlate/Correlation/NoiseClassifier.cs`

1. 各アクションのノイズ判定:
   - `empty_click`: Click だが UIA 変化なし + アプリログなし
   - `window_move`: ウィンドウバー/枠へのクリック
   - `duplicate_click`: 直前の Click と同一座標・同一ターゲット（500ms以内）
   - `accidental_drag`: 移動距離が極小（< 5px）の DragAndDrop
2. confidence 値 (0.0〜1.0) を計算
3. `--noise-threshold` (デフォルト 0.7) 以上でノイズ判定
4. `--include-noise` でノイズ判定された操作も出力（デフォルトは除外）

### Step D2-6: 統合ログ出力

**作成ファイル**: `src/WinFormsTestHarness.Correlate/Models/CorrelatedAction.cs`

1. 出力フィールド: seq, ts, type, input, target, screenshots, uiaDiff, appLog, noise, _explain
2. NDJSON 形式で1行1アクションを stdout に出力
3. `--explain` モード時: `_explain` フィールドに判定根拠を追記
4. 末尾に summary メタ行を出力:
   ```json
   {"type":"summary","summaryType":"correlation","metrics":{"totalActions":25,"withAppLog":20,"withoutAppLog":5}}
   {"type":"summary","summaryType":"coverage","metrics":{"totalActions":25,"uiaResolved":22,"uiaFallback":3}}
   ```

### Step D2-7: SystemGap 処理

1. stdin の `type: "system"` イベント（hook_lost, queue_overflow 等）を検知
2. gap 区間を `SystemGap` アクションとして統合ログに挿入:
   ```json
   {"seq":5,"ts":"...","type":"SystemGap","input":{"reason":"hook_lost","duration":5.2},"note":"この期間の入力イベントは記録されていない可能性がある"}
   ```

### Step D2-8: CLI エントリーポイント

**対象ファイル**: `src/WinFormsTestHarness.Correlate/Program.cs`

1. System.CommandLine でオプション定義:
   - `--uia <path>`: UIA 変化 NDJSON
   - `--app-log <path>`: アプリ内ロガー NDJSON
   - `--screenshots <dir>`: スクリーンショットディレクトリ
   - `--window <ms>`: 相関時間窓（デフォルト: 2000）
   - `--include-noise`: ノイズも出力
   - `--noise-threshold <n>`: ノイズ判定閾値（デフォルト: 0.7）
   - `--explain`: 判定根拠を注釈
   - `--debug` / `--quiet`
2. `Console.Out.AutoFlush = true`

### Step D2-9: テスト

**作成ファイル**: `tests/WinFormsTestHarness.Tests/Correlate/` 配下

1. `UiaTargetResolverTests.cs`:
   - 座標 → UIA 要素の逆引き精度
   - 複数候補時の最小面積選択
2. `TimeWindowCorrelatorTests.cs`:
   - 手作りの aggregated NDJSON + UIA NDJSON → 期待される相関結果
   - 時間窓外のイベントは紐付かないこと
   - スクリーンショットの before/after 紐付け
3. `NoiseClassifierTests.cs`:
   - empty_click の判定
   - duplicate_click の判定
   - threshold 境界のテスト
4. `AppLogCorrelatorTests.cs`:
   - AutomationId 一致による因果関係突合
   - 時間窓内の最近接イベント選択
5. **統合テスト**: demo データを使ったパイプライン全体の動作確認

### デモ検証（フルパイプライン）

```bash
SESSION=demo/session-test
mkdir -p $SESSION/screenshots

# 記録（並列実行）
wfth-record --process SampleApp --capture --capture-dir $SESSION/screenshots > $SESSION/record.ndjson &
PID_RECORD=$!
wfth-inspect watch --process SampleApp > $SESSION/uia.ndjson &
PID_INSPECT=$!

# 手動テスト実施 → Ctrl+C で停止
kill $PID_RECORD $PID_INSPECT
wait

# 統合ログ生成
wfth-aggregate < $SESSION/record.ndjson \
  | wfth-correlate --uia $SESSION/uia.ndjson \
                   --screenshots $SESSION/screenshots \
                   --explain \
  > $SESSION/session.ndjson

# 結果確認
cat $SESSION/session.ndjson | jq 'select(.type != "summary")' | head -20
cat $SESSION/session.ndjson | jq 'select(.type == "summary")'
```

---

## MVP E: WinFormsTestHarness.Core（テスト実行フレームワーク）

**ゴール**: AI が生成するテストコードから UI 操作の実装詳細を隠蔽するテスト実行フレームワーク。ドライバー層（HybridElementLocator, UIAutomationDriver）と操作抽象化層（FormPage, IElement）を提供する。

### 前提条件
- MVP C（WinFormsTestHarness.Capture）のコアライブラリが実装済み
- FlaUI.UIA3 パッケージが利用可能

### Step E-1: プロジェクト設定

**対象ファイル**: `src/WinFormsTestHarness.Core/WinFormsTestHarness.Core.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="FlaUI.UIA3" Version="4.*" />
    <ProjectReference Include="..\WinFormsTestHarness.Capture\WinFormsTestHarness.Capture.csproj" />
  </ItemGroup>
</Project>
```

### Step E-2: 例外クラス定義

**作成ファイル**: `src/WinFormsTestHarness.Core/Exceptions/` 配下

1. `WinFormsTestHarnessException.cs` — 基底例外
2. `ElementNotFoundException.cs` — 要素未発見（Failures リスト + ScreenshotPath 付き）
3. `ActionFailedException.cs` — 操作失敗
4. `AssertionException.cs` — 検証失敗

### Step E-3: ストラテジー定義

**作成ファイル**: `src/WinFormsTestHarness.Core/Abstraction/ElementStrategy.cs`

1. `StrategyKind` enum: ByAutomationId, ByName, ByControlType, ByClassName, ByPath, ByImage, ByPattern
2. `ElementStrategy` クラス: Kind, Value, ControlType, Description, ReferenceImage, PreferredTimeout

**作成ファイル**: `src/WinFormsTestHarness.Core/Abstraction/Strategy.cs`

1. ファクトリメソッド: `ByAutomationId(id)`, `ByName(name, controlType?)`, `ByControlType(type)`, `ByClassName(name)`, `ByPath(path)`, `ByImage(imagePath)`, `ByPattern(regex)`

### Step E-4: ドライバーインターフェース

**作成ファイル**: `src/WinFormsTestHarness.Core/Driver/IElementDriver.cs`

```csharp
public interface IElementDriver
{
    Task<FoundElement?> FindAsync(ElementStrategy strategy, TimeSpan timeout);
    IReadOnlySet<StrategyKind> SupportedStrategies { get; }
}
```

**作成ファイル**: `src/WinFormsTestHarness.Core/Driver/FoundElement.cs`

- AutomationId, Name, ControlType, BoundingRect, LocatedBy, UiaElement (internal), ImageCenter (internal)

### Step E-5: UIAutomationDriver

**作成ファイル**: `src/WinFormsTestHarness.Core/Driver/UIAutomationDriver.cs`

1. コンストラクタ: `IntPtr hwnd` → `UIA3Automation` + `FromHandle` でルート要素取得
2. SupportedStrategies: ByAutomationId, ByName, ByControlType, ByClassName, ByPath
3. `FindAsync`: ストラテジーに応じた検索 + ポーリング（200ms間隔, timeout まで）
   - `FindByAutomationId`: `ConditionFactory.ByAutomationId` → `FindFirstDescendant`
   - `FindByName`: `ConditionFactory.ByName` + オプション `ByControlType` 絞り込み
   - `FindByControlType`, `FindByClassName`, `FindByPath` も同パターン
4. `ToFoundElement`: AutomationElement → FoundElement 変換
5. `DumpTreeJson(IntPtr hwnd)`: デバッグ用 UIA ツリーダンプ（失敗レポート用）
6. `IDisposable`: UIA3Automation の Dispose

### Step E-6: 画像認識ドライバー（スタブ）

**作成ファイル**: `src/WinFormsTestHarness.Core/Driver/IImageMatcher.cs`

```csharp
public interface IImageMatcher
{
    ImageMatchResult? FindTemplate(Bitmap screenshot, Bitmap template, double threshold = 0.85);
    ImageMatchResult? FindByOcr(Bitmap screenshot, string pattern);
}
```

**作成ファイル**: `src/WinFormsTestHarness.Core/Driver/NullImageMatcher.cs`

- 常に null を返すスタブ実装

**作成ファイル**: `src/WinFormsTestHarness.Core/Driver/ImageRecognitionDriver.cs`

- ScreenCapturer + IImageMatcher を使用
- SupportedStrategies: ByImage, ByPattern
- NullImageMatcher 使用時は常に null 返却（エラーにはならない）

### Step E-7: HybridElementLocator

**作成ファイル**: `src/WinFormsTestHarness.Core/Driver/HybridElementLocator.cs`

1. コンストラクタ: `IReadOnlyList<IElementDriver> drivers`, `ScreenCapturer?`, `TimeSpan? defaultTimeout`
2. `FindAsync(ElementStrategy[] strategies, TimeSpan? timeout)`:
   - 各ストラテジーを優先順位順に試行
   - 対応ドライバーがなければスキップ（FailureList に記録）
   - 最初に見つかった要素を返す
   - 全ストラテジー失敗時: 失敗時スクリーンショット撮影 → `ElementNotFoundException` をスロー
3. タイムアウト配分: 全体タイムアウト / ストラテジー数（前のストラテジーが早期完了すれば後に繰り越し）

### Step E-8: ActionExecutor

**作成ファイル**: `src/WinFormsTestHarness.Core/Driver/ActionExecutor.cs`

1. `ClickAsync(FoundElement)`:
   - UIA: InvokePattern → TogglePattern → マウスクリックフォールバック
2. `SetTextAsync(FoundElement, string text)`:
   - UIA: ValuePattern → フォーカス+キーボード入力フォールバック
3. `GetText(FoundElement)`:
   - UIA: ValuePattern.Value → Name プロパティフォールバック
4. `SelectAsync(FoundElement, string itemText)`:
   - UIA: ExpandCollapsePattern → 子要素検索 → SelectionItemPattern
5. `SetCheckedAsync(FoundElement, bool isChecked)`:
   - UIA: TogglePattern の状態確認→必要に応じて Toggle

**作成ファイル**: `src/WinFormsTestHarness.Core/Infrastructure/MouseHelper.cs`

1. `ClickAsync(Point, MouseButton)`: SetCursorPos → mouse_event (Down+Up)
2. `DoubleClickAsync(Point)`: 2回クリック
3. `DragAsync(Point from, Point to)`: Down → 段階的 Move → Up

**作成ファイル**: `src/WinFormsTestHarness.Core/Infrastructure/NativeMethods.cs`

- SetCursorPos, mouse_event, SendInput 等の P/Invoke 定義

### Step E-9: IElement / Element

**作成ファイル**: `src/WinFormsTestHarness.Core/Abstraction/IElement.cs`

- ClickAsync, DoubleClickAsync, RightClickAsync, SetTextAsync, GetTextAsync, SelectAsync, SetCheckedAsync, GetCheckedAsync, IsEnabledAsync, IsVisibleAsync, Should(), WaitUntilExistsAsync, ResolveAsync

**作成ファイル**: `src/WinFormsTestHarness.Core/Abstraction/Element.cs`

1. 遅延バインド + キャッシュ付き実装
2. `ResolveAsync()`: キャッシュ有効チェック → HybridElementLocator.FindAsync
3. 状態変更操作（Click, SetText, Select, SetChecked）後はキャッシュ無効化
4. `IsStillValid(FoundElement)`: UIA 要素の生存チェック

### Step E-10: FormPage

**作成ファイル**: `src/WinFormsTestHarness.Core/Abstraction/FormPage.cs`

1. `abstract class FormPage`: HybridElementLocator + ActionExecutor を保持
2. `protected IElement Element(params ElementStrategy[] strategies)`: ストラテジーチェーンで要素定義
3. `protected IElement Element(TimeSpan timeout, params ElementStrategy[] strategies)`: タイムアウト付き
4. `virtual Task WaitForLoadAsync(TimeSpan? timeout)`: サブクラスでオーバーライド

### Step E-11: Assertions

**作成ファイル**: `src/WinFormsTestHarness.Core/Assertions/IElementAssertions.cs`

- HaveTextAsync, ContainTextAsync, MatchTextAsync, ExistAsync, NotExistAsync, BeEnabledAsync, BeDisabledAsync, BeVisibleAsync, BeHiddenAsync, BeCheckedAsync, BeUncheckedAsync

**作成ファイル**: `src/WinFormsTestHarness.Core/Assertions/ElementAssertions.cs`

- 各メソッドの実装（条件不一致時に `AssertionException` をスロー）

### Step E-12: AppInstance

**作成ファイル**: `src/WinFormsTestHarness.Core/App/LaunchConfig.cs`

- ExePath, Arguments, WorkingDirectory, AttachLogger, ScreenshotOnFailure, ScreenshotDirectory, DefaultTimeout, LaunchTimeout, ImageMatcher, BuildConfiguration

**作成ファイル**: `src/WinFormsTestHarness.Core/App/AppInstance.cs`

1. `static LaunchAsync(LaunchConfig)`: プロセス起動 → メインウィンドウ待機 → ドライバー初期化
2. `GetFormAsync<T>()`: Page Object インスタンス生成 + WaitForLoadAsync
3. `WaitForFormAsync<T>(TimeSpan?)`: フォーム出現をポーリング待機
4. `CaptureFailureReportAsync()`: スクリーンショット + UIA ツリーダンプ
5. `CloseAsync()`: CloseMainWindow → WaitForExit → Kill
6. `IAsyncDisposable` 実装

**作成ファイル**: `src/WinFormsTestHarness.Core/App/FailureReport.cs`

- Timestamp, ScreenshotPath, UiaTreeJson, AppLogEntries

### Step E-13: テストベースクラス

**作成ファイル**: `src/WinFormsTestHarness.Core/Testing/WinFormsTestBase.cs`

1. NUnit の `[OneTimeSetUp]` / `[TearDown]` / `[OneTimeTearDown]` を使用
2. `OneTimeSetUp`: `AppInstance.LaunchAsync(CreateLaunchConfig())`
3. `TearDown`: テスト失敗時に `CaptureFailureReportAsync` + `TestContext.AddTestAttachment`
4. `OneTimeTearDown`: `App.DisposeAsync()`
5. `abstract LaunchConfig CreateLaunchConfig()`: サブクラスが実装

### Step E-14: 設定

**作成ファイル**: `src/WinFormsTestHarness.Core/Infrastructure/TestHarnessConfig.cs`

1. DefaultTimeout, ImplicitWait, ScreenshotDir, ReferenceImageDir
2. CI 判定: 環境変数 `CI=true`
3. `CITimeoutMultiplier`: 環境変数 `E2E_TIMEOUT_MULTIPLIER`（デフォルト 2.0）
4. `EffectiveTimeout`: CI 環境では倍率適用

### Step E-15: SampleApp 用 Page Object + テスト

**作成ファイル**: `tests/WinFormsTestHarness.Tests/Core/Pages/` 配下

1. `MainFormPage.cs`: SampleApp メインフォームの Page Object
2. `SearchFormPage.cs`: 検索フォームの Page Object
3. `CustomerEditFormPage.cs`: 顧客編集フォームの Page Object

**作成ファイル**: `tests/WinFormsTestHarness.Tests/Core/` 配下

4. `HybridElementLocatorTests.cs`:
   - フォールバック動作: 第1ストラテジー失敗 → 第2ストラテジー成功
   - 全ストラテジー失敗 → ElementNotFoundException（詳細メッセージ付き）
5. `UIAutomationDriverTests.cs`:
   - SampleApp の要素を AutomationId / Name / ControlType で検索
6. `ActionExecutorTests.cs`:
   - Click, SetText, GetText の基本動作
7. `ElementTests.cs`:
   - 遅延バインドの動作
   - キャッシュ無効化の動作
8. `AppInstanceTests.cs`:
   - SampleApp の起動・終了
   - メインウィンドウの取得

**作成ファイル**: `tests/WinFormsTestHarness.Tests/Core/E2E/` 配下

9. `CustomerSearchE2ETest.cs`:
   ```csharp
   [TestFixture]
   public class CustomerSearchE2ETest : WinFormsTestBase
   {
       protected override LaunchConfig CreateLaunchConfig() => new()
       {
           ExePath = TestConfig.SampleAppPath,
       };

       [Test]
       public async Task 顧客検索_田中で検索_結果が表示される()
       {
           var main = await App.GetFormAsync<MainFormPage>();
           await main.CustomerSearchButton.ClickAsync();

           var search = await App.WaitForFormAsync<SearchFormPage>();
           await search.SearchCondition.SetTextAsync("田中");
           await search.SearchButton.ClickAsync();

           await search.ResultGrid.Should().ExistAsync();
       }
   }
   ```

### デモ検証

```bash
# ビルド
dotnet build -c E2ETest

# E2E テスト実行
dotnet test tests/WinFormsTestHarness.Tests/ -c E2ETest --filter "FullyQualifiedName~Core.E2E"
```

---

## 各 MVP の完了定義

| MVP | 完了条件 |
|-----|---------|
| **B** (wfth-record) | SampleApp への操作が正しい NDJSON で記録される。Ctrl+C で正常停止する。モーダルダイアログへの入力も記録される |
| **C** (wfth-capture) | スクリーンショットが PNG で保存される。差分検知で無変化時スキップが動作する。`wfth-record --capture` で入力+スクリーンショットの統合 NDJSON が出力される |
| **Logger** | SampleApp E2ETest ビルドでクリック・テキスト入力・フォーム遷移がローカルファイルに NDJSON 記録される。Release ビルドでロガーが除去される |
| **D-1** (wfth-aggregate) | `wfth-record` の生 NDJSON を stdin から読み、Click/TextInput/DragAndDrop/SpecialKey の集約済みアクションを stdout に出力する |
| **D-2** (wfth-correlate) | aggregate の出力 + UIA NDJSON を紐付けた session.ndjson が生成される。`--explain` で判定根拠が表示される |
| **E** (Core) | SampleApp に対する E2E テストが NUnit で実行でき、Page Object パターンで画面操作と検証ができる |
