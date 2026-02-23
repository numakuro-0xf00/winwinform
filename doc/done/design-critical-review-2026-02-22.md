# 設計レビュー（批判的レビュー）

- レビュー日: 2026-02-22
- 対象: `doc/*.md` 一式（特に Recording 系、Spec Parser、統合設計）
- 観点: 仕様整合性、実装可能性、運用リスク、セキュリティ
- 注記: 本ドキュメントは修正前の指摘スナップショット。修正内容は別途コミット差分を参照。
- 対応状況: **全8件対応済み** (2026-02-23 更新)

## 総評

現状の設計は方向性は良いが、**CLI 契約と責務分担の整合性が崩れているため、そのまま実装開始すると高確率で手戻り**が発生する。
最低でも Critical 1件と High 4件を解消してから実装フェーズへ進むべき。

## Findings（重大度順）

### 1. [Critical] ~~`wfth-correlate` の CLI 契約が文書間で衝突している~~ → 対応済み

- 根拠:
  - `doc/recording-cli-design.md:187` は `wfth-correlate` 入力を `stdin`（`wfth-aggregate` 出力）として定義
  - `doc/correlate-split-design.md:159` は暗黙ディレクトリ規約の廃止を明記
  - `doc/capture-design.md:300` は `wfth-correlate --record ...` を前提
  - `doc/spec-parser-design.md:800` は `wfth-correlate $SESSION/` を前提
- 影響:
  - 実装者がどの I/F を正とすべきか判断できない
  - サンプルスクリプト/CI 手順の互換性が壊れる
- 提案:
  - `wfth-correlate` の正規 I/F を 1 つに固定し、全ドキュメントを同一契約へ更新
  - 旧 I/F を残すなら「互換モード」と明記し、廃止期限を定義
- **対応**: `142177f` — `winforms-e2e-test-platform-design.md` に正規ソース参照追加、`architecture-review.md` 更新、`correlate-split-design.md` に `--noise-threshold` 追加

### 2. [High] ~~出力フォーマット（`session.ndjson` vs `session.json`）が不一致~~ → 対応済み

- 根拠:
  - `doc/recording-cli-design.md:199` はデフォルト出力 NDJSON
  - `doc/recording-cli-design.md:159` は `session.ndjson` を例示
  - `doc/correlate-split-design.md:23` は `session.json` 変換を将来の `wfth-session` に分離
  - `doc/capture-design.md:441` と `doc/spec-parser-design.md:802` は `session.json` を前提
- 影響:
  - 下流ツールの入力契約が揺れ、実装・テストの前提が定まらない
- 提案:
  - 「正規出力は NDJSON」「JSON 集約は別段（`jq -s` or `wfth-session`）」のように一本化
  - 変換境界を architecture 図と CLI 例に反映
- **対応**: `capture-design.md` で「変更後」を `session.ndjson` に統一済み。`spec-parser-design.md` から `session.json` 参照を削除済み

### 3. [High] ~~ノイズ分類の担当（aggregate / correlate）が衝突している~~ → 対応済み

- 根拠:
  - `doc/recording-cli-design.md:176` は `wfth-aggregate --no-denoise` を定義
  - `doc/correlate-split-design.md:212` はノイズ分類を aggregate 担当と定義
  - `doc/recording-data-quality-design.md:20` はノイズ分類を correlate 担当と定義
  - `doc/recording-data-quality-design.md:152` は correlate 側オプションを定義
- 影響:
  - 実装責務が二重化し、テスト観点も分裂する
- 提案:
  - ノイズ分類の責務をどちらかに固定
  - 固定後、もう一方の文書からルール本体を削除して参照だけ残す
- **対応**: `142177f` で確認 — `recording-data-quality-design.md` で correlate 側に統一済み。`recording-cli-design.md` から `--no-denoise` は削除済み

### 4. [High] ~~Spec Parser の入力拡張子定義が自己矛盾している（`.xls`）~~ → 対応済み

- 根拠:
  - `doc/spec-parser-design.md:53` は入力を `.xlsx, .xls` と定義
  - `doc/spec-parser-design.md:103` は ClosedXML 採用
  - `doc/spec-parser-design.md:929` は「`.xlsx のみ対応`」を制限事項として明記
- 影響:
  - CLI 契約と実装可能性が不一致で、利用者にランタイムエラーを招く
- 提案:
  - MVP 契約を `.xlsx` のみに修正し、`.xls` は事前変換を明記
  - 入力バリデーションで `.xls` を即時エラー化（変換手順メッセージ付き）
- **対応**: `spec-parser-design.md:53` を `.xlsx のみ。.xls は事前変換` に修正済み。制限事項 (line 927) でも明記

### 5. [High] ~~IPC 設計に改ざん・盗聴対策の記述が不足~~ → 対応済み

- 根拠:
  - `doc/recording-integration-design.md:186` は NDJSON over Named Pipe
  - `doc/recording-integration-design.md:189` は予測可能なパイプ名 `WinFormsTestHarness_{pid}`
  - `doc/recording-integration-design.md:198` と `doc/recording-integration-design.md:201` は業務データを含むイベント例
- 影響:
  - 同一ホスト上の別プロセスによる偽装送信・覗き見リスク
  - 監査要件がある環境で運用しづらい
- 提案:
  - セッションランダム値を含むパイプ名へ変更
  - 接続 ACL を「同一ユーザー/同一プロセス起動元」に限定
  - 初期ハンドシェイク（nonce/token）で正当性検証を追加
- **対応**: `recording-integration-design.md` に sessionNonce 付きパイプ名・ACL 制限・hello/challenge/response ハンドシェイクを追加済み。`logger-architecture-design.md` も `5507fc7` で統一

### 6. [Medium] ~~フック生存監視ロジックに誤検知リスクがある~~ → 対応済み

- 根拠:
  - `doc/recording-reliability-design.md:42` は「自己テストイベント方式」と記載
  - `doc/recording-reliability-design.md:47` と `doc/recording-reliability-design.md:75` は「前面かつ 5 秒無コールバック」で死活判定
- 影響:
  - ユーザー無操作のアイドル時に不要な再フックが走る可能性
  - 再フック中の欠損イベントを増やす
- 提案:
  - 実際に自己テストイベントを送る設計へ統一するか、`GetLastInputInfo` 等で「入力があったのにフック未着」のみ異常判定にする
- **対応**: `recording-reliability-design.md:62-71` で `GetLastInputInfo` + 自己テストパルス方式に統一済み。Idle 時は再設定しない設計

### 7. [Medium] ~~キュー運用に上限・劣化方針がない~~ → 対応済み

- 根拠:
  - `doc/recording-reliability-design.md:17` はキュー投入方式
  - `doc/recording-reliability-design.md:24` と `doc/recording-reliability-design.md:35` は `ConcurrentQueue` を使用
  - バックプレッシャー/上限/ドロップ方針の記述が見当たらない
- 影響:
  - 高頻度入力や I/O 劣化時にメモリ膨張し、プロセス不安定化を招く
- 提案:
  - 有界キュー化（`Channel<T>` 等）とドロップポリシー（古いイベント破棄/圧縮）を定義
  - ドロップ発生を system イベントとして記録
- **対応**: `recording-reliability-design.md:35-43` で `BoundedChannel(capacity: 4096)` + 劣化モードを定義済み

### 8. [Medium] ~~最上位の全体設計書が最新分割方針を反映していない~~ → 対応済み

- 根拠:
  - `doc/winforms-e2e-test-platform-design.md:776` は単一 `EventCorrelator` を中核として記述
  - `doc/recording-cli-design.md:153` は `aggregate + correlate` 分割へ設計変更済み
- 影響:
  - 新規参加者が古い全体像を正と誤認しやすい
- 提案:
  - 全体設計書の冒頭に「この章は旧構成」注記を追加するか、構成図を分割後に更新
  - 「正本ドキュメント」を 1 つ決めて他文書は参照中心にする
- **対応**: `142177f` — `winforms-e2e-test-platform-design.md:3-4` に更新注記追加、セクション 5.5 を分割後の設計に更新済み

## 実装前チェックリスト（推奨）

- [x] `wfth-correlate` の入出力契約を確定し、関連ドキュメントを同日更新する — `142177f`
- [x] ノイズ分類の責務を aggregate / correlate のどちらかに固定する — correlate に統一済み (`142177f`)
- [x] Spec Parser の入力仕様を `.xlsx` に統一し、`.xls` の扱いを明文化する — `spec-parser-design.md:53` で対応済み
- [x] IPC の接続保護（命名、ACL、ハンドシェイク）を最小限で設計に追加する — `recording-integration-design.md` + `logger-architecture-design.md` (`5507fc7`)
- [x] Hook 監視ロジックの誤検知条件とキュー劣化時の挙動を定義する — `recording-reliability-design.md` で GetLastInputInfo + 自己テストパルス方式、BoundedChannel(4096) を定義済み
