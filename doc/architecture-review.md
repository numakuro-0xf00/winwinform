# 設計レビュー: WinFormsTestHarness

レビュー日: 2026-02-22

## 総合評価

設計の全体的な方向性は優れている。Unix哲学に基づくパイプラインCLI分離、ハイブリッドUI要素特定、条件付きコンパイルによるロガー無害化、NDJSONによるストリーム間連携など、技術的な判断の多くは的確。wfth-inspect の実装品質も高く、FlaUI/SWA両バックエンドの抽象化やデータモデルは堅実。

以下、3つのカテゴリで指摘する。

---

## A. 要修正 — アーキテクチャ上の矛盾・不整合

### A-1. 条件付きコンパイル戦略の不一致

メイン設計ドキュメントでは `[Conditional("E2E_TEST")]` 属性方式を提示しているが、logger-architecture-design.md では `#if E2E_TEST` 方式に変更している。**これらは根本的に異なるメカニズムである。**

| 観点 | `[Conditional]` | `#if` |
|------|----------------|-------|
| 呼び出し側の影響 | 呼び出し側のコンパイル時にシンボル必要（IL呼び出し除去） | Logger側のメソッド本体が空になるだけ |
| アプリ側の変更 | `TestLogger.Attach()` を**そのまま書ける**（コンパイラが除去） | `#if E2E_TEST` で**囲む必要がある** |
| NuGetパッケージ利用時 | パッケージ側でシンボル定義不要 | パッケージDLLの**ビルド時点**でシンボル必要 |

`#if` 方式ではLogger DLLは常にビルドされるがメソッド本体が空になるため、**アプリ側にも`#if E2E_TEST`の記述が必要**になる。メイン設計ドキュメントの「Program.csに1行追加」というシンプルさの約束が崩れる。

**推奨**: 設計ドキュメント間で方針を統一する。`#if`方式は技術的にはlogger-architecture-design.mdの理由（CI Release+Logger有効）が妥当なので、メイン設計ドキュメント（セクション4）を更新すべき。

### A-2. Logger csproj の TargetFramework 矛盾

現在の実装:
```xml
<!-- Logger.csproj -->
<TargetFramework>netstandard2.0</TargetFramework>
```

logger-architecture-design.md の設計:
```xml
<TargetFramework>net8.0-windows</TargetFramework>
<UseWindowsForms>true</UseWindowsForms>
```

Logger は `Control`, `Form`, `TextBox`, `DataGridView` 等の WinForms 型に直接依存する。`netstandard2.0` では**コンパイル不可能**。これは実装着手時のブロッカーになる。

### A-3. プロジェクト名の不一致

| 設計ドキュメント（CLAUDE.md/メイン設計doc） | 実際のプロジェクト |
|---|---|
| `WinFormsTestHarness.Recorder` | `WinFormsTestHarness.Record` |
| `WinFormsTestHarness.SpecParser` | 未作成 |
| `WinFormsTestHarness.Capture.Cli`（capture-design.md） | 未作成 |

特に `Recorder` vs `Record` は CI やドキュメント参照で混乱を招く。CLI のコマンド名（`wfth-record`）に合わせて `Record` とするか、設計ドキュメントを更新すべき。

### A-4. Capture プロジェクトの役割変更が未反映

capture-design.md では Capture を **classlib** に変更し、別途 **Capture.Cli** を新設する設計だが、現在の csproj はまだ `<OutputType>Exe</OutputType>` + `<PackAsTool>` のコンソールアプリのまま。Core が Capture を `ProjectReference` する設計なので、classlib 化は Core 実装のブロッカー。

---

## B. 設計改善の提案

### B-1. セッション・オーケストレーター不在

Recording セッションには `wfth-record` + `wfth-inspect` の2プロセス並列起動が必要で、現設計ではユーザーがシェルスクリプトで手動管理する。

```bash
# 現在の設計: ユーザーが手動で管理
wfth-record  --process SampleApp --capture > $SESSION/record.ndjson &
wfth-inspect watch --process SampleApp     > $SESSION/uia.ndjson &
# ... 手動で kill & wait ...
wfth-correlate $SESSION/ -o $SESSION/session.json
```

**問題**:
- プロセス間のシグナル伝搬が未定義（一方がクラッシュした場合）
- セッションディレクトリの作成・管理が煩雑
- 初回ユーザーにとって敷居が高い

**提案**: `wfth-session` オーケストレーターCLIを検討する。内部で子プロセスを起動・監視し、`Ctrl+C` で一括停止 → 自動 correlate を行う。シェルスクリプトのパイプライン操作も引き続きサポートすることで後方互換性を保てる。MVP 後の対応で構わないが、設計の TODO に入れるべき。

### B-2. HybridElementLocator のタイムアウト分配戦略

現設計:
```
全ストラテジー合計でデフォルト10秒
例: 3つのストラテジーがある場合 → 各 ~3.3秒
```

**問題**: UIA の `ByAutomationId` は通常 50ms で結果が返る。10秒のうち3.3秒を割り当てるのは無駄。一方、画像認識はスクリーンショット撮影 + マッチングで 1 秒以上かかる可能性がある。

**提案**: 均等分配ではなく、**各ストラテジーに個別タイムアウトを設定可能にし、デフォルトではUIA系を短く（2秒）、画像系を長く（5秒）**にする。前段のストラテジーが即座に失敗した場合、残時間を後段に繰り越す現在の設計は良いが、デフォルト配分を見直すべき。

### B-3. Element キャッシュの無効化漏れ

`Element.ClickAsync()` 後にキャッシュを無効化する設計だが、以下の操作ではキャッシュが残る:

- `SetTextAsync()` — テキスト入力で画面遷移する場合がある（例: オートコンプリート）
- `SelectAsync()` — ComboBox 選択でUI構造が変わる場合がある
- `SetCheckedAsync()` — チェックで関連フィールドの表示/非表示が切り替わる場合がある
- 外部要因 — タイマーベースの画面更新、他スレッドからのUI変更

**提案**: 全操作後にキャッシュを無効化するか、キャッシュ TTL（例: 1秒）を導入する。パフォーマンスへの影響は軽微（UIA の FindFirst は通常 10-50ms）。

### B-4. パスワード保護の一貫性

Logger はパスワードフィールドの値をマスクするが、**スクリーンショットにはパスワードが視覚的に映る可能性がある**。また、`wfth-record` はキーボードのキーイベントをそのまま記録するため、**パスワードの入力シーケンスが生データに残る**。

**提案**: recording-data-quality-design.md に「パスワードフィールド操作時のデータ保護方針」を追加する。少なくとも:
- wfth-correlate で target が PasswordChar 付き TextBox と判明した場合、入力テキストをマスク
- スクリーンショットでのパスワード領域のぼかしは「将来検討」として明記

### B-5. AI判断ストラテジー（ByRelativePosition）の設計不在

メイン設計ドキュメントでは以下のような「最終手段」が示されている:

```csharp
Strategy.ByRelativePosition("検索ボタンの左隣のテキストボックス")
```

しかし、core-design.md の `StrategyKind` enum にこの種別が存在しない。`IElementDriver` にも対応する実装がない。メイン設計ドキュメントの最も野心的な差別化要素が、Core設計に反映されていない。

**提案**: MVP E のスコープ外であっても、`StrategyKind.ByRelativePosition` と `StrategyKind.ByAiVision` を enum に予約定義し、Core設計に「将来拡張」として明記すべき。

### B-6. wfth-correlate のイベント順序保証

現設計では `record.ndjson` と `uia.ndjson` を別プロセスが生成し、correlate がタイムスタンプでマージする。

**問題**: 2つのストリームの書き込みバッファリングのタイミングにより、ファイル上の行順序がタイムスタンプ順と異なる場合がある。特に `wfth-record` は `stdout` バッファリング（行バッファかフルバッファか）の影響を受ける。

**提案**: recording-cli-design.md に「NDJSON 出力は行バッファモード（`Console.Out.AutoFlush = true` または `--line-buffered`）を使用する」と明記する。correlate 側のタイムスタンプソートで吸収可能だが、入力データの前提条件として記載が必要。

### B-7. テストランナー抽象化

現設計では「基盤部分（AppInstance, HybridElementLocator 等）はランナー非依存」と明記されているが、これがアーキテクチャ上で**強制**されていない。

`WinFormsTestBase` が `NUnit` に直接依存しているのは設計判断として妥当だが、Core パッケージ（NuGet）自体が `NUnit` への PackageReference を持つ設計になっている（core-design.md セクション9.2）。

**提案**: Core パッケージから NUnit 依存を除外し、`WinFormsTestBase` は別パッケージ（`WinFormsTestHarness.NUnit`）または `samples/` に配置する。こうすることで、Core を NUnit なしで参照可能にし、将来の xUnit/MSTest サポートが自然に可能になる。

---

## C. 注意事項・検討推奨

### C-1. グローバルフックと管理者権限

`WH_MOUSE_LL` / `WH_KEYBOARD_LL` は低レベルフックであり、一部のセキュリティソフト（特にエンタープライズ環境のEDR）がブロックする可能性がある。WinForms レガシーアプリが多い「現場」で遭遇する可能性が高いリスク。設計ドキュメントに記載がない。

### C-2. System.CommandLine のバージョン

全 CLI ツールが `System.CommandLine 2.0.0-beta4.22272.1` を使用している。これは 2022年のベータ版であり、API が安定版で変更される可能性がある。`System.CommandLine` の GA リリース状況を確認し、他の CLI ライブラリも選択肢として記録しておくことを推奨する。

### C-3. CI ヘッドレス実行の信頼性

GitHub Actions `windows-latest` で UIA が動作するという知見は価値があるが、**GitHub がランナーイメージを更新した際に UIA の動作が壊れる**リスクがある。E2E テストの CI 成功率を安定させるには、自前の Windows VM ランナーも並行で検討すべき。

### C-4. Spec Parser の LLM コスト

テスト仕様書パーサーは LLM 呼び出しが必須であり、50行のExcelシートを1回パースするのに Claude/GPT の API コストが発生する。大規模テストスイート（数百テストケース）で繰り返し使用するとコストが積み上がる。

**検討**: 既知の定型フォーマット（ヘッダー行が固定のExcel）にはルールベースのパーサーを提供し、LLM は非定型フォーマットのフォールバックとする2層構造が実用的。

### C-5. DPI 処理のテスト再生側設計

recording-reliability-design.md で DPI 記録を詳細に設計しているが、**Core フレームワーク（テスト再生側）で DPI をどう扱うか**が未設計。ActionExecutor のマウスクリックは物理座標を使用するが、記録時と再生時の DPI が異なる場合に座標変換が必要。

### C-6. ControlWatcher のメモリリーク

ControlWatcher は `ControlAdded` でコントロールを監視対象に追加するが、`ControlRemoved` での**ハンドラ解除が設計に含まれていない**。長時間稼働するアプリでは、削除されたコントロールへの参照がイベントハンドラ経由で保持され、GC 対象にならない可能性がある。

---

## まとめ

| カテゴリ | 件数 | 対応 |
|---------|------|------|
| **A. 要修正（矛盾・不整合）** | 4件 | 実装着手前に解消必須 |
| **B. 設計改善提案** | 7件 | 実装中に随時対応 |
| **C. 注意事項** | 6件 | リスクとして認識・記録 |

設計のレイヤー分離、フォールバック戦略、データ形式の選択など、アーキテクチャの骨格は良好。特にカテゴリA（条件付きコンパイル方針の統一、Logger csproj の修正、プロジェクト名の統一、Capture のclasslib化）を設計ドキュメントレベルで解消してから実装に入ることを推奨する。
