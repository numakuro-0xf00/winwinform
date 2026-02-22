# Recording Session ワークフロー・Controller UI 設計

## 1. 概要

Recording セッションのライフサイクル制御と、手動テスター向け操作 UI の設計。

**設計判断サマリ:**

| 項目 | 決定 | 理由 |
|------|------|------|
| 一時停止方式 | ソフトポーズ（フック維持） | フック再設定リスク回避、性能影響無視できる |
| 複数ツール連動 | ラッパーCLI（wfth-session） | テスターがパイプラインを意識しなくて済む |
| 制御 IPC | 名前付きパイプ | アプリ内ロガー IPC と設計一貫性あり、拡張性高い |
| 後処理 | 自動実行（`--no-postprocess` で無効化可） | テスターの手間を最小化 |
| Controller UI | トレイアイコン + ホットキー | 対象アプリの操作を妨げない、非エンジニアにも使いやすい |
| ツール構成 | wfth-session に UI を統合 | ツール数を抑制、単一起動で完結 |

---

## 2. セッションライフサイクル

### 2.1 状態遷移

```
[Idle] ──(Start)──→ [Recording] ──(Pause)──→ [Paused]
                         │                       │
                      (Stop)              (Resume)→ [Recording]
                         │                       │
                         ▼                    (Stop)
                     [Stopped]          ──→  [Stopped]
                         │
                    [PostProcessing]
                         │
                      [Done]
```

### 2.2 各状態の詳細

| 状態 | フック | イベント記録 | トレイアイコン | 説明 |
|------|--------|-------------|----------------|------|
| Idle | なし | — | グレー ○ | 起動直後、記録開始前 |
| Recording | アクティブ | 通常記録 | 赤 ● | 入力イベントを記録中 |
| Paused | アクティブ（維持） | `paused:true` タグ付き | 黄 ● | 一時停止中、フックは維持 |
| Stopped | 解除済み | — | グレー ○ | 記録終了、後処理待ち |
| PostProcessing | — | — | グレー（回転） | aggregate + correlate 実行中 |
| Done | — | — | グレー ○ | セッション完了 |

### 2.3 トリガー

| トリガー | アクション | 結果 |
|---------|----------|------|
| Alt+1 or メニュー「記録開始」 | Start | Idle → Recording |
| Alt+2 or メニュー「一時停止」 | Pause | Recording → Paused |
| Alt+2 or メニュー「再開」 | Resume | Paused → Recording |
| Alt+3 or メニュー「停止」 | Stop | Recording/Paused → Stopped |
| 対象プロセス終了 | Auto Stop | Recording/Paused → Stopped |
| Ctrl+C（コンソール） | Graceful Stop | Recording/Paused → Stopped |

---

## 3. wfth-session CLI 設計

### 3.1 CLI インターフェース

```
wfth-session [options]

Target（いずれか1つ必須）:
  --process <name>       プロセス名で対象指定（部分一致、大文字小文字無視）
  --hwnd <handle>        ウィンドウハンドル（0xHHHH形式）
  --launch <path>        実行ファイルを起動してから記録開始

Launch:
  --launch-args <args>   --launch 時のコマンドライン引数

Recording:
  --filter <type>        mouse|keyboard|all（デフォルト: all）
  --no-mousemove         MouseMoveイベントを除外

Capture:
  --capture              スクリーンショット撮影を有効化（デフォルト: true）
  --no-capture           スクリーンショット撮影を無効化
  --capture-level <n>    撮影レベル 0|1|2|3（デフォルト: 1）
  --capture-quality <q>  low|medium|high|full（デフォルト: medium）

Session:
  --session-dir <dir>    セッション保存ディレクトリ（デフォルト: 自動生成）
  --session-name <name>  セッション名（デフォルト: rec-YYYYMMDD-HHMMSS）
  --sessions-root <dir>  セッションルートディレクトリ（デフォルト: ./sessions）

Post-processing:
  --no-postprocess       記録停止後の自動後処理を無効化
  --correlate-window <ms> 相関時間窓（デフォルト: 2000）

UI:
  --no-tray              トレイアイコンを非表示（CLIのみ）
  --hotkey-start <key>   記録開始ホットキー（デフォルト: F5）
  --hotkey-pause <key>   一時停止/再開ホットキー（デフォルト: F9）
  --hotkey-stop <key>    停止ホットキー（デフォルト: F10）

Global:
  --debug                診断情報を stderr に出力
  --quiet                stderr 出力を抑制
```

### 3.2 動作フロー

```
$ wfth-session --process SampleApp --capture

[Phase 1: 初期化]
  1. 対象ウィンドウを検索・取得
  2. セッションディレクトリ作成: sessions/rec-20260222-143000/
  3. トレイアイコン表示（--no-tray でない場合）
  4. ホットキー登録（RegisterHotKey）
  5. コンソール出力: "Ready. Press F5 to start recording, or right-click tray icon."

[Phase 2: 記録待ち (Idle)]
  ← F5 押下 or メニュー「記録開始」

[Phase 3: 記録中 (Recording)]
  1. 制御パイプ作成
  2. wfth-record 子プロセス起動:
     wfth-record --process SampleApp --capture --capture-level 1 \
                 --capture-dir sessions/rec-.../screenshots \
                 --out sessions/rec-.../record.ndjson \
                 --control-pipe WinFormsTestHarness_Control_{session_id}
  3. wfth-inspect watch 子プロセス起動:
     wfth-inspect watch --process SampleApp \
                 --out sessions/rec-.../uia.ndjson
  4. トレイアイコン: 赤 ●
  5. コンソール: "Recording started (SampleApp). F9=Pause, F10=Stop"
  6. 経過時間をツールチップに表示

  ← F9 押下 → Phase 4
  ← F10 押下 → Phase 5
  ← 対象プロセス終了 → Phase 5

[Phase 4: 一時停止 (Paused)]
  1. 制御パイプ経由: {"cmd":"pause"}
  2. wfth-record: session/pause マーカー出力、以降のイベントに paused:true
  3. トレイアイコン: 黄 ●
  4. コンソール: "Paused. F9=Resume, F10=Stop"

  ← F9 押下 → Phase 3（Resume）
  ← F10 押下 → Phase 5

[Phase 5: 停止 (Stopped)]
  1. 制御パイプ経由: {"cmd":"stop"}
  2. wfth-record: session/stop マーカー出力 → 終了
  3. wfth-inspect: SIGTERM → 終了
  4. 子プロセス終了を待機（タイムアウト 5秒、強制kill）
  5. トレイアイコン: グレー ○
  6. コンソール: "Recording stopped."

[Phase 6: 後処理 (PostProcessing)]
  1. コンソール: "Running post-processing..."
  2. パイプライン実行:
     wfth-aggregate < record.ndjson \
       | wfth-correlate --uia uia.ndjson \
                        --screenshots screenshots/ \
       > session.ndjson
  3. コンソール: "Session saved: sessions/rec-20260222-143000/"
  4. コンソール: "  Actions: 25, Duration: 2m 35s"

[Phase 7: 完了 (Done)]
  ← F5 押下 → 新しいセッションで Phase 2 へ
  ← メニュー「終了」→ アプリケーション終了
```

### 3.3 セッションディレクトリの自動生成

```
sessions/
└── rec-20260222-143000/          ← wfth-session が自動作成
    ├── record.ndjson             ← wfth-record 出力
    ├── uia.ndjson                ← wfth-inspect watch 出力
    ├── screenshots/              ← wfth-record --capture 出力
    │   ├── 0001_before.png
    │   ├── 0001_after.png
    │   └── ...
    └── session.ndjson            ← 後処理で生成
```

命名規則:
- デフォルト: `rec-YYYYMMDD-HHMMSS`（ローカルタイムゾーン）
- `--session-name` 指定時: 指定名をそのまま使用
- 衝突時: `rec-20260222-143000-2` のように連番サフィックス追加

---

## 4. 制御パイプ IPC

### 4.1 パイプ名

```
WinFormsTestHarness_Control_{session_id}

session_id: wfth-session が生成するランダムID（8文字hex）
  例: WinFormsTestHarness_Control_a3f7b2c1
```

### 4.2 プロトコル

方向: **双方向**。改行区切りの JSON。

- **コマンド** (wfth-session → wfth-record): 制御指示
- **応答** (wfth-record → wfth-session): コマンド受理/エラー報告

```json
// コマンド（wfth-session → wfth-record）
{"cmd":"pause","id":"c1","ts":"2026-02-22T14:31:00.000Z"}
{"cmd":"resume","id":"c2","ts":"2026-02-22T14:31:30.000Z"}
{"cmd":"stop","id":"c3","ts":"2026-02-22T14:32:05.000Z"}

// 応答（wfth-record → wfth-session）
{"ack":"c1","ok":true,"ts":"2026-02-22T14:31:00.005Z"}
{"ack":"c2","ok":true,"ts":"2026-02-22T14:31:30.003Z"}
{"ack":"c3","ok":false,"error":"flush_timeout","ts":"2026-02-22T14:32:05.010Z"}
```

フィールド:
- `id`: コマンド識別子（wfth-session が採番、応答の `ack` と対応）
- `ok`: コマンドが正常に処理されたか
- `error`: 失敗理由（`ok:false` の場合のみ）

### 4.3 応答タイムアウト

wfth-session は応答を **3秒** 待つ。タイムアウト時は**楽観的に状態遷移する**（テスターが操作不能にならないことを優先）。

```
pause/resume タイムアウト:
  1. stderr に警告: "Warning: wfth-record did not acknowledge pause within 3s"
  2. バルーン通知（Warning）: "記録エンジンからの応答がありません。記録データに影響がある可能性があります"
  3. 楽観的に状態遷移する（Paused / Recording に変更）
  4. wfth-record の実出力（session/pause マーカーの有無）が最終的な正となる
     → wfth-record が実際には pause していなかった場合、後段の wfth-aggregate が
        paused:true のないイベントをそのまま通すため、記録データは欠損しない
     → 状態ズレが起きても「記録データの安全性」は担保される

stop タイムアウト:
  1. stderr に警告: "Warning: wfth-record did not acknowledge stop within 3s"
  2. バルーン通知（Warning）: "記録エンジンからの応答がありません。停止処理を続行します"
  3. 状態を Stopped に遷移
  4. 子プロセスの WaitForExit タイムアウト（5秒）に委ねる
  5. それでも終了しなければ強制 kill（既存の ChildProcessManager の挙動）

ack が ok:false で返ってきた場合:
  1. stderr にエラー内容: "Warning: command rejected: {error}"
  2. バルーン通知（Warning）: "記録エンジンがコマンドを拒否しました: {error}"
  3. 状態遷移は行う（テスターの操作を妨げない）
```

**設計根拠**: テスターの操作を妨げず、かつ異常の発生を見落とさせない。バルーン通知は数秒で自動的に消えるため操作の邪魔にならないが、テスターに「この記録セッションは信頼性が低い可能性がある」ことを伝えられる。仮に状態がズレても、wfth-record の NDJSON 出力（session/pause・session/resume マーカー）が真の記録状態を反映するため、後処理の正確性には影響しない。

### 4.4 wfth-record 側の対応

wfth-record に `--control-pipe <name>` オプションを追加。

```csharp
class ControlPipeListener : IDisposable
{
    private readonly NamedPipeClientStream _pipeIn;
    private readonly NamedPipeServerStream _pipeOut;
    private StreamWriter? _writer;

    public event EventHandler? PauseRequested;
    public event EventHandler? ResumeRequested;
    public event EventHandler? StopRequested;

    public ControlPipeListener(string pipeName)
    {
        // コマンド受信用（wfth-session がサーバー）
        _pipeIn = new NamedPipeClientStream(".", pipeName,
            PipeDirection.In, PipeOptions.Asynchronous);
        // 応答送信用（wfth-record がサーバー）
        _pipeOut = new NamedPipeServerStream(pipeName + "_ack",
            PipeDirection.Out, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }

    public async Task ListenAsync(CancellationToken ct)
    {
        await Task.WhenAll(
            _pipeIn.ConnectAsync(ct),
            _pipeOut.WaitForConnectionAsync(ct));
        _writer = new StreamWriter(_pipeOut) { AutoFlush = true };

        using var reader = new StreamReader(_pipeIn);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null) break;

            var cmd = JsonSerializer.Deserialize<ControlCommand>(line);
            var ok = true;
            string? error = null;

            try
            {
                switch (cmd?.Cmd)
                {
                    case "pause":  PauseRequested?.Invoke(this, EventArgs.Empty); break;
                    case "resume": ResumeRequested?.Invoke(this, EventArgs.Empty); break;
                    case "stop":   StopRequested?.Invoke(this, EventArgs.Empty); break;
                    default: ok = false; error = "unknown_command"; break;
                }
            }
            catch (Exception ex)
            {
                ok = false;
                error = ex.Message;
            }

            // 応答送信
            await SendAckAsync(cmd?.Id, ok, error);
        }
    }

    private async Task SendAckAsync(string? id, bool ok, string? error)
    {
        if (_writer == null || id == null) return;
        var ack = JsonSerializer.Serialize(new
        {
            ack = id, ok, error,
            ts = DateTime.UtcNow.ToString("O")
        });
        await _writer.WriteLineAsync(ack);
    }
}
```

### 4.4 制御パイプ未使用時

`--control-pipe` を指定しない場合、wfth-record は従来通り Ctrl+C で停止する。
wfth-session を使わない単体実行もこれまで通り可能（後方互換）。

---

## 5. ソフトポーズ

### 5.1 セマンティクス

一時停止中もフック（WH_MOUSE_LL / WH_KEYBOARD_LL）は維持する。イベントは受信するが `paused:true` フラグを付与して出力する。

```
理由:
  - フック再設定（UnhookWindowsHookEx → SetWindowsHookEx）はサイレント失敗のリスク
  - recording-reliability-design.md のフック生存監視はあるが、意図的な再設定は想定外
  - アイドルフックの性能影響は無視できる（CallNextHookEx が即座に返る）
  - wfth-aggregate が paused イベントを破棄するため、下流への影響もなし
```

### 5.2 NDJSON 出力（wfth-record）

#### セッションマーカー

```json
{"ts":"...","type":"session","action":"pause","reason":"user_request"}
{"ts":"...","type":"session","action":"resume","pauseDuration":30.0}
```

#### 一時停止中のイベント

```json
{"ts":"...","type":"mouse","action":"LeftDown","sx":450,"sy":320,"rx":230,"ry":180,"paused":true}
{"ts":"...","type":"key","action":"down","vk":84,"key":"T","scan":20,"char":"T","paused":true}
```

### 5.3 wfth-aggregate の処理

```csharp
// wfth-aggregate: paused イベントを破棄
foreach (var rawEvent in ReadNdJson(stdin))
{
    // session マーカーは常に透過（pause/resume 情報を下流に伝える）
    if (rawEvent.Type == "session")
    {
        WriteOutput(rawEvent);
        continue;
    }

    // paused イベントは破棄
    if (rawEvent.Paused == true)
        continue;

    // 通常の集約処理
    ProcessEvent(rawEvent);
}
```

### 5.4 一時停止中の wfth-inspect watch

wfth-inspect watch にはポーズの概念を持たせない（UIAツリーの変化は常に記録する）。ポーズ中の UIA 変化は wfth-correlate が session/pause〜resume の時間窓で除外する。

```
理由:
  - wfth-inspect は独立ツールとして単体でも使う
  - ポーズ制御を追加すると制御パイプの複雑性が増す
  - correlate 側で時間窓フィルタリングするほうが UNIX 的
```

### 5.5 一時停止中のスクリーンショット

wfth-record --capture は一時停止中の撮影をスキップする（入力イベントトリガーのため、paused 中はトリガーが発火しない）。

---

## 6. 後処理パイプライン

### 6.1 自動実行フロー

```csharp
class PostProcessor
{
    public async Task<PostProcessResult> RunAsync(
        string sessionDir, PostProcessOptions options, CancellationToken ct)
    {
        var recordPath = Path.Combine(sessionDir, "record.ndjson");
        var uiaPath = Path.Combine(sessionDir, "uia.ndjson");
        var screenshotsDir = Path.Combine(sessionDir, "screenshots");
        var outputPath = Path.Combine(sessionDir, "session.ndjson");

        // wfth-aggregate < record.ndjson | wfth-correlate ... > session.ndjson
        var aggregate = StartProcess("wfth-aggregate",
            stdin: File.OpenRead(recordPath));
        var correlate = StartProcess("wfth-correlate",
            $"--uia {uiaPath} --screenshots {screenshotsDir}" +
            $" --window {options.CorrelateWindow}",
            stdin: aggregate.StandardOutput,
            stdout: File.Create(outputPath));

        await Task.WhenAll(
            aggregate.WaitForExitAsync(ct),
            correlate.WaitForExitAsync(ct));

        return new PostProcessResult
        {
            OutputPath = outputPath,
            ActionCount = CountLines(outputPath),
            Duration = ParseSessionDuration(recordPath)
        };
    }
}
```

### 6.2 後処理の無効化

```bash
# 後処理をスキップ（手動でパイプラインを実行したい場合）
wfth-session --process SampleApp --no-postprocess

# 後処理だけ再実行（セッションディレクトリ指定）
wfth-session postprocess sessions/rec-20260222-143000/
```

`wfth-session postprocess` サブコマンドを提供し、任意のセッションディレクトリに対して後処理を再実行できる。

---

## 7. Controller UI（トレイアイコン）

### 7.1 トレイアイコン

```
状態別アイコン:
  ○ (gray)   — Idle / Stopped / Done
  ● (red)    — Recording
  ● (amber)  — Paused
  ○ (gray, spinning) — PostProcessing

ツールチップ:
  Idle:       "WinForms Test Harness — Ready"
  Recording:  "記録中 (02:35) — SampleApp"
  Paused:     "一時停止 — SampleApp"
  Processing: "後処理中..."
```

### 7.2 コンテキストメニュー

```
右クリックメニュー（状態別）:

[Idle]
  ┌──────────────────────┐
  │ ▶ 記録開始        F5 │
  ├──────────────────────┤
  │ ⚙ 設定...            │
  │ ✖ 終了               │
  └──────────────────────┘

[Recording]
  ┌──────────────────────┐
  │ ❙❙ 一時停止       F9 │
  │ ■  停止           F10 │
  ├──────────────────────┤
  │ 📁 セッション を開く  │
  └──────────────────────┘

[Paused]
  ┌──────────────────────┐
  │ ▶ 再開            F9 │
  │ ■  停止           F10 │
  ├──────────────────────┤
  │ 📁 セッション を開く  │
  └──────────────────────┘

[PostProcessing]
  ┌──────────────────────┐
  │ （後処理中...）       │
  └──────────────────────┘

[Done]
  ┌──────────────────────┐
  │ ▶ 新規記録        F5 │
  ├──────────────────────┤
  │ 📁 セッション を開く  │
  │ ⚙ 設定...            │
  │ ✖ 終了               │
  └──────────────────────┘
```

### 7.3 バルーン通知

```
状態変化時にバルーン通知を表示:

Recording Started:  "SampleApp の記録を開始しました"
Paused:             "記録を一時停止しました (F9 で再開)"
Resumed:            "記録を再開しました"
Stopped:            "記録を停止しました"
PostProcess Done:   "セッション保存完了: 25 アクション / 2分35秒"
Error:              "エラー: wfth-record が予期せず終了しました"
Ack Timeout:        "⚠ 記録エンジンからの応答がありません（クリックして確認）"
Ack Rejected:       "⚠ 記録エンジンがコマンドを拒否しました（クリックして確認）"
```

### 7.4 復帰確認ダイアログ

Ack タイムアウト / 拒否のバルーン通知をクリックすると復帰確認ダイアログを表示する。
また、異常発生中はトレイアイコンに警告バッジ（⚠）を重ねて表示し、コンテキストメニューにも「状態を確認...」項目を追加する。

```
┌────────── 記録エンジンの状態 ──────────┐
│                                         │
│ ⚠ 記録エンジンからの応答がありません。   │
│                                         │
│ 記録は継続していますが、一時停止の       │
│ 反映状況が不確定です。                   │
│                                         │
│   [記録を続行]  [停止して保存]  [再記録]  │
│                                         │
└─────────────────────────────────────────┘
```

| ボタン | アクション |
|--------|-----------|
| 記録を続行 | ダイアログを閉じ、警告バッジを解除。現在の状態で記録を継続する |
| 停止して保存 | 記録を停止し、記録済みデータで後処理を実行する |
| 再記録 | 記録を停止（後処理なし）し、新しいセッションで記録を再開する |

**動作フロー:**

```
バルーン通知表示
  → トレイアイコンに ⚠ バッジ追加
  → コンテキストメニューに「状態を確認...」追加
  │
  ├─ バルーンクリック or メニュー「状態を確認...」
  │    → 復帰確認ダイアログ表示
  │       ├─ [記録を続行]  → ダイアログ閉じる、⚠ バッジ解除
  │       ├─ [停止して保存] → StopAsync() → 後処理
  │       └─ [再記録]      → StopAsync(noPostProcess: true)
  │                          → StartRecordingAsync(config)
  │
  └─ ユーザーが無視（バルーンが自動で消える）
       → ⚠ バッジは残る（次のコマンドが正常応答したら自動解除）
       → テスターの操作はブロックされない
```

**⚠ バッジの自動解除:**

次のコマンドが正常に ack された場合（`ok:true`）、パイプ通信が正常に復帰したとみなし、⚠ バッジを自動的に解除する。テスターが明示的に確認しなくても、一時的な遅延であれば自然に解消される。

### 7.5 設定ダイアログ

最小限の設定ダイアログ:

```
┌─────────── 設定 ───────────┐
│                             │
│ 対象プロセス: [SampleApp  ] │
│                             │
│ ホットキー:                 │
│   開始:    [F5 ]            │
│   一時停止: [F9 ]           │
│   停止:    [F10]            │
│                             │
│ スクリーンショット:          │
│   [✓] 有効                  │
│   レベル: [1 - AfterOnly ▼] │
│                             │
│ セッション保存先:            │
│   [./sessions            ] │
│                             │
│     [OK]    [キャンセル]     │
└─────────────────────────────┘
```

---

## 8. ホットキー

### 8.1 グローバルホットキー登録

```csharp
class HotkeyManager : IDisposable
{
    // Win32 RegisterHotKey でグローバルホットキーを登録
    // 対象アプリがフォアグラウンドでもホットキーが機能する

    private const int WM_HOTKEY = 0x0312;
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 1;

    public void Register(Keys key, Action handler)
    {
        var id = _nextId++;
        NativeMethods.RegisterHotKey(_windowHandle, id,
            GetModifiers(key), GetVirtualKey(key));
        _handlers[id] = handler;
    }

    // WndProc で WM_HOTKEY を処理
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && _handlers.TryGetValue(
            (int)m.WParam, out var handler))
        {
            handler();
        }
        base.WndProc(ref m);
    }
}
```

### 8.2 ホットキーの衝突回避

```
問題:
  - F5/F9/F10 は他のアプリでも使われる（F5=リフレッシュ等）
  - 対象アプリ自身が F5 を使っている場合、記録に影響する

対策:
  - RegisterHotKey はグローバルフックより優先され、対象アプリには伝搬しない
  - 衝突が問題になる場合は --hotkey-* オプションで変更可能
  - 推奨: Ctrl+Shift+F5/F9/F10 など修飾キー付きに変更
  - RegisterHotKey 失敗時は stderr に警告出力、メニュー操作のみで動作
```

### 8.3 デフォルトホットキー

| 操作 | デフォルト | 変更オプション |
|------|----------|---------------|
| 記録開始 | F5 | `--hotkey-start` |
| 一時停止/再開 | F9 | `--hotkey-pause` |
| 停止 | F10 | `--hotkey-stop` |

---

## 9. wfth-session アーキテクチャ

### 9.1 プロジェクト構成

```
src/WinFormsTestHarness.Session/
  ├── Program.cs                    — System.CommandLine エントリーポイント
  ├── WinFormsTestHarness.Session.csproj
  ├── SessionOrchestrator.cs        — セッションライフサイクル管理
  ├── ChildProcessManager.cs        — 子プロセス起動・監視・停止
  ├── ControlPipeServer.cs          — 制御パイプサーバー
  ├── PostProcessor.cs              — 後処理パイプライン実行
  ├── UI/
  │   ├── TrayIconController.cs     — トレイアイコン管理
  │   ├── ContextMenuBuilder.cs     — 状態別コンテキストメニュー
  │   └── SettingsDialog.cs         — 設定ダイアログ
  ├── Hotkeys/
  │   ├── HotkeyManager.cs          — RegisterHotKey ラッパー
  │   └── NativeMethods.Hotkey.cs   — P/Invoke
  └── Models/
      ├── SessionState.cs           — 状態遷移管理
      ├── SessionConfig.cs          — セッション設定
      └── PostProcessResult.cs      — 後処理結果
```

### 9.2 csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>  <!-- NotifyIcon, ContextMenuStrip 用 -->
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>wfth-session</ToolCommandName>
    <ApplicationManifest>app.manifest</ApplicationManifest>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />
    <ProjectReference Include="..\WinFormsTestHarness.Common\WinFormsTestHarness.Common.csproj" />
  </ItemGroup>
</Project>
```

注: `OutputType=Exe` により CLI モード（`--no-tray`）では親ターミナルにそのままステータスを出力できる。トレイモード（デフォルト）では起動直後に `FreeConsole()` を呼び出してコンソールを切り離す。ターミナルから起動した場合は親ターミナルから切り離されるだけで自然な挙動となる。

### 9.3 セッションオーケストレーター

```csharp
class SessionOrchestrator
{
    private SessionState _state = SessionState.Idle;
    private readonly ChildProcessManager _children;
    private readonly ControlPipeServer _controlPipe;
    private readonly PostProcessor _postProcessor;

    public event EventHandler<SessionState>? StateChanged;

    public async Task StartRecordingAsync(SessionConfig config)
    {
        if (_state != SessionState.Idle && _state != SessionState.Done)
            throw new InvalidOperationException($"Cannot start from {_state}");

        // セッションディレクトリ作成
        var sessionDir = CreateSessionDirectory(config);

        // 制御パイプ作成
        var controlPipeName = $"WinFormsTestHarness_Control_{GenerateId()}";
        await _controlPipe.StartAsync(controlPipeName);

        // 子プロセス起動
        await _children.StartAsync(new[]
        {
            BuildRecordProcess(config, sessionDir, controlPipeName),
            BuildInspectProcess(config, sessionDir)
        });

        SetState(SessionState.Recording);
    }

    private static readonly TimeSpan AckTimeout = TimeSpan.FromSeconds(3);

    public async Task PauseAsync()
    {
        if (_state != SessionState.Recording)
            throw new InvalidOperationException($"Cannot pause from {_state}");

        var ack = await _controlPipe.SendAndWaitAckAsync(
            new { cmd = "pause" }, AckTimeout);
        if (!ack.Ok)
            Log.Warn($"Pause not acknowledged: {ack.Error}");

        // 楽観的に状態遷移（実出力が最終的な正）
        SetState(SessionState.Paused);
    }

    public async Task ResumeAsync()
    {
        if (_state != SessionState.Paused)
            throw new InvalidOperationException($"Cannot resume from {_state}");

        var ack = await _controlPipe.SendAndWaitAckAsync(
            new { cmd = "resume" }, AckTimeout);
        if (!ack.Ok)
            Log.Warn($"Resume not acknowledged: {ack.Error}");

        // 楽観的に状態遷移（実出力が最終的な正）
        SetState(SessionState.Recording);
    }

    public async Task StopAsync()
    {
        if (_state != SessionState.Recording && _state != SessionState.Paused)
            throw new InvalidOperationException($"Cannot stop from {_state}");

        var ack = await _controlPipe.SendAndWaitAckAsync(
            new { cmd = "stop" }, AckTimeout);
        if (!ack.Ok)
            Log.Warn($"Stop not acknowledged: {ack.Error}");

        // 応答の成否に関わらず子プロセス終了を待つ
        await _children.WaitForExitAsync(timeout: TimeSpan.FromSeconds(5));
        SetState(SessionState.Stopped);

        // 後処理
        if (!_config.NoPostProcess)
        {
            SetState(SessionState.PostProcessing);
            var result = await _postProcessor.RunAsync(_sessionDir, _config);
            OnPostProcessComplete(result);
        }

        SetState(SessionState.Done);
    }
}
```

### 9.4 子プロセス管理

```csharp
class ChildProcessManager : IDisposable
{
    private readonly List<Process> _processes = new();

    public async Task StartAsync(IEnumerable<ProcessStartInfo> startInfos)
    {
        foreach (var info in startInfos)
        {
            var process = Process.Start(info)
                ?? throw new InvalidOperationException($"Failed to start {info.FileName}");

            process.EnableRaisingEvents = true;
            process.Exited += OnChildExited;
            _processes.Add(process);
        }
    }

    public async Task WaitForExitAsync(TimeSpan timeout)
    {
        var cts = new CancellationTokenSource(timeout);
        var tasks = _processes.Select(p =>
            p.WaitForExitAsync(cts.Token)).ToArray();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // タイムアウト: 残存プロセスを強制終了
            foreach (var p in _processes.Where(p => !p.HasExited))
            {
                Log.Warn($"Force killing {p.ProcessName} (pid={p.Id})");
                p.Kill(entireProcessTree: true);
            }
        }
    }

    private void OnChildExited(object? sender, EventArgs e)
    {
        if (sender is Process p && p.ExitCode != 0)
        {
            ChildFailed?.Invoke(this, new ChildFailedEventArgs(p));
        }
    }

    public event EventHandler<ChildFailedEventArgs>? ChildFailed;
}
```

---

## 10. エラーハンドリング

### 10.1 子プロセスの予期せぬ終了

```
wfth-record が予期せず終了した場合:
  1. stderr にエラー出力
  2. バルーン通知: "エラー: wfth-record が予期せず終了しました"
  3. wfth-inspect も停止
  4. 記録済みデータは保持（不完全だが利用可能）
  5. 後処理を試行（失敗しても記録データは残る）

wfth-inspect が予期せず終了した場合:
  1. stderr に警告出力
  2. バルーン通知: "警告: UIAツリー監視が停止しました（記録は継続）"
  3. wfth-record は継続（UIA情報なしでも記録は有用）
  4. 再起動を試行（1回まで）
```

### 10.2 対象プロセスの終了

```
対象アプリが終了した場合:
  1. wfth-record が target_exited で自動停止
  2. wfth-session がそれを検知
  3. wfth-inspect も停止
  4. 後処理を実行
  5. バルーン通知: "対象アプリが終了したため、記録を停止しました"
```

### 10.3 ホットキー登録失敗

```
RegisterHotKey が失敗した場合（他アプリが占有）:
  1. stderr に警告: "Warning: Failed to register F9 hotkey (already in use)"
  2. バルーン通知: "ホットキー F9 の登録に失敗しました。メニューから操作してください"
  3. トレイアイコンのコンテキストメニューは常に利用可能
  4. 代替ホットキー（Ctrl+Shift+F9 等）の自動試行は行わない
     （ユーザーに --hotkey-pause オプションでの指定を促す）
```

---

## 11. 実装優先度

| 機能 | 優先度 | 理由 |
|------|--------|------|
| wfth-session CLI（--no-tray モード） | **MVP** | コアのオーケストレーション |
| 制御パイプ IPC | **MVP** | ポーズ/停止制御の基盤 |
| wfth-record --control-pipe 対応 | **MVP** | 制御パイプの受信側 |
| ソフトポーズ (paused フラグ) | **MVP** | 一時停止の中核 |
| 自動後処理 | **MVP** | テスター体験の基本 |
| セッションディレクトリ自動生成 | **MVP** | テスターが意識しなくてよい |
| トレイアイコン | 高 | テスター向け UI の核 |
| ホットキー（RegisterHotKey） | 高 | 操作性の要 |
| コンテキストメニュー | 高 | トレイアイコンと同時実装 |
| バルーン通知 | 中 | 状態変化のフィードバック |
| 設定ダイアログ | 中 | CLI引数で代替可能 |
| postprocess サブコマンド | 低 | 手動実行で代替可能 |

---

## 12. recording-cli-design.md からの差分

| 項目 | 旧設計 | 新設計 |
|------|--------|--------|
| ツール構成 | wfth-record + wfth-inspect を手動並列起動 | wfth-session が自動オーケストレーション |
| 一時停止 | 未設計 | ソフトポーズ（フック維持 + paused タグ） |
| 制御 IPC | なし | 制御用名前付きパイプ |
| 後処理 | 手動で aggregate \| correlate | wfth-session が自動実行 |
| テスター向け UI | なし | トレイアイコン + ホットキー |
| セッション管理 | ディレクトリ規約のみ | 自動生成 + オーケストレーション |
