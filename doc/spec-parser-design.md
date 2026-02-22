# テスト仕様書パーサー（wfth-parse）設計

## 1. 概要

### 1.1 目的

日本の開発現場で一般的なExcelベースのテスト仕様書を読み取り、AIエージェントがテストコード生成に使える構造化JSON（TestSpec）に変換する。

### 1.2 設計方針

```
原則: フォーマット解釈をAI（LLM）に委譲する

理由:
  - 日本のテスト仕様書はチーム・会社ごとにフォーマットが異なる
  - ヘッダー行の位置、列の意味、セル結合パターンが非定型
  - ルールベースの解析は脆く、新フォーマットごとに開発が必要
  - LLM はレイアウトの「意図」を読み取れる
```

### 1.3 処理フロー

```
Excel ファイル (.xlsx)
    │
    ▼
[1. Excel Reader]  セルデータ + 書式情報を抽出
    │
    ▼
[2. Layout Analyzer]  構造的特徴を抽出（ヘッダー候補、セル結合、罫線パターン）
    │
    ▼
[3. LLM Interpreter]  レイアウトを解釈し、テストケースを構造化データに変換
    │
    ▼
[4. Validator]  JSON Schema 検証 + 整合性チェック
    │
    ▼
[5. TestSpec JSON]  出力
```

---

## 2. CLIインターフェース

```
wfth-parse [options] <input-file>

Input:
  <input-file>          Excelファイルパス (.xlsx, .xls)

Options:
  -o, --output <path>   出力先（デフォルト: stdout）
  --sheet <name>        対象シート名（デフォルト: 全シート走査）
  --sheet-index <n>     対象シートインデックス（0始まり）
  --format <type>       出力形式: json|yaml（デフォルト: json）
  --dry-run             LLM呼び出しなしでExcel解析結果のみ表示（デバッグ用）

LLM Options:
  --provider <name>     LLMプロバイダ: anthropic|openai|local（デフォルト: anthropic）
  --model <name>        モデル名（デフォルト: プロバイダのデフォルト）
  --api-key <key>       APIキー（環境変数 WFTH_LLM_API_KEY も使用可）
  --max-retries <n>     LLM応答のバリデーション失敗時の再試行回数（デフォルト: 2）

Template Options:
  --hint <path>         レイアウトヒントファイル（フォーマットの説明を与える）
  --example <path>      入出力例ファイル（Few-shot用の変換例）
```

### 使用例

```bash
# 基本的な使い方
wfth-parse test-spec.xlsx -o test-spec.json

# 特定シートのみ
wfth-parse test-spec.xlsx --sheet "テストケース一覧" -o test-spec.json

# レイアウトヒントを与える
wfth-parse test-spec.xlsx --hint hints/our-format.md -o test-spec.json

# デバッグ: Excel解析結果を確認
wfth-parse test-spec.xlsx --dry-run

# OpenAI を使用
wfth-parse test-spec.xlsx --provider openai --model gpt-4o
```

---

## 3. Excel Reader

### 3.1 NuGet依存

```
ClosedXML — .xlsx 読み書き（MITライセンス、アクティブにメンテナンス）
```

EPPlus は商用ライセンスに変更されたため ClosedXML を採用。

### 3.2 抽出データ

```csharp
/// <summary>シート全体のデータ</summary>
public class SheetData
{
    public string SheetName { get; set; } = "";
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }
    public List<CellData> Cells { get; set; } = new();
    public List<MergedRegion> MergedRegions { get; set; } = new();
}

/// <summary>セル情報</summary>
public class CellData
{
    public int Row { get; set; }        // 1始まり
    public int Column { get; set; }     // 1始まり
    public string Address { get; set; } = "";  // "A1", "B3" 等
    public string Value { get; set; } = "";
    public CellFormat Format { get; set; } = new();
}

/// <summary>セル書式（レイアウト解釈に使用）</summary>
public class CellFormat
{
    public bool IsBold { get; set; }
    public bool HasBorder { get; set; }
    public string BackgroundColor { get; set; } = "";  // "#RRGGBB" or ""
    public string FontColor { get; set; } = "";
    public double FontSize { get; set; }
    public HorizontalAlignment Alignment { get; set; }
}

/// <summary>セル結合情報</summary>
public class MergedRegion
{
    public string Range { get; set; } = "";  // "A1:C1"
    public int FirstRow { get; set; }
    public int FirstColumn { get; set; }
    public int LastRow { get; set; }
    public int LastColumn { get; set; }
    public string Value { get; set; } = "";
}
```

### 3.3 読み取り時の注意

```
1. セル結合: 結合セルの値は左上セルにのみ存在。
   結合範囲を MergedRegion として記録し、全セルから参照可能にする。

2. 数式: 計算結果（CachedValue）を取得。数式自体は無視。

3. 空行/空列: 連続する空行をブロック区切りとして検出。

4. 非表示シート/行/列: スキップする（テスト仕様として意図されていない）。

5. 画像/図形: 現時点では無視（将来的にOCRで読み取る可能性）。

6. 大規模シート: 1000行 x 50列 を上限とする（LLMコンテキスト制約）。
   超過する場合はブロック分割して処理。
```

---

## 4. Layout Analyzer

### 4.1 目的

Excel のセルデータから構造的特徴を抽出し、LLM への入力を整理する。LLM が解釈しやすい中間表現を生成する。

### 4.2 抽出する構造特徴

```
1. ヘッダー候補行:
   - 太字セルが連続する行
   - 背景色付きセルが連続する行
   - 最初の非空行

2. ブロック境界:
   - 空行で区切られたブロック
   - 罫線パターンの変化
   - セル結合パターンの変化

3. キーバリュー対:
   - "項目名:" + 値 のペア
   - セル結合でラベルが左、値が右

4. テーブル構造:
   - ヘッダー行 + データ行の繰り返し
   - 列数が一定のブロック

5. 階層構造:
   - インデント（先頭スペース）による階層
   - 番号付けパターン（1. 1.1 1.1.1）
   - セル結合による親子関係
```

### 4.3 中間表現（LLMへの入力）

Excelの内容をLLMが理解しやすいMarkdown風のテキスト表現に変換する。

```markdown
## Sheet: "テストケース一覧"

### Block 1 (rows 1-3): Header section
| A | B | C | D | E |
|---|---|---|---|---|
| **テスト仕様書** [merged A1:E1, bold, bg:#4472C4] | | | | |
| 作成日: | 2026-01-15 | | 作成者: | 山田太郎 |
| | | | | |

### Block 2 (rows 4-20): Table section
Header row: 4 [bold, bg:#D9E2F3]
| A | B | C | D | E |
|---|---|---|---|---|
| **No.** | **テストケースID** | **テスト項目** | **手順** | **期待結果** |
| 1 | TC-001 | 顧客検索_名前 | 1. メイン画面の「顧客検索」ボタンをクリック\n2. 検索条件に「田中」と入力\n3. 「検索」ボタンをクリック | 検索結果に「田中太郎」が表示される\n件数が「1件」と表示される |
| 2 | TC-002 | 顧客検索_該当なし | 1. メイン画面の「顧客検索」ボタンをクリック\n2. 検索条件に「存在しない名前」と入力\n3. 「検索」ボタンをクリック | 検索結果が空\n件数が「0件」と表示される |
```

```
表現ルール:
  - 太字は **text** で表記
  - セル結合は [merged A1:C1] で注記
  - 背景色は [bg:#RRGGBB] で注記
  - セル内改行は \n で表記
  - 空セルは空文字
  - ブロック間は空行で区切り
```

---

## 5. LLM Interpreter

### 5.1 プロンプト設計

#### システムプロンプト

```
あなたはテスト仕様書の解析エキスパートです。
Excelベースのテスト仕様書のレイアウト情報を受け取り、
テストケースを構造化JSONに変換します。

出力は以下のJSON Schemaに従ってください:
{schema}

注意事項:
- 手順（steps）は個々の操作に分解してください
- 各手順の action は click, input, select, check, verify のいずれかです
- 期待結果（expected）は検証可能な具体的な記述にしてください
- 前提条件（preconditions）は手順の前に必要な状態を記述してください
- テストケースIDが明示されていない場合は連番で生成してください
- 不明な項目は null としてください
```

#### ユーザープロンプト

```
以下のExcelシートの内容からテストケースを抽出してください。

{layout_analyzer_output}

{hint_content}  ← --hint オプションで追加コンテキスト

{example_content}  ← --example オプションでFew-shot例
```

### 5.2 LLMプロバイダ抽象化

```csharp
public interface ILlmProvider
{
    Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        LlmOptions options,
        CancellationToken ct = default);
}

public class LlmOptions
{
    public string Model { get; set; } = "";
    public double Temperature { get; set; } = 0.0;  // 決定的な出力
    public int MaxTokens { get; set; } = 8192;
    public string? ResponseFormat { get; set; }  // "json" for structured output
}
```

```
プロバイダ実装:
  AnthropicProvider — Claude API (Messages API)
  OpenAiProvider    — OpenAI API (Chat Completions)
  LocalProvider     — ローカルLLM (OpenAI互換API、Ollama等)
```

### 5.3 大規模シートの分割処理

LLMのコンテキストウィンドウに収まらない場合、シートをブロック分割して逐次処理する。

```
分割戦略:
  1. ヘッダーブロック（メタ情報）を共通コンテキストとして保持
  2. テーブルブロックをN行ずつ分割（デフォルト: 50行）
  3. 各チャンクにヘッダーブロック + テーブルヘッダー行 + データ行を含める
  4. 各チャンクの結果をマージし、テストケースIDの重複を排除
```

```
入力:
  ヘッダーブロック（rows 1-3）
  テーブルヘッダー（row 4）
  データ行 50件ずつ

チャンク1: ヘッダー + テーブルヘッダー + rows 5-54
チャンク2: ヘッダー + テーブルヘッダー + rows 55-104
...
→ 各チャンクの TestSpec を結合
```

### 5.4 レイアウトヒントファイル

既知のフォーマットの説明をMarkdownで記述し、LLMの解釈精度を上げる。

```markdown
# テスト仕様書フォーマットヒント

## レイアウト
- 1行目: タイトル（セル結合）
- 2行目: 作成日、作成者
- 4行目: テーブルヘッダー（背景色あり）
- 5行目以降: テストケースデータ

## 列の意味
- A列: 通し番号
- B列: テストケースID（"TC-" プレフィックス）
- C列: テスト項目名
- D列: 手順（改行区切りで複数ステップ）
- E列: 期待結果（改行区切りで複数項目）
- F列: 前提条件（空の場合あり）

## 注意
- 手順の番号は "1." "2." の形式
- 同一テストケースIDで複数行にまたがる場合あり（A列がセル結合）
```

### 5.5 Few-shot例ファイル

```json
{
  "examples": [
    {
      "input": "| No. | テストケースID | テスト項目 | 手順 | 期待結果 |\n|---|---|---|---|---|\n| 1 | TC-001 | ログイン成功 | 1. ユーザー名に\"admin\"を入力\\n2. パスワードに\"pass\"を入力\\n3. ログインボタンをクリック | メイン画面が表示される |",
      "output": {
        "testCases": [
          {
            "id": "TC-001",
            "name": "ログイン成功",
            "preconditions": ["ログイン画面が表示されていること"],
            "steps": [
              {"seq": 1, "action": "input", "target": "ユーザー名", "value": "admin"},
              {"seq": 2, "action": "input", "target": "パスワード", "value": "pass"},
              {"seq": 3, "action": "click", "target": "ログインボタン"}
            ],
            "expected": [
              {"type": "screen_visible", "target": "メイン画面"}
            ]
          }
        ]
      }
    }
  ]
}
```

---

## 6. 出力形式（TestSpec JSON）

### 6.1 JSON Schema

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "required": ["version", "source", "testCases"],
  "properties": {
    "version": {
      "type": "string",
      "const": "1.0"
    },
    "source": {
      "type": "object",
      "properties": {
        "file": { "type": "string" },
        "sheet": { "type": "string" },
        "parsedAt": { "type": "string", "format": "date-time" },
        "llmProvider": { "type": "string" },
        "llmModel": { "type": "string" }
      }
    },
    "metadata": {
      "type": "object",
      "properties": {
        "title": { "type": ["string", "null"] },
        "author": { "type": ["string", "null"] },
        "createdDate": { "type": ["string", "null"] },
        "project": { "type": ["string", "null"] }
      }
    },
    "testCases": {
      "type": "array",
      "items": { "$ref": "#/$defs/testCase" }
    }
  },
  "$defs": {
    "testCase": {
      "type": "object",
      "required": ["id", "name", "steps", "expected"],
      "properties": {
        "id": { "type": "string" },
        "name": { "type": "string" },
        "category": { "type": ["string", "null"] },
        "priority": { "type": ["string", "null"] },
        "preconditions": {
          "type": "array",
          "items": { "type": "string" }
        },
        "steps": {
          "type": "array",
          "items": { "$ref": "#/$defs/testStep" }
        },
        "expected": {
          "type": "array",
          "items": { "$ref": "#/$defs/expectedResult" }
        },
        "notes": { "type": ["string", "null"] },
        "sourceRows": {
          "type": "object",
          "description": "元Excelの行範囲（トレーサビリティ）",
          "properties": {
            "from": { "type": "integer" },
            "to": { "type": "integer" }
          }
        }
      }
    },
    "testStep": {
      "type": "object",
      "required": ["seq", "action", "target"],
      "properties": {
        "seq": { "type": "integer" },
        "action": {
          "type": "string",
          "enum": ["click", "double_click", "right_click",
                   "input", "clear", "select",
                   "check", "uncheck",
                   "key", "drag",
                   "wait", "verify", "navigate"]
        },
        "target": {
          "type": "string",
          "description": "操作対象の自然言語記述"
        },
        "value": {
          "type": ["string", "null"],
          "description": "入力値、選択値など"
        },
        "description": {
          "type": ["string", "null"],
          "description": "元の手順テキスト（原文保持）"
        }
      }
    },
    "expectedResult": {
      "type": "object",
      "required": ["type", "description"],
      "properties": {
        "type": {
          "type": "string",
          "enum": ["text_present", "text_equals", "text_absent",
                   "element_visible", "element_hidden", "element_enabled", "element_disabled",
                   "screen_visible", "screen_transition",
                   "row_count", "value_equals",
                   "message_box", "custom"]
        },
        "target": {
          "type": ["string", "null"],
          "description": "検証対象の自然言語記述"
        },
        "value": {
          "type": ["string", "null"],
          "description": "期待値"
        },
        "description": {
          "type": "string",
          "description": "元の期待結果テキスト（原文保持）"
        }
      }
    }
  }
}
```

### 6.2 出力例

```json
{
  "version": "1.0",
  "source": {
    "file": "customer-test-spec.xlsx",
    "sheet": "テストケース一覧",
    "parsedAt": "2026-02-22T15:00:00Z",
    "llmProvider": "anthropic",
    "llmModel": "claude-sonnet-4-6"
  },
  "metadata": {
    "title": "顧客管理システム テスト仕様書",
    "author": "山田太郎",
    "createdDate": "2026-01-15",
    "project": "CustomerManager"
  },
  "testCases": [
    {
      "id": "TC-001",
      "name": "顧客検索_名前で検索_1件表示",
      "category": "顧客検索",
      "priority": "高",
      "preconditions": [
        "アプリケーションが起動していること",
        "メイン画面が表示されていること",
        "顧客データに「田中太郎」が登録されていること"
      ],
      "steps": [
        {
          "seq": 1,
          "action": "click",
          "target": "顧客検索ボタン",
          "value": null,
          "description": "メイン画面の「顧客検索」ボタンをクリック"
        },
        {
          "seq": 2,
          "action": "input",
          "target": "検索条件",
          "value": "田中",
          "description": "検索条件に「田中」と入力"
        },
        {
          "seq": 3,
          "action": "click",
          "target": "検索ボタン",
          "value": null,
          "description": "「検索」ボタンをクリック"
        }
      ],
      "expected": [
        {
          "type": "text_present",
          "target": "検索結果一覧",
          "value": "田中太郎",
          "description": "検索結果に「田中太郎」が表示される"
        },
        {
          "type": "text_equals",
          "target": "件数表示",
          "value": "1件",
          "description": "件数が「1件」と表示される"
        }
      ],
      "notes": null,
      "sourceRows": { "from": 5, "to": 5 }
    }
  ]
}
```

---

## 7. Validator

### 7.1 検証レベル

```
Level 1 — 構造検証（必須）:
  - JSON Schema に準拠しているか
  - 必須フィールド（id, name, steps, expected）が存在するか
  - steps の seq が連番か
  - action の値が定義済み enum に含まれるか

Level 2 — 整合性検証（警告）:
  - テストケースIDの重複がないか
  - steps が空でないか
  - expected が空でないか
  - input アクションに value が設定されているか
  - click アクションに不要な value がないか

Level 3 — 品質検証（情報）:
  - target の記述が具体的か（「ボタン」だけでは曖昧）
  - preconditions が設定されているか
  - 期待結果の type が description と整合しているか
```

### 7.2 LLM応答のリトライ

```
バリデーション失敗時のフロー:

  1. LLM応答を JSON パース
  2. パース失敗 → エラーメッセージ付きでリトライ
  3. Schema バリデーション失敗 → バリデーションエラー付きでリトライ
  4. リトライ回数超過（--max-retries） → 部分結果を出力 + 警告

リトライプロンプト:
  "前回の出力に以下の問題がありました。修正してください:
   {validation_errors}

   前回の出力:
   {previous_output}"
```

---

## 8. TestSpec と Recording の紐付け

### 8.1 テスト仕様書ステップとRecordingアクションの対応

```
TestSpec の step:
  { "seq": 1, "action": "click", "target": "顧客検索ボタン" }

Recording の action:
  { "seq": 1, "type": "Click", "target": { "automationId": "btnSearch", "name": "検索" } }

紐付け:
  TestSpec.step.target（自然言語）
  ↔ Recording.action.target.name / .automationId（UIA情報）
  → AIエージェントが Page Object 生成時にマッピング
```

### 8.2 wfth-correlate への統合

```bash
# Recording 時にテスト仕様書を指定
wfth-correlate $SESSION/ \
  --spec test-spec.json \
  -o $SESSION/session.json
```

wfth-correlate が `--spec` オプションで TestSpec JSON を受け取り、Recording のアクションとテスト仕様書のステップを時系列で突合する。

```json
{
  "session": { ... },
  "actions": [
    {
      "seq": 1,
      "type": "Click",
      "input": { ... },
      "target": { ... },
      "specStep": {
        "testCaseId": "TC-001",
        "stepSeq": 1,
        "description": "メイン画面の「顧客検索」ボタンをクリック",
        "matchConfidence": 0.9
      }
    }
  ]
}
```

### 8.3 突合アルゴリズム

```
入力:
  - TestSpec: 順序付きステップ列
  - Recording: 順序付きアクション列（ノイズ除去済み）

方式: 順序保持マッチング

  specIndex = 0
  for each action in recording.actions:
    if specIndex >= spec.steps.length:
      break  // 全ステップ突合完了

    currentStep = spec.steps[specIndex]
    similarity = compareSemantic(action, currentStep)

    if similarity > threshold (0.5):
      action.specStep = currentStep
      action.specStep.matchConfidence = similarity
      specIndex++
    else:
      action.specStep = null  // 仕様書に対応しない操作（補助的操作）

意味的類似度の計算:
  1. action.type と step.action の一致（click↔click: 1.0, click↔input: 0.0）
  2. action.target.name と step.target の文字列類似度
  3. action.input.text と step.value の一致
  → 加重平均
```

---

## 9. アーキテクチャ

```
src/WinFormsTestHarness.Parse/
├── WinFormsTestHarness.Parse.csproj
├── Program.cs                        — System.CommandLine エントリポイント
├── Excel/
│   ├── ExcelReader.cs                — ClosedXML でセルデータ抽出
│   └── SheetData.cs                  — セルデータ・書式の中間表現
├── Layout/
│   ├── LayoutAnalyzer.cs             — 構造特徴抽出
│   ├── BlockDetector.cs              — ブロック境界検出
│   └── LayoutRepresentation.cs       — Markdown風中間表現の生成
├── Llm/
│   ├── ILlmProvider.cs               — LLMプロバイダインターフェース
│   ├── AnthropicProvider.cs           — Claude API
│   ├── OpenAiProvider.cs             — OpenAI API
│   ├── LocalProvider.cs              — OpenAI互換ローカルLLM
│   ├── PromptBuilder.cs             — プロンプト構築
│   └── LlmOptions.cs                — LLM設定
├── Models/
│   ├── TestSpec.cs                   — 出力データモデル
│   ├── TestCase.cs                   — テストケース
│   ├── TestStep.cs                   — テストステップ
│   └── ExpectedResult.cs            — 期待結果
├── Validation/
│   ├── SchemaValidator.cs            — JSON Schema 検証
│   ├── ConsistencyChecker.cs         — 整合性検証
│   └── ValidationResult.cs           — 検証結果
└── Parsing/
    ├── ParsePipeline.cs              — パイプライン全体の実行制御
    └── ChunkProcessor.cs             — 大規模シートの分割処理
```

### 9.1 csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>wfth-parse</ToolCommandName>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="ClosedXML" Version="0.102.*" />
    <PackageReference Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />
    <PackageReference Include="NJsonSchema" Version="11.*" />
  </ItemGroup>
</Project>
```

注: `net8.0`（Windows非依存）。Excel解析とLLM呼び出しのみなのでクロスプラットフォーム対応可能。

---

## 10. 制限事項と今後の拡張

### 10.1 現時点の制限

| 制限 | 理由 | 回避策 |
|------|------|--------|
| .xlsx のみ対応 | ClosedXML が .xlsx のみ | .xls は事前変換 |
| 画像/図形は無視 | 構造化困難 | 手動で補完 |
| 1シート1000行上限 | LLMコンテキスト制約 | チャンク分割で対応 |
| LLM依存（オフライン不可） | AI解釈型の設計上不可避 | --hint で精度補完 |
| 多言語未対応 | 日本語フォーマット前提 | プロンプトのローカライズで拡張可能 |

### 10.2 将来の拡張

```
Phase 2 の構想:
  - Word (.docx) 対応
  - PDF 対応（テキスト抽出 + レイアウト解析）
  - Markdown/YAML からの直接変換（LLM不要）
  - テスト仕様書テンプレートの提供（推奨フォーマット）
  - 変換結果のキャッシュ（同じExcelの再解析を省略）
  - VS Code拡張との連携（プレビュー、手動修正UI）
```

---

## 11. 実装優先度

| 機能 | 優先度 | 理由 |
|------|--------|------|
| Excel Reader (ClosedXML) | **MVP** | 入力の基盤 |
| Layout Analyzer (基本) | **MVP** | LLM入力の品質に直結 |
| LLM Interpreter (Anthropic) | **MVP** | コア機能 |
| TestSpec JSON 出力 | **MVP** | 出力の基盤 |
| Schema Validator | **MVP** | 出力品質の保証 |
| --hint オプション | MVP+ | 精度向上に重要 |
| --example (Few-shot) | MVP+ | 精度向上に重要 |
| チャンク分割 | MVP+ | 大規模シート対応 |
| OpenAI / Local プロバイダ | 将来 | Anthropic で十分開始可能 |
| --spec (correlate連携) | 将来 | Recording と仕様書の紐付け |
