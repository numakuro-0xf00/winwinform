# Logger ユニットテスト レビュー

**レビュアー**: t_wada (和田卓人)
**レビュー日**: 2026-02-23
**対象ファイル**:
- `tests/WinFormsTestHarness.Tests/Logger/LogEntryTests.cs`
- `tests/WinFormsTestHarness.Tests/Logger/LogPipelineTests.cs`
- `tests/WinFormsTestHarness.Tests/Logger/ControlInfoTests.cs`
- `tests/WinFormsTestHarness.Tests/Logger/JsonFileLogSinkTests.cs`
- `tests/WinFormsTestHarness.Tests/Logger/PreciseTimestampTests.cs`
- `tests/WinFormsTestHarness.Tests/Logger/LoggerConfigTests.cs`

---

## 総評 (Overall Assessment)

全体として、テストの方向性は正しく、Logger プロジェクトの主要な振る舞いをカバーしようとする意図が明確に感じられます。NUnit の `Assert.That` + Constraint Model を一貫して使用している点、テスト名が日本語で振る舞いを記述している点、`InMemorySink` というテストダブルで外部依存を分離している点は良い判断です。

しかしながら、いくつかの重要な問題があります。特に **(1) LogPipelineTests のキュー溢れテストにおけるアサーションの甘さ**、**(2) JsonFileLogSinkTests のファイルシステム依存テストにおけるクリーンアップの不確実性**、**(3) PreciseTimestampTests の非決定性**、そして **(4) テストスイート全体で欠落しているシナリオの多さ** が目立ちます。

Gerard Meszaros の『xUnit Test Patterns』が繰り返し警告する「Fragile Test」と「Erratic Test」のリスクが、現在のテストスイートにはいくつか潜んでいます。テストは「動く仕様書」であるべきで、テストが失敗したときに開発者が迷わず原因を特定できることが重要です。

---

## 良い点 (Strengths)

### 1. Constraint Model の一貫した使用
全テストファイルで `Assert.That(..., Is.EqualTo(...))` 形式を使っており、`Assert.AreEqual` のような旧来のメソッドを避けています。これは NUnit のベストプラクティスに沿っており、失敗メッセージの可読性が高くなります。

### 2. テストダブルの設計が適切 (LogPipelineTests)
`InMemorySink` は `ILogSink` を実装し、書き込み失敗のシミュレーションも可能です。テスト対象システム (SUT) の外部依存をインメモリに置き換える「Fake Object」パターンの好例です。`ConcurrentBag<LogEntry>` の選択もスレッドセーフな `LogPipeline` のテストに適しています。

### 3. JSON シリアライズのラウンドトリップ検証 (LogEntryTests)
`LogEntry` のファクトリメソッドで生成したオブジェクトを JSON にシリアライズし、`JsonDocument` で構造を検証するアプローチは、NDJSON IPC フォーマットの正しさを保証するうえで非常に有効です。null フィールドの省略確認まで行っている点も素晴らしいです。

### 4. フォールバック動作のテスト (LogPipelineTests)
Primary Sink 失敗時の Fallback Sink への切り替え、両 Sink 失敗時の No-Throw 保証というクリティカルパスがテストされています。ロガーの「ホストアプリをクラッシュさせない」という設計方針がテストで表現されている点は高く評価できます。

### 5. テスト名の記述性
日本語でのテスト名は、このプロジェクトのコンテキストで適切です。`PropertyChanged_マスク有効時に値がマスクされる` のように、「何が」「どういう条件で」「どうなるか」が読み取れるテスト名が多く、テストが仕様書として機能しています。

---

## 改善提案 (Improvement Suggestions)

### [Critical] 1. LogPipelineTests: キュー溢れテストのアサーションが仕様を表現していない

**場所**: `tests/WinFormsTestHarness.Tests/Logger/LogPipelineTests.cs` -- `キュー溢れ時に古いエントリが破棄される()` (L67-85)

**問題**: L79 のアサーション `Assert.That(pipeline.QueueCount, Is.LessThanOrEqualTo(5))` は常に成功するトートロジーです。5個投入して「5以下である」と検証しても何も保証しません。このテストの意図は「maxQueueSize=3 なので最大3個しか保持しない」ことの検証のはずです。

**理由**: テストのアサーションは、テスト名が約束する振る舞いを正確に検証しなければなりません。現状では、キュー溢れロジックが完全に壊れていてもこのテストは通ります。これは「Tautological Test（トートロジカルテスト）」の一種です。

**提案**:
```csharp
[Test]
public void キュー溢れ時に古いエントリが破棄される()
{
    var primary = new InMemorySink();
    var fallback = new InMemorySink();
    using var pipeline = new LogPipeline(primary, fallback, maxQueueSize: 3, flushIntervalMs: int.MaxValue);

    for (int i = 0; i < 5; i++)
    {
        pipeline.Enqueue(LogEntry.Custom($"msg{i}", TestTimestamp));
    }

    // maxQueueSize=3 なので、キューには最大3エントリしか保持されない
    Assert.That(pipeline.QueueCount, Is.LessThanOrEqualTo(3),
        "キューサイズが maxQueueSize を超えてはならない");

    pipeline.FlushQueue();

    Assert.That(primary.Entries.Count, Is.EqualTo(3),
        "maxQueueSize 分のエントリのみが書き込まれるべき");
}
```

さらに、**どのエントリが残っているか**（古いものが捨てられ、新しいものが残るのか）も検証すべきです。しかし `ConcurrentBag` は順序を保証しないため、`InMemorySink` の `Entries` を `ConcurrentQueue` に変更することを推奨します（後述）。

---

### [Critical] 2. LogPipelineTests: InMemorySink の ConcurrentBag は順序検証に不適切

**場所**: `tests/WinFormsTestHarness.Tests/Logger/LogPipelineTests.cs` -- `InMemorySink` クラス (L16-32)

**問題**: テスト名 `Enqueue_複数エントリが順序通りフラッシュされる` (L51) は「順序通り」を謳っていますが、`ConcurrentBag<LogEntry>` は順序を保証しない LIFO ベースのコレクションです。このテストは件数しか検証しておらず、テスト名が約束する「順序」を実際には検証していません。

**理由**: テスト名とアサーションが乖離すると、テストの仕様書としての価値が失われます。また、将来誰かが「順序を検証しよう」と `ConcurrentBag` の列挙順に依存するアサーションを追加すると、非決定的に失敗する Erratic Test になります。

**提案**:
```csharp
// InMemorySink 内のコレクションを変更
public ConcurrentQueue<LogEntry> Entries { get; } = new();

// Write メソッドも対応
public void Write(LogEntry entry)
{
    if (_failOnWrite) throw new IOException("Simulated write failure");
    Entries.Enqueue(entry);
}

// 順序検証テスト
[Test]
public void Enqueue_複数エントリが投入順にフラッシュされる()
{
    var primary = new InMemorySink();
    var fallback = new InMemorySink();
    using var pipeline = new LogPipeline(primary, fallback, maxQueueSize: 100, flushIntervalMs: int.MaxValue);

    for (int i = 0; i < 5; i++)
    {
        pipeline.Enqueue(LogEntry.Custom($"msg{i}", TestTimestamp));
    }
    pipeline.FlushQueue();

    var messages = primary.Entries.Select(e => e.Message).ToList();
    Assert.That(messages, Is.EqualTo(new[] { "msg0", "msg1", "msg2", "msg3", "msg4" }),
        "エントリは投入順にフラッシュされるべき");
}
```

---

### [High] 3. JsonFileLogSinkTests: デフォルトパステストがファイルを後始末していない

**場所**: `tests/WinFormsTestHarness.Tests/Logger/JsonFileLogSinkTests.cs` -- `デフォルトパス_指定なしの場合自動生成される()` (L81-88)

**問題**: `new JsonFileLogSink(null, ...)` は `%TEMP%/WinFormsTestHarness/logs/` に実際のファイルを作成しますが、このテストは `TearDown` で管理される `_testDir` とは無関係なディレクトリにファイルを残します。繰り返しテストを実行するとゴミファイルが蓄積されます。

**理由**: テストは副作用を残してはなりません。テストの独立性（Test Independence）を損ない、他のテストやシステムに影響を与える可能性があります。

**提案**:
```csharp
[Test]
public void デフォルトパス_指定なしの場合自動生成される()
{
    using var sink = new JsonFileLogSink(null, maxFileSize: 1024 * 1024);

    Assert.That(sink.IsConnected, Is.True);
    sink.Write(LogEntry.Custom("auto_path_test", TestTimestamp));

    // テスト後にデフォルトパスのディレクトリを削除
    // => ただし JsonFileLogSink が CurrentFilePath を公開していない問題がある
}
```

理想的には、`JsonFileLogSink` にファイルパスを取得するプロパティを公開するか（テスタビリティ向上）、あるいはこのテストでは環境変数やファイルパス生成ロジックを直接テストする形に変えるべきです。現状ではテスト終了後に自動生成パスのディレクトリを特定できないため、クリーンアップが不可能です。

---

### [High] 4. PreciseTimestampTests: 時間依存テストの非決定性リスク

**場所**: `tests/WinFormsTestHarness.Tests/Logger/PreciseTimestampTests.cs` -- `Now_現在時刻に近い値を返す()` (L35-47)

**問題**: `DateTime.UtcNow` との差が1秒以内であることを検証していますが、CI 環境（特に負荷の高い共有ランナー）でプロセスがスケジューリング遅延を受けると、1秒を超える可能性があります。これは典型的な Erratic Test（非決定的テスト）です。

**理由**: テストは「いつ、どこで、何回実行しても同じ結果を返す」べきです（Deterministic Test 原則）。時刻に依存するテストは、許容誤差を十分に大きくするか、時刻の注入（Clock injection）パターンを使うべきです。

**提案**:
```csharp
[Test]
public void Now_現在時刻に近い値を返す()
{
    var before = DateTime.UtcNow;
    var ts = new PreciseTimestamp();
    var result = ts.Now();
    var after = DateTime.UtcNow;

    var parsed = DateTime.ParseExact(result, "yyyy-MM-ddTHH:mm:ss.ffffffZ",
        System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);

    // before <= parsed <= after + margin の範囲内であることを確認
    Assert.That(parsed, Is.GreaterThanOrEqualTo(before.AddSeconds(-1)),
        "タイムスタンプは計測開始前の時刻より後であるべき");
    Assert.That(parsed, Is.LessThanOrEqualTo(after.AddSeconds(1)),
        "タイムスタンプは計測終了後の時刻 + マージン以内であるべき");
}
```

また、許容誤差を5秒程度に広げることも CI の安定性のためには現実的な選択です。

---

### [High] 5. LogPipelineTests: Dispose 後の二重 Dispose 安全性テストが欠落

**場所**: `tests/WinFormsTestHarness.Tests/Logger/LogPipelineTests.cs`

**問題**: `Dispose時に残りキューがフラッシュされSinkがDisposeされる` テストは良いですが、`Dispose()` を2回呼んでも安全であることのテストがありません。`IDisposable` を実装するクラスは二重 Dispose に対して安全であるべきです（.NET のガイドライン）。

**理由**: `using` ステートメントと明示的 `Dispose()` が混在するコードでは二重 Dispose が発生し得ます。特に `LogPipeline.Dispose()` 内では `_timer.Dispose()` を呼んでおり、二度呼ばれた場合に `FlushQueue()` が再実行される可能性もあります。

**提案**:
```csharp
[Test]
public void Dispose_二重呼び出しでも例外がスローされない()
{
    var primary = new InMemorySink();
    var fallback = new InMemorySink();
    var pipeline = new LogPipeline(primary, fallback, maxQueueSize: 100, flushIntervalMs: int.MaxValue);

    pipeline.Dispose();
    Assert.DoesNotThrow(() => pipeline.Dispose());
}
```

---

### [Medium] 6. LogEntryTests: JsonSerializerOptions がテストとプロダクションで二重定義

**場所**: `tests/WinFormsTestHarness.Tests/Logger/LogEntryTests.cs` (L11-14) および `src/WinFormsTestHarness.Logger/Sinks/JsonFileLogSink.cs` (L13-16)

**問題**: テスト側で `s_jsonOptions` を独自に定義しています。もしプロダクションコード側の `JsonSerializerOptions` に将来変更が加えられた場合（例: `PropertyNamingPolicy` の追加、カスタムコンバーターの追加）、テスト側のオプションと乖離し、テストが実態と異なるシリアライズ結果を検証してしまいます。

**理由**: テストは「プロダクションコードと同じ条件」で検証すべきです。テスト側で独自の設定を持つと、テストが通っていてもプロダクション環境では異なる振る舞いをする可能性があります。

**提案**: プロダクションコード側の `JsonSerializerOptions` を internal static で公開し、テストから参照できるようにする。

```csharp
// JsonFileLogSink.cs（または共通の場所）
internal static readonly JsonSerializerOptions JsonOptions = new()
{
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
};

// テスト側
private static readonly JsonSerializerOptions s_jsonOptions = JsonFileLogSink.JsonOptions;
```

あるいは、`LogEntry` クラス自体に `ToJson()` メソッドを持たせるのも一案です。

---

### [Medium] 7. LogEntryTests: Sanitize のテストで境界値500文字ちょうどのケースが欠落

**場所**: `tests/WinFormsTestHarness.Tests/Logger/LogEntryTests.cs` -- Sanitize 関連テスト

**問題**: 600文字（超過ケース）と "hello"（5文字、短いケース）のみテストされています。境界値である500文字ちょうどのケースと501文字のケースがありません。

**理由**: 境界値分析はテスト設計の基本です。`str.Length > 500` という条件を検証するには、500文字と501文字のテストが不可欠です。

**提案**:
```csharp
[Test]
public void Sanitize_500文字ちょうどはトランケートされない()
{
    var str500 = new string('a', 500);
    var result = LogEntry.Sanitize(str500) as string;
    Assert.That(result, Is.EqualTo(str500));
    Assert.That(result!.Length, Is.EqualTo(500));
}

[Test]
public void Sanitize_501文字はトランケートされる()
{
    var str501 = new string('a', 501);
    var result = LogEntry.Sanitize(str501) as string;
    Assert.That(result!.Length, Is.EqualTo(503)); // 500 + "..."
    Assert.That(result, Does.EndWith("..."));
}
```

---

### [Medium] 8. LogEntryTests: Sanitize で ToString() が null を返すオブジェクトのケース

**場所**: `tests/WinFormsTestHarness.Tests/Logger/LogEntryTests.cs` および `src/WinFormsTestHarness.Logger/Models/LogEntry.cs` (L128-132)

**問題**: `Sanitize` メソッドでは `value.ToString()` を呼び出し、その結果が null の場合もあり得ます（`ToString()` は null を返すことが許容されている）。しかしこのケースがテストされていません。

**理由**: プロダクションコードで `str != null && str.Length > 500` と null チェックしていることからも、著者は `ToString()` が null を返す可能性を認識しています。認識された分岐はテストで表現すべきです。

**提案**:
```csharp
private sealed class NullToStringObject
{
    public override string? ToString() => null;
}

[Test]
public void Sanitize_ToStringがnullを返すオブジェクトではnullを返す()
{
    var result = LogEntry.Sanitize(new NullToStringObject());
    Assert.That(result, Is.Null);
}
```

---

### [Medium] 9. JsonFileLogSinkTests: ファイルローテーションテストのアサーションが弱い

**場所**: `tests/WinFormsTestHarness.Tests/Logger/JsonFileLogSinkTests.cs` -- `ファイルローテーション_最大サイズ超過で新ファイルに切り替わる()` (L52-65)

**問題**: `Assert.That(files.Length, Is.GreaterThanOrEqualTo(2))` は「2以上」としか検証していません。ローテーション後のファイル名のフォーマット (`rotate.1.ndjson`) や、各ファイルの内容が正しいかは検証されていません。

**理由**: ファイルローテーションはデータロストのリスクがある機能です。「ローテーションされた」という事実だけでなく、「データが失われていない」ことの検証が重要です。

**提案**:
```csharp
[Test]
public void ファイルローテーション_最大サイズ超過で新ファイルに切り替わる()
{
    var filePath = Path.Combine(_testDir, "rotate.ndjson");
    using (var sink = new JsonFileLogSink(filePath, maxFileSize: 50))
    {
        sink.Write(LogEntry.Custom("first", TestTimestamp));
        sink.Write(LogEntry.Custom("second", TestTimestamp));
    }

    var files = Directory.GetFiles(_testDir, "rotate*.ndjson").OrderBy(f => f).ToArray();
    Assert.That(files.Length, Is.EqualTo(2), "元ファイルとローテーションファイルの2つが存在すべき");

    // 元ファイルに最初のエントリが書かれていることを確認
    var firstFileContent = File.ReadAllLines(files[0]);
    Assert.That(firstFileContent.Length, Is.GreaterThanOrEqualTo(1));

    // 全ファイルのエントリ合計が投入数と一致 = データロストなし
    var totalLines = files.Sum(f => File.ReadAllLines(f).Length);
    Assert.That(totalLines, Is.EqualTo(2), "ローテーション後もデータが失われてはならない");
}
```

---

### [Medium] 10. ControlInfoTests: ControlInfo.Name に空文字列を渡した場合のテストが欠落

**場所**: `tests/WinFormsTestHarness.Tests/Logger/ControlInfoTests.cs`

**問題**: `ControlInfo` コンストラクタは `name` パラメータに対してバリデーションを行っていません。空文字列や null が渡された場合の振る舞いが定義されておらず、テストもありません。一方、`ControlInfo.FromControl` は `string.IsNullOrEmpty` で名前なしコントロールにフォールバック名を付与しています。

**理由**: コンストラクタに直接空文字列を渡すパスと、`FromControl` 経由のパスで振る舞いが異なる可能性があります。コンストラクタのバリデーション有無は設計判断ですが、テストでその期待値を明確にすべきです。

**提案**:
```csharp
[Test]
public void コンストラクタ_名前が空文字列でも正常に動作する()
{
    var info = new ControlInfo("", "Button", "MainForm", false);
    Assert.That(info.Name, Is.EqualTo(""));
}

[Test]
public void コンストラクタ_名前がnullでも正常に動作する()
{
    // null を許容するならテストで明示する。
    // 許容しないなら ArgumentNullException のテストにする。
    var info = new ControlInfo(null!, "Button", "MainForm", false);
    Assert.That(info.Name, Is.Null);
}
```

---

### [Low] 11. LoggerConfigTests: Default プロパティが毎回新しいインスタンスを返すことの検証

**場所**: `tests/WinFormsTestHarness.Tests/Logger/LoggerConfigTests.cs`

**問題**: `LoggerConfig.Default` は `new()` を返していますが、もし将来キャッシュされたシングルトンに変更された場合、あるテストでの変更が別のテストに影響を及ぼします。現在の実装が毎回新しいインスタンスを返すことを保証するテストがありません。

**提案**:
```csharp
[Test]
public void Default_呼び出しごとに新しいインスタンスを返す()
{
    var config1 = LoggerConfig.Default;
    var config2 = LoggerConfig.Default;
    Assert.That(config1, Is.Not.SameAs(config2));
}
```

---

### [Low] 12. テスト全体: AAA パターンの視覚的分離

**場所**: 全テストファイル

**問題**: 一部のテストで Arrange / Act / Assert の間に空行がなく、3つのフェーズの境界が視覚的に不明瞭です。

**理由**: AAA パターンの空行分離は、テストの可読性における基本プラクティスです。特に Arrange が複数行にわたる場合、Act との境界が曖昧になります。

**提案**: 全テストで Arrange / Act / Assert の間に空行を入れ、必要に応じて `// Arrange`, `// Act`, `// Assert` コメントを追加する。特に `LogPipelineTests.Enqueue_FlushQueue_Sinkに書き込まれる` (L35-48) はモデル的に分離されていて良いですが、`LogEntryTests.EventEntry_イベントログのJSON形式が正しい` (L19-32) では Arrange(L21-22), Act(L24-25), Assert(L27-31) の分離がやや曖昧です。

---

## 不足しているテスト (Missing Tests)

### 優先度: 高

1. **LogPipeline: Dispose 後の Enqueue/FlushQueue 呼び出し**
   - Dispose 後に `Enqueue` や `FlushQueue` を呼んだ場合に例外が出ないこと（No-Throw 保証の一部）。現在の実装では `_timer.Dispose()` 後も `FlushQueue` が呼べてしまう。

2. **LogPipeline: Timer によるバックグラウンドフラッシュ**
   - `flushIntervalMs` に実際の小さい値を設定し、手動で `FlushQueue` を呼ばなくてもタイマーでフラッシュが実行されることを確認するテスト。現在のテストはすべて `flushIntervalMs: int.MaxValue` でタイマーを無効化しているため、タイマー統合の検証が皆無。

3. **LogPipeline: PrimarySink が途中で復帰するシナリオ**
   - Primary Sink が一時的に失敗した後、復帰した場合に正しく Primary に戻るか。実装を見ると、毎回 Primary を先に試みるため問題ないはずだが、テストで保証すべき。

4. **JsonFileLogSink: Write 後の Dispose でデータが失われないこと**
   - `AutoFlush = true` なので問題ないはずだが、フラッシュ保証は重要な振る舞いであり、テストで明示すべき。

5. **JsonFileLogSink: Dispose 後の Write 呼び出し**
   - Dispose 後に `Write` を呼んだ場合の振る舞い。現在の実装では `_writer == null` チェックで無視されるが、テストで明示すべき。

6. **LogEntry: PropertyChanged で masked=false かつ ControlInfo.IsPasswordField=true の場合**
   - `PropertyChanged` の `masked` パラメータと `ControlInfo.IsPasswordField` は独立しています。テスト内の `PropertyChanged_マスク有効時に値がマスクされる` では両方が `true` のケースしかテストしていません。`masked` パラメータの値のみがマスキングを制御するという振る舞いを明示的にテストすべきです。

### 優先度: 中

7. **IpcLogSink のテスト**
   - `IpcLogSink` には一切テストがありません。名前付きパイプの接続は外部依存が大きいですが、`ResolvePipeName` のロジック（環境変数からの PID 解決、パイプ名フォーマット）は純粋なロジックであり、テスト可能です。テスタビリティのためにメソッドを static internal にして直接テストすることを検討してください。

8. **PasswordDetector のテスト**
   - `PasswordDetector.IsPasswordField` は純粋なロジックに近いですが、WinForms の `TextBox` インスタンスが必要です。E2E テスト環境であればテスト可能なはずです。

9. **LoggerConfig: 不正値（負数、ゼロ）のバリデーション**
   - `MaxQueueSize` に 0 や負数を設定した場合の振る舞いが未定義・未テスト。`FlushIntervalMs` に 0 を設定した場合も同様。

10. **JsonFileLogSink: 非常に長い JSON エントリがファイルサイズ計算に正しく反映されるか**
    - `_currentFileSize += json.Length + Environment.NewLine.Length` はバイト数ではなく文字数で計算しています。マルチバイト文字（日本語メッセージ）を含む場合、実際のファイルサイズと乖離する可能性があります。これはバグの可能性もあるため、テストで現在の振る舞いを固定しておくべきです。

### 優先度: 低

11. **LogEntry: EventEntry に null の ControlInfo を渡した場合**
    - 現在の実装では NullReferenceException が発生しますが、テストで期待される例外を明示すべきです。

12. **LogPipeline: 大量エントリの同時 Enqueue（マルチスレッドテスト）**
    - `ConcurrentQueue` を使っているためスレッドセーフなはずですが、`_queueCount` の `Interlocked` 操作と `TryDequeue` の間にレースコンディションの可能性があります（Enqueue の L35-43 参照）。これは並行テストで検証すべきです。

---

## 総括

テストスイートの骨格は良好で、主要なハッピーパスとフォールバック動作がカバーされています。しかし、**アサーションの精度**（特にキュー溢れテスト）、**テストデータ構造の選択**（ConcurrentBag vs ConcurrentQueue）、**境界値テストの不足**、そして **時間依存テストの非決定性** は早期に改善すべき課題です。

「テストは最も重要な設計ドキュメントである」という原則に立ち返ると、現在のテストスイートから読み取れない仕様が多すぎます。特に IpcLogSink のテストが皆無であることは、Logger プロジェクトの中核機能（IPC 出力）の仕様がテストで保護されていないことを意味します。

まずは Critical と High の改善提案から着手し、その後 Missing Tests の優先度高の項目を追加していくことを推奨します。

---

> 「テストを書く習慣のないプログラマはプロフェッショナルではない。同様に、アサーションの精度に無頓着なテストは、仕様書としての価値を持たない。」 -- t_wada
