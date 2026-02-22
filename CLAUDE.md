# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

WinFormsTestHarness — WinForms E2E テスト自動化プラットフォーム。UI Automation + 画像認識 + AIエージェント向け専用ロガーのハイブリッドアプローチで、WinFormsレガシーアプリのE2Eテストを自動化する。

設計ドキュメント: `doc/winforms-e2e-test-platform-design.md`

## Architecture (3-Layer Test Code)

```
テストケース層    — AIが生成するNUnit/xUnit/MSTest互換のテストコード
操作抽象化層      — Page Objectパターンで画面を抽象化、UIA/画像認識/フォールバックを内部制御
ドライバー層      — UIAutomationDriver / ImageRecognitionDriver、OSとの直接やり取り
```

要素特定は `HybridElementLocator` が優先順位付きストラテジー（UIA AutomationId → UIA Name → 画像認識 → AI判断）をフォールバック実行する。

## Project Structure

```
WinFormsTestHarness/
├── src/
│   ├── WinFormsTestHarness.Common/     # 共通ライブラリ（NDJSON I/O, ExitCodes, JsonHelper等）
│   ├── WinFormsTestHarness.Inspect/    # wfth-inspect — UIAツリー偵察CLI（実装済み）
│   ├── WinFormsTestHarness.Record/     # wfth-record — 入力イベント記録（スタブ）
│   ├── WinFormsTestHarness.Capture/    # wfth-capture — スクリーンショット撮影（スタブ）
│   ├── WinFormsTestHarness.Aggregate/  # wfth-aggregate — 生イベント集約（スタブ）
│   ├── WinFormsTestHarness.Correlate/  # wfth-correlate — 時間窓相関（スタブ）
│   ├── WinFormsTestHarness.Core/       # テスト実行フレームワーク（ドライバー層 + 操作抽象化層）
│   └── WinFormsTestHarness.Logger/     # アプリ内ロガー NuGetパッケージ
├── tests/
│   └── WinFormsTestHarness.Tests/
├── samples/
│   └── SampleApp/                      # テスト対象サンプルアプリ
└── demo/                               # パイプライン検証用デモデータ
```

## Tech Stack

- **Language**: C# (.NET)
- **Test runners**: NUnit / xUnit / MSTest
- **UI Automation**: System.Windows.Automation (UIA COM)
- **Image recognition**: 未定（OpenCV、Windows.Media.Ocr、外部AI等）
- **IPC**: 名前付きパイプ (NamedPipeStream)
- **Log format**: JSON
- **Screenshots**: PNG

## Key Design Decisions

- アプリ内ロガーは `[Conditional("E2E_TEST")]` 属性で本番ビルドから完全除去される（ILレベルで呼び出しが消える）
- ビルド構成: `dotnet build -c Release`（本番）vs `dotnet build -c E2ETest`（ロガー有効）
- Recording EngineはSetWindowsHookExによるグローバルフックで入力をキャプチャし、対象ウィンドウのみをフィルタリング
- スクリーンショットは差分検知（2%閾値）で無変化時スキップ
- 統合ログは操作ログ + スクリーンショット + UIAツリー差分 + アプリ内ロガーイベントを時系列で紐付け

## Build & Test Commands

```bash
dotnet build -c E2ETest   # ロガー有効テスト用ビルド
dotnet build -c Release   # 本番ビルド（ロガー除去）
dotnet test               # テスト実行
```

## Git Workflow

- 作業単位ごとにブランチを作成してから作業を開始すること
- ブランチ作成は `gh` コマンドを使用する
- ブランチ名は `feature/<短い説明>` または `fix/<短い説明>` 形式（英語）
- 作業完了後は `gh pr create` でPRを作成する

```bash
# ブランチ作成 & チェックアウト
gh repo set-default  # 初回のみ
git switch -c feature/my-feature

# 作業完了後
git add <files>
git commit -m "メッセージ"
git push -u origin feature/my-feature
gh pr create --title "タイトル" --body "説明"
```

## Development Notes

- このリポジトリはPhase 1（Recording & 回帰テスト生成）の実装が主目標
- Phase 2（仕様書駆動テスト生成）は将来構想
- AIエージェントのモデル選択・プロンプト設計は意図的にスコープ外
- テスト仕様書パーサーは詳細設計未着手
- 言語は日本語ベースで開発（コメント、ドキュメント、テスト名）
