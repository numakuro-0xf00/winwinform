#!/usr/bin/env bash
# demo.sh — パイプラインフォーマット検証デモ
#
# 手作りの NDJSON テストデータを使って、パイプラインの各段階を実証する。
# 実際のツール実装前にフォーマット設計を検証する目的。
#
# 前提: jq がインストールされていること
#
# 使い方:
#   cd demo && bash demo.sh

set -euo pipefail
DIR="$(cd "$(dirname "$0")" && pwd)"

echo "=== WinFormsTestHarness パイプライン デモ ==="
echo ""

# --- 1. record.ndjson の内容確認 ---
echo "--- 1. record.ndjson（生イベント）---"
echo "イベント数: $(wc -l < "$DIR/record.ndjson")"
echo "イベントタイプ別:"
jq -r '.type' < "$DIR/record.ndjson" | sort | uniq -c | sort -rn
echo ""

# --- 2. jq でタイプフィルタ ---
echo "--- 2. マウスイベントのみ抽出 ---"
jq 'select(.type == "mouse")' < "$DIR/record.ndjson"
echo ""

# --- 3. jq でキーボードイベント抽出 ---
echo "--- 3. キーボードイベント（down のみ）---"
jq 'select(.type == "key" and .action == "down")' < "$DIR/record.ndjson"
echo ""

# --- 4. uia.ndjson の内容確認 ---
echo "--- 4. uia.ndjson（UIAツリー変化）---"
echo "スナップショット数: $(wc -l < "$DIR/uia.ndjson")"
jq '{ts: .ts, name: .name, childCount: (.children | length)}' < "$DIR/uia.ndjson"
echo ""

# --- 5. 全ストリームをマージ＆ソート ---
echo "--- 5. 全ストリームのマージ＆タイムスタンプソート ---"
jq -s 'sort_by(.ts) | .[] | {ts: .ts, type: .type, action: .action}' \
  "$DIR/record.ndjson" "$DIR/uia.ndjson"
echo ""

# --- 6. 期待される集約結果（wfth-aggregate 出力相当）---
echo "--- 6. 期待される集約結果（wfth-aggregate の出力イメージ）---"
cat <<'AGGREGATE'
{"ts":"2026-02-22T14:30:05.100Z","type":"Click","button":"Left","sx":450,"sy":320,"rx":230,"ry":180}
{"ts":"2026-02-22T14:30:08.400Z","type":"TextInput","text":"Tan","startTs":"2026-02-22T14:30:08.400Z","endTs":"2026-02-22T14:30:08.600Z"}
{"ts":"2026-02-22T14:30:10.700Z","type":"SpecialKey","key":"Enter"}
{"ts":"2026-02-22T14:30:15.000Z","type":"DoubleClick","button":"Left","sx":500,"sy":400,"rx":280,"ry":260}
AGGREGATE
echo ""

# --- 7. 期待される相関結果（wfth-correlate 出力相当）---
echo "--- 7. 期待される相関結果（wfth-correlate の出力イメージ）---"
cat <<'CORRELATE'
{"seq":1,"ts":"2026-02-22T14:30:05.100Z","type":"Click","input":{"button":"Left","sx":450,"sy":320,"rx":230,"ry":180},"uiaDiff":{"added":[{"name":"検索","controlType":"Window","className":"SearchForm"}]}}
{"seq":2,"ts":"2026-02-22T14:30:08.400Z","type":"TextInput","input":{"text":"Tan","duration":0.2}}
{"seq":3,"ts":"2026-02-22T14:30:10.700Z","type":"SpecialKey","input":{"key":"Enter"},"uiaDiff":{"changed":[{"automationId":"dgvResults","property":"rows","from":5,"to":1}]}}
{"seq":4,"ts":"2026-02-22T14:30:15.000Z","type":"DoubleClick","input":{"button":"Left","sx":500,"sy":400,"rx":280,"ry":260}}
CORRELATE
echo ""

echo "=== デモ完了 ==="
echo ""
echo "このデモは手作りデータによるフォーマット検証です。"
echo "実際のパイプライン:"
echo "  wfth-aggregate < record.ndjson | wfth-correlate --uia uia.ndjson > session.ndjson"
