#!/bin/bash
# PostToolUse hook: Write|Edit 後に .cs ファイルの roslyn-query diagnostics を実行
# エラーがあれば Claude にフィードバックする

INPUT=$(cat)
FILE_PATH=$(echo "$INPUT" | jq -r '.tool_input.file_path')

# .cs ファイル以外はスキップ
if [[ ! "$FILE_PATH" =~ \.cs$ ]]; then
  exit 0
fi

# roslyn-query diagnostics を実行
RESULT=$(roslyn-query diagnostics "$FILE_PATH" --json 2>&1)
EXIT_CODE=$?

# デーモン未起動（exit 4）はスキップ
if [ $EXIT_CODE -eq 4 ]; then
  # "Document not found" = 新規ファイルでデーモンが未認識 → 再起動してリトライ
  if echo "$RESULT" | grep -q "Document not found"; then
    roslyn-query shutdown >/dev/null 2>&1
    roslyn-query init >/dev/null 2>&1
    RESULT=$(roslyn-query diagnostics "$FILE_PATH" --json 2>&1)
    EXIT_CODE=$?
    # 再起動後も失敗ならスキップ
    if [ $EXIT_CODE -eq 4 ]; then
      exit 0
    fi
  else
    exit 0
  fi
fi

# 診断結果があれば Claude にフィードバック
if echo "$RESULT" | jq -e '.diagnostics | length > 0' >/dev/null 2>&1; then
  jq -n --arg ctx "$RESULT" '{
    "hookSpecificOutput": {
      "hookEventName": "PostToolUse",
      "additionalContext": ("roslyn-query diagnostics 結果:\n" + $ctx)
    }
  }'
fi

exit 0
