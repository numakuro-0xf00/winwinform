# WinFormsTestHarness.Common 共通ライブラリ設計

## 1. 背景

UNIX思想レビューにおいて、DRY（Don't Repeat Yourself）原則の違反が最大の弱点として識別された。具体的には:

- NDJSON 読み書きロジックが各ツールで再実装されている
- ウィンドウハンドル解決（0x形式パース）が複数ツールで重複
- CLI オプション定義（`--process`, `--hwnd`, `--out`）が各ツールで独立定義
- JSON シリアライズ設定（camelCase + null省略）が統一されていない
- 終了コードの定義が存在しない

## 2. 設計方針

全CLIツール（`wfth-inspect`, `wfth-record`, `wfth-capture`, `wfth-aggregate`, `wfth-correlate`）が共有するインフラストラクチャを単一ライブラリに集約する。

- **依存方向**: Common は他のプロジェクトに依存しない（最下層）
- **TargetFramework**: `net8.0-windows`（全ツールと同一）
- **パッケージ依存**: `System.CommandLine`（CLI オプション定義のため）

## 3. モジュール構成

```
WinFormsTestHarness.Common/
├── IO/
│   ├── NdJsonWriter.cs       — NDJSON出力（stdout / ファイル）
│   └── NdJsonReader.cs       — NDJSON入力（stdin / ファイル、不正行の報告付き）
├── Timing/
│   └── PreciseTimestamp.cs   — Stopwatch ベース高精度タイムスタンプ
├── Windows/
│   └── HwndHelper.cs        — ウィンドウハンドル 0x形式パース
├── Cli/
│   ├── ExitCodes.cs          — 終了コード定数（0=成功, 1=引数, 2=未発見, 3=実行時）
│   ├── CommonOptions.cs      — 共通CLIオプション定義
│   └── DiagnosticContext.cs  — --debug / --quiet フラグ制御
└── Serialization/
    └── JsonHelper.cs         — camelCase + null省略のJSON設定
```

## 4. 各モジュール詳細

### 4.1 Cli/ExitCodes

全CLIツールで統一する終了コード:

| コード | 定数名 | 意味 | 例 |
|--------|--------|------|-----|
| 0 | `Success` | 正常終了 | — |
| 1 | `ArgumentError` | 引数エラー | 必須オプション不足、不正な値 |
| 2 | `TargetNotFound` | 対象未発見 | プロセスが見つからない、UI要素なし |
| 3 | `RuntimeError` | 実行時エラー | UIA操作失敗、I/Oエラー |

シェルスクリプトでの使用例:
```bash
wfth-inspect list || echo "exit code: $?"
wfth-inspect tree --process NotExist; echo $?  # → 2
```

### 4.2 Cli/DiagnosticContext

`--debug` と `--quiet` フラグの状態を保持し、stderr 出力を制御する:

- `DebugLog(msg)`: `--debug` 時のみ `[DEBUG] msg` を stderr に出力
- `Warn(msg)`: `--quiet` でなければ `Warning: msg` を stderr に出力
- `Info(msg)`: `--quiet` でなければ stderr に出力
- `Error(msg)`: 常に `Error: msg` を stderr に出力（`--quiet` でも抑制しない）

### 4.3 IO/NdJsonWriter

stdout またはファイルへの NDJSON 出力。各行を即座に flush する:

```csharp
using var writer = NdJsonWriter.ToStdout();
writer.Write(new { ts = "...", type = "mouse", action = "LeftDown" });
```

### 4.4 IO/NdJsonReader

stdin またはファイルからの NDJSON 読み込み。不正行は stderr に報告してスキップ（サイレントドロップ禁止）:

```csharp
var reader = NdJsonReader.FromFile("record.ndjson");
foreach (var evt in reader.ReadAll<InputEvent>())
{
    // ...
}
```

不正行の報告例:
```
Warning: NDJSON parse error at line 42: Expected '{' but got 'n'
  Content: not a json line
```

### 4.5 Serialization/JsonHelper

`System.Text.Json` の設定を統一:

- `PropertyNamingPolicy = CamelCase`
- `DefaultIgnoreCondition = WhenWritingNull`

全ツールがこの設定を共有することで、NDJSON フォーマットの一貫性を保証する。

### 4.6 Windows/HwndHelper

`0x001A0F32` 形式のウィンドウハンドル文字列を `IntPtr` にパースする。
`0x` プレフィックスの有無を許容する。

### 4.7 Timing/PreciseTimestamp

`Stopwatch` ベースの高精度タイムスタンプ生成。
`DateTime.UtcNow` の解像度（約15ms）を超えるミリ秒精度を提供する。

## 5. 移行計画

### Phase 1（実施済み）
- `wfth-inspect` の `JsonHelper` と `HwndHelper` を Common に委譲
- 全コマンドに `ExitCodes` を適用
- `--debug` / `--quiet` グローバルオプション追加
- 全スタブプログラムが Common を参照

### Phase 2（各ツール実装時）
- `wfth-record` 実装時: `NdJsonWriter`, `HwndHelper`, `PreciseTimestamp` を使用
- `wfth-capture` 実装時: `NdJsonWriter`, `HwndHelper` を使用
- `wfth-aggregate` 実装時: `NdJsonReader`, `NdJsonWriter` を使用
- `wfth-correlate` 実装時: `NdJsonReader`, `NdJsonWriter` を使用

## 6. 依存関係図

```
wfth-inspect ──┐
wfth-record ───┤
wfth-capture ──┼── WinFormsTestHarness.Common
wfth-aggregate─┤
wfth-correlate─┘
```
