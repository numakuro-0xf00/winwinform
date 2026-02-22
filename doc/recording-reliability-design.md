# Recording Engine 信頼性・安定性設計

## 1. グローバルフックのクラッシュリカバリ

### 1.1 問題

`SetWindowsHookEx` の低レベルフック（WH_MOUSE_LL / WH_KEYBOARD_LL）には以下のリスクがある:

- **サイレント自動解除**: コールバックが一定時間内（レジストリ `LowLevelHooksTimeout`、デフォルト約300ms）に `CallNextHookEx` を返さない場合、Windowsがフックを自動解除する。警告やエラーは出ない。
- **例外による解除**: コールバック内で未処理例外が発生するとフックが解除される。
- **プロセスクラッシュ**: wfth-record プロセス自体がクラッシュした場合、フックはOS側で自動クリーンアップされる（他プロセスへの影響はない）。

### 1.2 対策設計

#### A. コールバックの高速化（予防）

フックコールバック内では最小限の処理のみ行い、イベントデータをキューに投入して即座に返す。

```
[Hook Callback Thread]              [Writer Thread]
  │                                    │
  ├─ イベント受信                      │
  ├─ IsTargetWindow() 判定（高速）     │
  ├─ ConcurrentQueue.Enqueue()        │
  ├─ CallNextHookEx() return ←────── │ 10μs以内
  │                                    ├─ Dequeue()
  │                                    ├─ JSON シリアライズ
  │                                    └─ stdout 書き出し
```

設計原則:
- コールバック内で I/O（ファイル書き込み、Console出力）を行わない
- コールバック内で例外を発生させない（全体を try-catch で囲む）
- コールバック内でロックを取得しない
- `ConcurrentQueue<T>` でロックフリーにイベントを受け渡す

#### B. フック生存監視（検知）

定期的にフックが有効かどうかを確認する。低レベルフックにはフック状態を直接問い合わせるAPIがないため、間接的に検知する。

```
方式: 自己テストイベント

[Watchdog Thread — 3秒間隔]
  1. 現在のフックハンドルが IntPtr.Zero でないことを確認
  2. 最後にフックコールバックが呼ばれた時刻を確認
  3. 対象ウィンドウがフォアグラウンドなのに5秒以上コールバック未着
     → フック消失と判断
  4. → フック再設定を試行
```

```csharp
class HookHealthMonitor
{
    private volatile long _lastCallbackTick;   // Interlocked で更新
    private readonly IntPtr _targetHwnd;

    // コールバック内で呼ぶ（軽量）
    public void RecordActivity()
    {
        Interlocked.Exchange(ref _lastCallbackTick,
            Environment.TickCount64);
    }

    // Watchdog スレッドで呼ぶ
    public HookStatus Check()
    {
        var elapsed = Environment.TickCount64 -
            Interlocked.Read(ref _lastCallbackTick);

        // 対象ウィンドウがフォアグラウンドかどうか
        var isForeground = NativeMethods.GetForegroundWindow() == _targetHwnd
            || IsOwnedByTarget(NativeMethods.GetForegroundWindow());

        if (isForeground && elapsed > 5000)
            return HookStatus.PossiblyDead;

        return HookStatus.Alive;
    }
}
```

#### C. フック再設定（復旧）

```
復旧フロー:
  1. 既存フックハンドルで UnhookWindowsHookEx()（念のため）
  2. SetWindowsHookEx() で再設定
  3. 再設定成功 → 警告ログを stderr に出力、NDJSON に復旧イベント記録
  4. 再設定失敗（3回リトライ） → エラー出力して終了

  復旧イベント:
  {"ts":"...","type":"system","action":"hook_recovered","elapsed":5.2,"retries":1}
```

#### D. プロセスクラッシュガード

```
対策:
  - AppDomain.UnhandledException ハンドラでフック解除を保証
  - Console.CancelKeyPress で Ctrl+C 時のクリーンアップ
  - CriticalFinalizerObject を継承したフッククラスで
    GC時のファイナライザでも解除を試行
```

### 1.3 イベント欠損への対応

フック消失〜復旧の間はイベント欠損が発生する。これを統合ログで明示する。

```json
{"ts":"...","type":"system","action":"hook_lost","hookType":"mouse"}
{"ts":"...","type":"system","action":"hook_recovered","hookType":"mouse","gap":5.2}
```

wfth-correlate はこの gap を検知し、統合ログに以下を挿入:

```json
{
  "seq": 5,
  "ts": "...",
  "type": "SystemGap",
  "input": {"reason":"hook_lost","duration":5.2},
  "note": "この期間の入力イベントは記録されていない可能性がある"
}
```

---

## 2. 対象アプリのハング検知

### 2.1 問題

対象WinFormsアプリがハング（応答なし）になった場合:
- UIAツリーの走査がブロックまたはタイムアウトする
- フック自体は機能し続けるが、アプリが入力を処理しない
- スクリーンショットは撮影可能だがUIが更新されない

### 2.2 ハング検知方式

```csharp
class AppHealthMonitor
{
    private readonly IntPtr _targetHwnd;
    private const int TimeoutMs = 3000;

    public AppStatus Check()
    {
        // IsHungAppWindow: ウィンドウが5秒以上メッセージに
        // 応答しない場合 true を返す
        if (NativeMethods.IsHungAppWindow(_targetHwnd))
            return AppStatus.Hung;

        // SendMessageTimeout: より細かいタイムアウト制御
        var result = NativeMethods.SendMessageTimeout(
            _targetHwnd,
            0x0000,       // WM_NULL（副作用なし）
            IntPtr.Zero,
            IntPtr.Zero,
            SendMessageTimeoutFlags.SMTO_ABORTIFHUNG,
            TimeoutMs,
            out _);

        return result != IntPtr.Zero
            ? AppStatus.Responsive
            : AppStatus.Hung;
    }
}
```

### 2.3 ハング時の動作

```
検知間隔: 3秒ごとに AppHealthMonitor.Check()

[通常] → [ハング検知]
              │
              ├─ stderr に警告出力
              ├─ NDJSON にハングイベント記録
              │    {"ts":"...","type":"system","action":"app_hung"}
              ├─ 入力イベントの記録は継続（フックは動作し続ける）
              ├─ ただし入力イベントに "app_hung":true フラグを付与
              │
              ├─ [3秒ごとに再チェック]
              │
              └─ [応答復帰]
                    ├─ 復帰イベント記録
                    │    {"ts":"...","type":"system","action":"app_responsive","hungDuration":12.5}
                    └─ 通常記録に復帰
```

### 2.4 対象アプリ終了の検知

```csharp
// プロセス終了の非同期監視
process.EnableRaisingEvents = true;
process.Exited += (s, e) =>
{
    // session/stop マーカー出力
    writer.Write(new SessionEvent("stop", "target_exited", process.ExitCode));
    // wfth-record 自体も終了
    cts.Cancel();
};
```

```
{"ts":"...","type":"session","action":"stop","reason":"target_exited","exitCode":0}
```

--launch で起動した場合もアタッチの場合も、対象プロセス終了を検知して自動停止する。

---

## 3. マルチウィンドウ・モーダルダイアログの追跡

### 3.1 問題

WinFormsアプリは複数ウィンドウを持つことが一般的:
- メインフォーム + モーダルダイアログ（SearchForm等）
- メッセージボックス
- コンテキストメニュー（ToolStripDropDown）
- 子ウィンドウ内のコントロール（DateTimePicker のドロップダウン等）

入力フックの `IsTargetWindow()` 判定が正確でないと、関連ウィンドウへの入力を記録できない。

### 3.2 ウィンドウ所有関係の追跡

```
Win32 ウィンドウ所有関係:

MainForm (hwnd=0x100)
  ├── owned → SearchForm (hwnd=0x200)        ← ShowDialog() で表示
  ├── owned → MessageBox (hwnd=0x300)         ← MessageBox.Show()
  └── child → Panel, DataGridView 等          ← 子ウィンドウ

所有関係の取得:
  GetWindow(hwnd, GW_OWNER) → 所有元ウィンドウ
  IsChild(parent, child)    → 親子関係判定
  GetAncestor(hwnd, GA_ROOTOWNER) → ルート所有元
```

### 3.3 WindowTracker 設計

`SetWinEventHook` でウィンドウの生成・破棄・アクティブ化をリアルタイム監視する。

```csharp
class WindowTracker : IDisposable
{
    private readonly int _targetPid;
    private readonly IntPtr _rootHwnd;
    private readonly HashSet<IntPtr> _trackedWindows = new();
    private readonly IntPtr _eventHookHandle;

    public event EventHandler<WindowEvent> WindowChanged;

    public WindowTracker(IntPtr rootHwnd, int targetPid)
    {
        _rootHwnd = rootHwnd;
        _targetPid = targetPid;
        _trackedWindows.Add(rootHwnd);

        // ウィンドウイベントのグローバル監視
        _eventHookHandle = NativeMethods.SetWinEventHook(
            EVENT_OBJECT_SHOW,        // 0x8002
            EVENT_OBJECT_DESTROY,     // 0x8001
            IntPtr.Zero,
            WinEventCallback,
            (uint)_targetPid,         // 対象PIDのみ
            0,                        // 全スレッド
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS
        );
    }

    private void WinEventCallback(
        IntPtr hWinEventHook, uint eventType,
        IntPtr hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != 0) return; // OBJID_WINDOW のみ

        switch (eventType)
        {
            case EVENT_OBJECT_SHOW:
                if (BelongsToTarget(hwnd) && _trackedWindows.Add(hwnd))
                {
                    WindowChanged?.Invoke(this, new WindowEvent
                    {
                        Action = "activated",
                        Hwnd = hwnd,
                        Title = GetWindowTitle(hwnd),
                        ClassName = GetClassName(hwnd),
                        IsModal = IsModalDialog(hwnd)
                    });
                }
                break;

            case EVENT_OBJECT_DESTROY:
                if (_trackedWindows.Remove(hwnd))
                {
                    WindowChanged?.Invoke(this, new WindowEvent
                    {
                        Action = "closed",
                        Hwnd = hwnd
                    });
                }
                break;
        }
    }

    /// <summary>対象アプリに属するウィンドウか判定</summary>
    public bool BelongsToTarget(IntPtr hwnd)
    {
        // 1. 同一プロセスか
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid != _targetPid) return false;

        // 2. 追跡済みウィンドウか
        if (_trackedWindows.Contains(hwnd)) return true;

        // 3. ルート所有元がメインウィンドウか
        var rootOwner = NativeMethods.GetAncestor(hwnd, GA_ROOTOWNER);
        if (rootOwner == _rootHwnd) return true;

        // 4. 子ウィンドウか
        if (NativeMethods.IsChild(_rootHwnd, hwnd)) return true;

        return false;
    }

    /// <summary>モーダルダイアログか判定</summary>
    private bool IsModalDialog(IntPtr hwnd)
    {
        // 所有元ウィンドウが無効化されていればモーダル
        var owner = NativeMethods.GetWindow(hwnd, GW_OWNER);
        return owner != IntPtr.Zero
            && !NativeMethods.IsWindowEnabled(owner);
    }
}
```

### 3.4 IsTargetWindow の改善

InputHookManager の判定を WindowTracker に委譲する。

```csharp
// 改善前: 静的な判定
private bool IsTargetWindow()
{
    var fg = GetForegroundWindow();
    return fg == _targetHwnd || IsChild(_targetHwnd, fg);
}

// 改善後: WindowTracker に委譲
private bool IsTargetWindow()
{
    var fg = NativeMethods.GetForegroundWindow();
    return _windowTracker.BelongsToTarget(fg);
}
```

### 3.5 ウィンドウイベントの出力

```json
{"ts":"...","type":"window","action":"activated","hwnd":"0x002B1234","title":"検索","class":"SearchForm","modal":true}
{"ts":"...","type":"window","action":"closed","hwnd":"0x002B1234","title":"検索"}
{"ts":"...","type":"window","action":"activated","hwnd":"0x003C5678","title":"確認","class":"#32770","modal":true}
{"ts":"...","type":"window","action":"closed","hwnd":"0x003C5678","title":"確認"}
```

`class:"#32770"` は MessageBox のウィンドウクラス名。

---

## 4. 高DPI環境・複数モニタでの座標ズレ

### 4.1 問題

Windows の DPI スケーリングにより、アプリケーションが報告する座標と物理ピクセル座標がずれる:

```
DPI 100% (96dpi):  論理座標 = 物理座標
DPI 150% (144dpi): 論理座標 100 = 物理座標 150
DPI 200% (192dpi): 論理座標 100 = 物理座標 200
```

複数モニタの場合、モニタごとに異なるDPIを持つことがある（Per-Monitor DPI）。

低レベルフック（WH_MOUSE_LL）が返す座標は**物理ピクセル座標**（スクリーン座標）。一方、UIAの `BoundingRectangle` やWinFormsの `Control.Location` はDPI設定によって異なる基準になる可能性がある。

### 4.2 wfth-record の DPI 対応

#### A. wfth-record 自体の DPI Awareness

wfth-record プロセスを **Per-Monitor V2 DPI Aware** に設定する。

```xml
<!-- WinFormsTestHarness.Record.csproj -->
<PropertyGroup>
  <ApplicationManifest>app.manifest</ApplicationManifest>
</PropertyGroup>
```

```xml
<!-- app.manifest -->
<application xmlns="urn:schemas-microsoft-com:asm.v3">
  <windowsSettings>
    <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">
      PerMonitorV2
    </dpiAwareness>
  </windowsSettings>
</application>
```

これにより WH_MOUSE_LL で受け取る座標が物理ピクセル座標になることが保証される。

#### B. 座標の記録方針

**物理ピクセル座標を一次データとして記録する。** DPI情報も併記し、論理座標への変換は wfth-correlate で行う。

```json
{
  "ts": "...",
  "type": "mouse",
  "action": "LeftDown",
  "sx": 675,  // 物理ピクセル（スクリーン座標）
  "sy": 480,
  "rx": 345,  // 物理ピクセル（ウィンドウ相対）
  "ry": 270,
  "dpi": 144, // このイベント時点のモニタDPI
  "monitor": 1
}
```

#### C. モニタ情報の取得

```csharp
class MonitorInfo
{
    public static (int dpi, int monitorIndex, Rect monitorRect)
        GetMonitorAtPoint(int screenX, int screenY)
    {
        var hMonitor = NativeMethods.MonitorFromPoint(
            new POINT(screenX, screenY),
            MONITOR_DEFAULTTONEAREST);

        // Per-Monitor DPI の取得
        NativeMethods.GetDpiForMonitor(
            hMonitor,
            MonitorDpiType.MDT_EFFECTIVE_DPI,
            out var dpiX, out var _);

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        NativeMethods.GetMonitorInfo(hMonitor, ref info);

        return ((int)dpiX, GetMonitorIndex(hMonitor), info.rcMonitor);
    }
}
```

#### D. セッション開始時のモニタ構成記録

```json
{
  "ts": "...",
  "type": "session",
  "action": "start",
  "process": "SampleApp",
  "pid": 12345,
  "hwnd": "0x001A0F32",
  "monitors": [
    {"index":0,"primary":true,"rect":{"x":0,"y":0,"w":2560,"h":1440},"dpi":144},
    {"index":1,"primary":false,"rect":{"x":2560,"y":0,"w":1920,"h":1080},"dpi":96}
  ]
}
```

### 4.3 ウィンドウ相対座標の計算

```csharp
class CoordinateConverter
{
    /// <summary>
    /// スクリーン座標からウィンドウ相対座標を計算（物理ピクセル）
    /// </summary>
    public static (int rx, int ry) ToWindowRelative(
        int screenX, int screenY, IntPtr hwnd)
    {
        // GetWindowRect は DPI Aware プロセスでは物理ピクセルを返す
        NativeMethods.GetWindowRect(hwnd, out var rect);
        return (screenX - rect.Left, screenY - rect.Top);
    }

    /// <summary>
    /// 物理ピクセル座標を論理座標に変換
    /// </summary>
    public static (double lx, double ly) PhysicalToLogical(
        int physicalX, int physicalY, int dpi)
    {
        var scale = dpi / 96.0;
        return (physicalX / scale, physicalY / scale);
    }
}
```

### 4.4 wfth-correlate での座標正規化

wfth-correlate が統合ログを出力する際、物理座標と論理座標の両方を含める。

```json
{
  "seq": 1,
  "type": "Click",
  "input": {
    "button": "Left",
    "physical": {"sx":675,"sy":480,"rx":345,"ry":270},
    "logical": {"sx":450,"sy":320,"rx":230,"ry":180},
    "dpi": 144,
    "monitor": 0
  },
  "target": {
    "source": "UIA",
    "automationId": "btnSearch",
    "rect": {"x":420,"y":310,"w":80,"h":30}
  }
}
```

### 4.5 既知の制限事項

| 状況 | 影響 | 対策 |
|------|------|------|
| 対象アプリが System DPI Aware の場合 | アプリの座標系とフックの座標系が一致しない場合がある | DPI情報を記録し、correlate時に補正 |
| 記録中にDPIが変わる（モニタ間移動） | 座標基準が途中で変わる | イベントごとにDPIを記録 |
| 対象アプリが DPI Unaware の場合 | Windows がスケーリングを仮想化する | 物理座標で記録し、アプリ側の論理座標は推定 |
| リモートデスクトップ環境 | DPI がセッションごとに異なる | セッション開始時のモニタ構成記録で対応 |

---

## 5. 実装優先度

| 対策 | 優先度 | 理由 |
|------|--------|------|
| コールバック高速化（キュー方式） | **必須** | フック安定性の基盤 |
| WindowTracker（マルチウィンドウ追跡） | **必須** | モーダルダイアログが使えないと実用にならない |
| DPI Awareness マニフェスト | **必須** | 座標ズレは致命的 |
| フック生存監視 + 再設定 | 高 | サイレント消失は発見が困難 |
| アプリハング検知 | 高 | 長時間記録で遭遇する |
| モニタ構成記録 | 中 | マルチモニタ環境でなければ不要 |
| 論理座標変換 | 中 | correlate時に後処理可能 |
| プロセスクラッシュガード | 中 | OSがクリーンアップするため影響は限定的 |
| イベント欠損マーカー | 低 | フック復旧後に初めて必要 |
