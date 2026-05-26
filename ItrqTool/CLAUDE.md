# CLAUDE.md — Architectural governance

Read this file fully before writing any code, creating any file, or adding any NuGet package.
This document is the authoritative source of truth for every structural and convention decision
in this codebase. When in doubt, follow what is written here rather than general .NET conventions.

---

## What this application does

A Windows fat client (Windows 10/11) that automates file-processing workflows.
Each workflow is a Directed Acyclic Graph (DAG) of tasks. Each task reads one or more files,
applies task-specific logic, and writes its output to new files. Those output files feed
into subsequent tasks as inputs.

The user selects a workflow, then executes tasks one at a time, reviewing the structured
output of each task before advancing to the next. There is no automated pipeline execution.

---

## Non-negotiable rules

These rules must never be broken, even when breaking them would be locally convenient.

1. **The dependency rule**: dependencies point inward only.
   `Presentation` → `Application` → `Domain` ← `Infrastructure`
   `Tasks` → `Domain` only.
   `Infrastructure` and `Tasks` must never reference each other.
   `Domain` may reference Microsoft.Extensions.Logging.Abstractions only.
   No other external NuGet packages and no project references. The
   Abstractions package is treated as part of the BCL surface area
   for logging.

2. **ClosedXML must not appear in `ItrqTool.Tasks`.**
   Tasks read Excel files via `IExcelReader` from `ItrqTool.Domain`.
   The ClosedXML NuGet package belongs in `ItrqTool.Infrastructure` only.

3. **Tasks must not throw to signal failure.**
   A task signals failure by returning `new TaskResult(Succeeded: false, messages, elapsed)`.
   Exceptions are for truly unexpected errors (null references, IO access denied, etc.),
   not for business-rule failures such as validation errors or missing rows.

4. **The `TaskType` string in a task class must exactly match the `"type"` field in the
   workflow JSON definition.** They are the coupling point. If they diverge, the task will
   not be found at runtime.

5. **Domain types in Presentation — bindable boundary rule.**
   Domain types must NOT appear in the public/bindable surface of view models or in XAML
   binding paths. They MAY flow through Presentation as private implementation details:
   held in private fields, passed between view models during navigation, used during
   conversion to UI types. The bindable boundary is the rule, not the import statement.

6. **Architecture tests must stay green at all times.**
   Run `dotnet test ItrqTool.Architecture.Tests` before considering any task complete.

---

## Application posture: conservative input handling

This application is a QA tool. Surfacing anomalies in input data is the
primary goal; processing efficiency is secondary. The application is
deployed in a chain involving multiple organisational units, each of
whom may return workbooks with structural or content deviations from
the expected template. These deviations are exactly what the user needs
to see — never what the application should silently smooth over.

Every task that consumes a workbook must:

- Validate sheet existence and surface a clear error if a configured
  sheet is missing.
- Validate that expected columns are populated and surface row-level
  anomalies (missing cells, wrong types, out-of-range values) rather
  than coercing to defaults.
- Surface unexpected structural conditions (extra rows in a section
  range, populated cells outside any defined section, blank cells where
  data is required) as warnings or errors in the task's TaskResult.
- When in doubt between "log a warning and continue" and "stop and
  surface", err toward surfacing. A failed task with a precise message
  is more useful than a successful task with quietly wrong output.

Severity gradient by input source:

- **Auditor-supplied templates** (steps 1–2 of the lifecycle): less
  acute but same default posture. Wrong template structure should be
  caught at the first task that touches it.
- **Organisational-unit responses** (steps 3–6): treated as untrusted.
  Validation is the point.

Severity model for input deviations:

- **Error**: workbook deviates from config expectation (declared
  question rows missing or blank, declared structural cells absent).
  The workbook is the untrusted artifact; this is exactly the QA
  finding the application exists to surface.
- **Warning**: config under-specifies workbook (populated rows
  outside any declared section range). The user authored the config;
  the warning prompts them to check whether to extend ranges or
  intentionally exclude content.
- **Lower priority**: silent defaults that flow visibly into reports
  (empty section names → blank in HTML; null DV/CF metadata → "—"
  in display). Already observable in the report; the gain from
  flagging is incremental.

This posture is upstream of all task design. New tasks default to
strict validation; relaxations require justification documented
inline.

---

## Solution structure

```
ItrqTool.slnx
├── src/
│   ├── ItrqTool.Domain/              No external dependencies whatsoever.
│   ├── ItrqTool.Application/         References: ItrqTool.Domain
│   ├── ItrqTool.Infrastructure/      References: ItrqTool.Domain
│   ├── ItrqTool.Tasks/               References: ItrqTool.Domain
│   └── ItrqTool.Presentation/        References: ItrqTool.Application, ItrqTool.Infrastructure,
│                                                 ItrqTool.Tasks (for DI registration only)
└── tests/
    ├── ItrqTool.Architecture.Tests/  References: all src projects, NetArchTest.Rules, FluentAssertions
    ├── ItrqTool.Domain.Tests/        References: ItrqTool.Domain
    ├── ItrqTool.Application.Tests/   References: ItrqTool.Application, ItrqTool.Domain, NSubstitute
    ├── ItrqTool.Infrastructure.Tests/ References: ItrqTool.Infrastructure,
    │                                              ItrqTool.Domain
    ├── ItrqTool.Integration.Tests/   References: ItrqTool.Presentation
    │                                             (and transitively all src projects)
    │                                             TFM: net10.0-windows
    └── ItrqTool.Tasks.Tests/         References: ItrqTool.Tasks, ItrqTool.Domain, NSubstitute,
                                                 ClosedXML (test fixture creation only)
```

Workflow definition files live in a `/workflows` directory at the solution root.
They are copied to the output directory by the Presentation project's `.csproj`.

---

## Technology stack — exact packages

| Concern | Package | Version | Project |
|---|---|---|---|
| Runtime | .NET | 10 | all |
| UI framework | WPF (built-in) | (built-in) | Presentation |
| MVVM | CommunityToolkit.Mvvm | 8.3.2 | Presentation |
| DI container | Microsoft.Extensions.DependencyInjection | 10.0.8 | Presentation |
| DI assembly scanning | Scrutor | 4.2.2 | Presentation |
| Configuration | Microsoft.Extensions.Configuration + Json | 10.0.8 | Presentation |
| Logging (file + UI) | Serilog + Serilog.Sinks.File + Serilog.Extensions.Logging | 4.0.0 / 6.0.0 / 8.0.0 | Presentation |
| Logging factory and DI integration | Microsoft.Extensions.Logging | 10.0.8 | Presentation |
| Logging abstraction | Microsoft.Extensions.Logging.Abstractions | 10.0.8 | Domain, Application, Tasks |
| Excel I/O | ClosedXML | 0.102.3 | Infrastructure only (Tasks.Tests for fixtures) |
| Testing framework | xUnit | 2.9.2 | all test projects |
| Mocking | NSubstitute | 5.3.0 | Application.Tests, Tasks.Tests |
| Assertions | FluentAssertions | 6.12.2 | all test projects |
| Architecture tests | NetArchTest.Rules | 1.3.2 | Architecture.Tests |

Do not add NuGet packages not listed here without documenting the reason in this file.

---

## Domain model — canonical type definitions

These types live in `ItrqTool.Domain`. Do not alter their signatures without updating this file.

```csharp
// ── Task contract ──────────────────────────────────────────────────────────────

public interface IWorkflowTask
{
    string TaskType { get; }
    Task<TaskResult> ExecuteAsync(TaskExecutionContext context, CancellationToken ct);
}

public record TaskExecutionContext(
    string TaskId,
    IReadOnlyDictionary<string, string> InputPaths,    // logical key → resolved absolute path
    IReadOnlyDictionary<string, string> OutputPaths,   // logical key → resolved absolute path
    ILogger Logger,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Parameters   // static config from workflow JSON node
);

// ── Task result ────────────────────────────────────────────────────────────────

public record TaskResult(
    bool Succeeded,
    IReadOnlyList<TaskMessage> Messages,
    TimeSpan Duration
);

public record TaskMessage(
    MessageSeverity Severity,
    string Text,
    DateTimeOffset Timestamp
);

public enum MessageSeverity { Info, Warning, Error }

// ── Workflow graph ─────────────────────────────────────────────────────────────

public record WorkflowDefinition(
    string Id,
    string Name,
    string? Group,                    // optional; null if absent or null in JSON.
                                      // If absent and id contains ':', derived = full id
                                      // (e.g. id "A:B:task" → Group "A:B:task").
                                      // If absent and id has no ':': null (→ "Ungrouped").
    IReadOnlyList<TaskNode> Nodes
);

public record TaskNode(
    string Id,
    string TaskType,
    IReadOnlyDictionary<string, TaskOutputRef> Inputs,       // localKey → (upstreamTaskId, outputKey)
    IReadOnlyDictionary<string, string> OutputFileNames,     // logicalKey → filename
    IReadOnlyDictionary<string, string> Parameters           // static config values, case-insensitive
);

public record TaskOutputRef(string TaskId, string OutputKey);

// ── Excel I/O ──────────────────────────────────────────────────────────────────

public interface IExcelReader
{
    IReadOnlyList<string> GetSheetNames(string filePath);
    ExcelSheet ReadSheet(string filePath, string sheetName, bool firstRowIsHeader = true);
    IReadOnlyList<ExcelSheet> ReadAllSheets(string filePath, bool firstRowIsHeader = true);
}

public record ExcelSheet(
    string Name,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<ExcelCellValue>> Rows
);

public record ExcelCellValue(object? Value, Type? ClrType)
{
    public T? As<T>() => Value is T v ? v : default;
}

public interface IExcelWriter
{
    void WriteWorkbook(ExcelWorkbookData data, string filePath);
}

public record ExcelWorkbookData(IReadOnlyList<ExcelSheetData> Sheets);

public record ExcelSheetData(
    string Name,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows
);

/// <summary>
/// Reads raw cell content and Excel structural metadata (data
/// validation type, conditional formatting operator) that
/// IExcelReader does not expose.
/// </summary>
public interface IExcelStructureReader
{
    IReadOnlyList<ExcelRowStructure> ReadRows(string filePath, string sheetName);
}

public record ExcelRowStructure(
    int RowNumber,
    // Key = uppercase column letter, e.g. "C", "D"
    IReadOnlyDictionary<string, ExcelCellStructure> CellsByColumn
);

public record ExcelCellStructure(
    string? TextValue,
    // Name of the ClosedXML XLAllowedValues enum value,
    // or null if no data validation is applied to this cell.
    string? DataValidationType,
    // Raw formula string from the DV rule (dv.Value from ClosedXML).
    // Inline lists: e.g. "\"Yes,No,N/A\"". Range refs: e.g. "Sheet!$A$1:$A$5".
    // Null if no DV rule applies.
    string? DataValidationFormula,
    // Name of the ClosedXML XLCFOperator enum value for the first matching
    // conditional format, or null if no conditional format applies.
    string? ConditionalFormattingOperator
);

// ── Reporting ─────────────────────────────────────────────────────────────────

// Namespace: ItrqTool.Domain.Reporting
// Files: src/ItrqTool.Domain/Reporting/

public record HtmlDiffReportData(
    string Title,
    string PreviousWorkbookPath,
    string CurrentWorkbookPath,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<HtmlDiffQuestion>           Added,
    IReadOnlyList<HtmlDiffQuestion>           Removed,
    IReadOnlyList<HtmlDiffChangedQuestion>    Changed,
    IReadOnlyList<HtmlDiffUnchangedQuestion>  Unchanged
);

public record HtmlDiffQuestion(
    string? QuestionNumber,
    string  Chapter,
    string  Section,
    int     RowNumber,          // row in the sheet the question came from
    string  QuestionText,
    string? DvType,
    string? CfOperator
);

public record HtmlDiffChangedQuestion(
    string  Chapter,
    string  Section,
    int     PreviousRowNumber,
    int     CurrentRowNumber,
    string? PreviousNumber,
    string? CurrentNumber,
    string  OldText,
    string  NewText,
    double  SimilarityScore,
    double? SecondBestSimilarity,
    string  OldDvDisplay,       // "—" if no DV
    string  NewDvDisplay,
    string? OldCfOperator,
    string? NewCfOperator,
    bool    TextChanged,
    bool    NumberChanged,
    bool    DvChanged,
    bool    CfChanged,          // false when old DvType == "List" (presentational noise)
    string? OldExplanation     = null,   // explanation prompt from the previous-year question
    string? NewExplanation     = null,   // explanation prompt from the current-year question
    bool    ExplanationChanged = false   // true when old/new explanations differ; triggers diff block in writer
);

public record HtmlDiffUnchangedQuestion(
    string  Chapter,
    string  Section,
    int     PreviousRowNumber,
    int     CurrentRowNumber,
    string? QuestionNumber,
    string  QuestionText,
    string  DvDisplay,          // formatted: "—", type name, or "List: A | B | C"
    string? CfOperator,
    double  SimilarityScore,    // always 1.0
    double? SecondBestSimilarity
);

public interface IHtmlReportWriter
{
    /// Generates a self-contained HTML report and writes it to filePath.
    /// Overwrites if the file already exists. Creates the directory if needed.
    void WriteReport(HtmlDiffReportData data, string filePath);
}

**Sheet-order tabs**: two additional tabs ("Current sheet", "Previous sheet")
render every question of the respective workbook in sheet-row order, with a
status badge per entry (added/changed/unchanged on the current side;
removed/changed/unchanged on the previous side), inline section/chapter
separator rows, and click-to-expand detail cards. Entry data is derived in
JS at init time from the existing `added`/`removed`/`changed`/`unchanged`
arrays; no new JSON fields. Filter behaviour: separators always visible,
detail rows track their parent entry's filter state.

// ── Workflow loading ───────────────────────────────────────────────────────────

public interface IWorkflowLoader
{
    WorkflowLoadResult LoadAll();
}

public record WorkflowLoadResult(
    IReadOnlyList<WorkflowDefinition> Workflows,
    IReadOnlyList<WorkflowLoadFailure> Failures
);

public record WorkflowLoadFailure(string FilePath, string ErrorMessage);
```

---

## Workflow definition format (JSON)

Files live in `/workflows/*.json`. The loader reads all files in that directory at startup.

```json
{
  "id": "invoice-processing",
  "name": "Invoice Processing Workflow",
  "tasks": [
    {
      "id": "extract",
      "type": "ExtractInvoiceData",
      "inputs": {},
      "outputs": { "data": "extracted_data.csv" }
    },
    {
      "id": "validate",
      "type": "ValidateInvoice",
      "inputs": { "data": "extract.data" },
      "outputs": { "report": "validation_report.json" }
    },
    {
      "id": "enrich",
      "type": "EnrichWithSupplierData",
      "inputs": { "data": "extract.data" },
      "outputs": { "enriched": "enriched_data.csv" }
    },
    {
      "id": "generate",
      "type": "GenerateOutput",
      "inputs": {
        "validated": "validate.report",
        "enriched":  "enrich.enriched"
      },
      "outputs": { "result": "final_output.xlsx" }
    }
  ]
}
```

**Input reference syntax:** `"taskId.outputKey"` — the loader splits on the first `.`.
A task with no inputs declares `"inputs": {}`.
Circular references and missing upstream keys are detected at load time and throw
`WorkflowDefinitionException` before any task runs.

**Hierarchical workflow IDs and group derivation:**
A workflow `id` may contain `:` separators (e.g. `"ITRQ RefYear 2025:Stage 0:task"`).
Each segment must be non-empty and must not contain `/` or `\`.

`JsonWorkflowLoader` derives `Group` from `id` when the JSON `"group"` field is absent or `null`:
- id contains `:` → derived Group = **full id** (all segments form the group hierarchy).
  Example: id `"A:B:task"` → Group `"A:B:task"` → TreeView renders A → B → task → leaf.
- id has no `:` → derived Group = `null` (workflow appears under "Ungrouped").
- Explicit `"group"` field always wins; derivation is skipped.

**Leaf label fallback:** `WorkflowListViewModel` uses `WorkflowDefinition.Name` as the
TreeView leaf label. If `Name` is null or whitespace, the label falls back to the last
segment of `id` (i.e. `id.Split(':').Last()`).

---

## Execution model — WorkflowSession

`WorkflowSession` lives in `ItrqTool.Application`. It manages all state for a single
workflow run. The user advances manually — there is no automatic execution loop.

```
WorkflowSessionStatus values:
  ReadyToRun      → a task is queued; the Run button is enabled
  Running         → current task is executing; the Run button is disabled
  AwaitingReview  → last task completed; user is reading results before advancing
  Completed       → all tasks finished successfully
  Failed          → a task returned Succeeded = false; no further advancement possible
```

**Execution sequence (per task):**
1. Resolve `InputPaths` from the completed tasks' recorded `OutputPaths`.
2. Resolve `OutputPaths` into the workflow's working directory (`<workflowDataRoot>/<workflow.Id>`).
3. Call `task.ExecuteAsync(context, ct)`.
4. If `result.Succeeded` is false → set status to `Failed`, stop.
5. Record the result. Advance `CurrentIndex`. Set status to `AwaitingReview` or `Completed`.

Each workflow has a dedicated, persistent working directory at
`<workflowDataRoot>/<workflow.Id>`. `workflowDataRoot` is configured
in appsettings.json under `ItrqTool:WorkflowDataRoot`, supports
Windows environment-variable expansion, and falls back to
`%USERPROFILE%\Documents\ItrqTool` if missing or empty.
`WorkflowSessionFactory.Create` computes the directory; `WorkflowSession`
exposes it via the `WorkingDirectory` property. The directory is
NOT created in the factory.

Files persist between tasks within a session. On the first task
execution of a fresh session, `WorkflowSession` wipes the directory's
contents before running the task — stale files from a prior run
never bleed into a new one. Subsequent task executions within the
same session do NOT wipe.

The application does NOT clean up working directories on close or
startup. Users grab their deliverables from the workflow's folder
whenever they choose.

If the wipe fails (e.g. an output file is locked by Excel), the
session sets `Status = Failed` and returns a `TaskResult` with one
`Error` message describing the wipe failure. The internal "first
execution" flag flips only on a successful wipe, so the user can
close Excel and re-run.

---

## Task authoring checklist

Follow these steps exactly when adding a new task type.

**Step 1 — Create the class in `ItrqTool.Tasks`:**

```csharp
// Namespace: ItrqTool.Tasks
// File: Tasks/{TaskType}Task.cs

public sealed class MyNewTask : IWorkflowTask
{
    // Inject only interfaces from ItrqTool.Domain (e.g. IExcelReader).
    // Never inject ClosedXML types, Infrastructure types, or Presentation types.
    public MyNewTask(/* domain interfaces only */) { }

    public string TaskType => "MyNew";  // ← must match "type" field in JSON exactly

    public async Task<TaskResult> ExecuteAsync(TaskExecutionContext ctx, CancellationToken ct)
    {
        var messages = new List<TaskMessage>();
        var sw = Stopwatch.StartNew();

        try
        {
            // Use ctx.InputPaths["key"] to get resolved input file paths.
            // Use ctx.OutputPaths["key"] to get resolved output file paths.
            // Log progress via messages, not ctx.Logger (ctx.Logger is for infra-level events).

            messages.Add(new(MessageSeverity.Info, "Started processing", DateTimeOffset.Now));

            // ... business logic ...

            return new TaskResult(Succeeded: true, messages, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw; // always rethrow cancellation
        }
        catch (Exception ex)
        {
            messages.Add(new(MessageSeverity.Error, ex.Message, DateTimeOffset.Now));
            return new TaskResult(Succeeded: false, messages, sw.Elapsed);
        }
    }
}
```

**Step 2 — Add the workflow JSON entry** in `/workflows/{workflow}.json`.
The `"type"` value must equal the `TaskType` property string.

**Step 3 — Write tests** in `ItrqTool.Tasks.Tests`.
At minimum: success path, failure path (bad input), cancellation respected.

**Step 4 — Verify architecture tests still pass:**
```
dotnet test tests/ItrqTool.Architecture.Tests
```

No manual DI registration is needed. Scrutor scans `ItrqTool.Tasks` on startup
and registers all `IWorkflowTask` implementations automatically.

---

## Excel reading

Tasks that process xlsx files inject `IExcelReader` from `ItrqTool.Domain`.

```csharp
public sealed class MyExcelTask : IWorkflowTask
{
    private readonly IExcelReader _excel;

    public MyExcelTask(IExcelReader excel) => _excel = excel;

    public string TaskType => "MyExcel";

    public async Task<TaskResult> ExecuteAsync(TaskExecutionContext ctx, CancellationToken ct)
    {
        var messages = new List<TaskMessage>();
        var sw = Stopwatch.StartNew();

        var path = ctx.InputPaths["source"];
        var sheets = _excel.GetSheetNames(path);
        messages.Add(new(MessageSeverity.Info, $"Found sheets: {string.Join(", ", sheets)}", DateTimeOffset.Now));

        var sheet = _excel.ReadSheet(path, sheets[0]);
        messages.Add(new(MessageSeverity.Info, $"Read {sheet.Rows.Count} rows from '{sheet.Name}'", DateTimeOffset.Now));

        // Access typed values:
        // sheet.Headers            → IReadOnlyList<string>
        // sheet.Rows[r][c].As<double>()
        // sheet.Rows[r][c].As<DateTime>()
        // sheet.Rows[r][c].As<string>()
        // sheet.Rows[r][c].Value   → raw object?, null if blank

        return new TaskResult(Succeeded: true, messages, sw.Elapsed);
    }
}
```

**Never reference ClosedXML in `ItrqTool.Tasks`.** This will be caught by the architecture tests.

---

## ControlLevelQuestionDiffTask — CLQ config file format

`ControlLevelQuestionDiffTask` (TaskType `"ControlLevelQuestionDiff"`) accepts four task parameters:

| Parameter | Description |
|---|---|
| `previousWorkbookFullFilename` | Absolute path to the previous-year auditor-questionnaire workbook |
| `currentWorkbookFullFilename` | Absolute path to the current-year auditor-questionnaire workbook |
| `previousConfigurationFullFilename` | Absolute path to the CLQ config JSON for the previous workbook |
| `currentConfigurationFullFilename` | Absolute path to the CLQ config JSON for the current workbook |

Each config file is deserialized independently and applied only to its own workbook, allowing
the two workbooks to have different sheet structures (e.g. across audit years).

The config describes the structure of the "Control Level Questions" sheet in a workbook.

**AuditQuestion** (in `ItrqTool.Tasks.ControlLevelQuestionDiff`):

```csharp
public sealed record AuditQuestion(
    string  ChapterName,
    string  SectionName,
    string  QuestionText,      // prefix stripped
    string  OriginalText,      // raw cell text
    string? QuestionNumber,    // e.g. "1.2", null if no prefix present
    int     RowNumber,
    string? DvType,
    string? DvFormula,         // raw DV formula from ExcelCellStructure.DataValidationFormula
    string? CfOperator
);
// AuditQuestion.ExtractNumber("1.2) text") → "1.2"; no prefix → null
// AuditQuestion.StripPrefix("1.2) text")   → "text"
```

**DiffResult / ChangedQuestion** (in `ItrqTool.Tasks.ControlLevelQuestionDiff`):

A matched question pair is either Changed or Unchanged — there is no separate ValidationChange
category. CF changes are ignored when the old DvType is "List" (presentational noise on dropdowns).

```csharp
public sealed record ChangedQuestion(
    AuditQuestion OldQuestion,
    AuditQuestion NewQuestion,
    double  SimilarityScore,
    double? SecondBestSimilarity,
    bool    TextChanged,        // SimilarityScore < 1.0
    bool    NumberChanged,
    bool    DvChanged,
    bool    CfChanged           // false when old DvType == "List"
);

public sealed record UnchangedQuestion(
    AuditQuestion Question,
    double? SecondBestSimilarity
);

public sealed record DiffResult(
    IReadOnlyList<AddedQuestion>      Added,
    IReadOnlyList<RemovedQuestion>    Removed,
    IReadOnlyList<ChangedQuestion>    Changed,
    IReadOnlyList<UnchangedQuestion>  Unchanged
);
```

**ControlLevelQuestionsConfig** (in `ItrqTool.Tasks.ControlLevelQuestionDiff`):

```csharp
public sealed record SectionDefinition(int SectionRow, int FirstQuestionRow, int LastQuestionRow);

public sealed class ControlLevelQuestionsConfig
{
    public string SheetName { get; init; } = "Control Level Questions";
    public string TextColumn { get; init; } = "C";
    public string InputColumn { get; init; } = "D";
    public IReadOnlyList<int> ChapterRows { get; init; } = [];
    // Each entry: "<sectionRow>:<firstQuestionRow>-<lastQuestionRow>"
    public IReadOnlyList<string> SectionRows { get; init; } = [];
    // ParsedSections parses SectionRows; throws FormatException on invalid entries
    public IReadOnlyList<SectionDefinition> ParsedSections { get; }
}
```

**SectionRows format:** Each string is `"<sectionRow>:<firstQuestionRow>-<lastQuestionRow>"`.
All numbers are 1-based Excel row numbers. Constraints:
- `sectionRow` must be a positive integer.
- `firstQuestionRow` must be greater than `sectionRow`.
- `lastQuestionRow` must be ≥ `firstQuestionRow`.

Rows not covered by any section range or chapter row are silently skipped during parsing.
`ParsedSections` throws `FormatException` on the first invalid entry; `ControlLevelQuestionDiffTask`
catches it and returns `Succeeded: false` with the error message.

**Example CLQ config file:**

```json
{
  "sheetName": "Control Level Questions",
  "textColumn": "C",
  "inputColumn": "D",
  "chapterRows": [1, 15, 30],
  "sectionRows": ["2:3-14", "16:17-29", "31:32-50"]
}
```

---

### RiskLevelQuestionDiffTask

The second diff task. Compares the Risk Level Questions sheet between
two reference years. Structurally similar to CLQ but with deliberate
divergences.

- **Task type**: `RiskLevelQuestionDiff`
- **Namespace**: `ItrqTool.Tasks.RiskLevelQuestionDiff`
- **Default sheet name**: `"Risk Level Questions"`
- **Default output filename**: `risk-level-question-diff.html`
- **Default report title**: `"Risk Level Questions Diff Report"`

#### Records

- `RiskLevelQuestion` — `(SectionName, QuestionText, ExplanationPrompt,
  QuestionNumber, RowNumber, DvType, DvFormula, CfOperator)`. No
  `ChapterName` (RLQ has sections only). No `OriginalText` (number is
  in its own column, no prefix to strip).
- `RiskLevelQuestionsConfig` — `SheetName`, `NumberColumn` (default
  `"B"`), `TextColumn` (default `"C"`), `AnswerColumn` (default `"D"`),
  `ExplanationColumn` (default `"E"`), `SectionRows`, computed
  `ParsedSections`. No `ChapterRows`.
- Result records `AddedQuestion`, `RemovedQuestion`, `ChangedQuestion`,
  `UnchangedQuestion`, `DiffResult` live in the same namespace as
  siblings to CLQ's. `ChangedQuestion` has an extra `ExplanationChanged`
  flag compared to CLQ's.

#### Configuration file format

```json
{
  "sheetName": "Risk Level Questions",
  "numberColumn": "B",
  "textColumn": "C",
  "answerColumn": "D",
  "explanationColumn": "E",
  "sectionRows": ["3:4-15", "16:17-42"]
}
```

`sectionRows` uses the same `"<sectionRow>:<firstQuestionRow>-<lastQuestionRow>"`
format as CLQ. Throws `FormatException` on invalid entries (propagates
through `ExecuteAsync`'s outer catch as a `TaskResult.Succeeded: false`).

#### Parser specifics

- Question number read directly from column B. No regex. Trimmed string
  stored as-is; `null` if cell missing or whitespace.
- Question text from column C (no prefix strip).
- Explanation prompt from column E.
- DV/CF metadata from column D, same `IExcelStructureReader` contract
  as CLQ.
- Section name read from column C on the row indicated by
  `SectionDefinition.SectionRow`.

#### Diff engine

`ItrqTool.Tasks.RiskLevelQuestionDiff.QuestionDiffEngine.Diff(prev, cur)`.
Mirrors CLQ's engine with one structural difference: an
`explanationChanged` flag is computed alongside `textChanged`,
`numberChanged`, `dvChanged`, `cfChanged`, and flows into the result's
`ChangedQuestion`.

The matching matrix uses `QuestionText` only with the existing
+0.10 section-match and +0.10 number-match contextual bonuses.
**`ExplanationPrompt` does NOT participate in the matching matrix.**
This is a deliberate decision: keeping the matching surface narrow
preserves the "reported similarity is base text similarity"
invariant. If a future failure mode shows that explanation similarity
would have disambiguated text-similar questions, adding an
`ExplanationBonus` is a one-line matrix-construction change.

**Matching surface narrowness — `ExplanationPrompt` in RLQ.** The RLQ
diff engine's matching matrix uses `QuestionText` only, with the same
section/number contextual bonuses as CLQ. `ExplanationPrompt` (the
second text field per row in the RLQ schema) does NOT participate in
matching; the `ExplanationChanged` flag is computed post-match from a
separate `TextSimilarity.Score` call on the matched pair's
explanation strings. Rationale: keeps the "reported similarity is
base question-text similarity" invariant clean, and avoids the
question of whether explanation similarity should affect the reported
`SimilarityScore` (it should not). Symmetric extensions are reserved
for the matching matrix only; secondary text fields stay outside it.

#### Parameters

Five, same shape as CLQ:

- `previousWorkbookFullFilename` (required)
- `currentWorkbookFullFilename` (required)
- `previousConfigurationFullFilename` (required)
- `currentConfigurationFullFilename` (required)
- `reportTitle` (optional, defaults to "Risk Level Questions Diff Report")

#### Workflow JSON

Placeholder at `workflows/risk-level-question-diff.json`. Same shape
as CLQ's workflow JSON with the task type, output filename, and
placeholder parameter paths adapted for RLQ.

---

## General Data Diff

The third diff task. Compares the General Data sheet between two reference years; produces an interactive HTML report. Unlike CLQ and RLQ (one question per sheet row), a General Data question can span multiple sheet rows with a variable number of template-label cells across columns D/E/F per row and an explanation cell in column G.

- **Default sheet name**: `"General Data"`
- **Default columns**: B=question number, C=text/section header, D/E/F=answer template labels, G=explanation prompt
- **Default report title**: `"General Data Diff Report"` (Phase 2)
- **Matching**: question-text-based with section bonus, mirroring RLQ; question numbers are display-only.

### Workflow JSON

```json
{
  "id": "general-data-diff",
  "name": "General Data Diff",
  "tasks": [
    {
      "id": "diff-report",
      "type": "GeneralDataDiff",
      "inputs": {},
      "outputs": { "report": "general-data-diff.html" },
      "parameters": {
        "previousWorkbookFullFilename": "<path to previous year's workbook>",
        "currentWorkbookFullFilename": "<path to current year's workbook>",
        "previousConfigurationFullFilename": "<path to previous year's general-data-structure.json>",
        "currentConfigurationFullFilename": "<path to current year's general-data-structure.json>"
      }
    }
  ]
}
```

### Structure JSON (per-year, runtime input)

```json
{
  "sheetName": "General Data",
  "numberColumn": "B",
  "textColumn": "C",
  "answerColumns": ["D", "E", "F"],
  "explanationColumn": "G",
  "sectionRows": [
    "13:14(1), 15(1), 16(1), 17(1), 18(3), 21(2)",
    "26:27(4), 31(1)"
  ]
}
```

Each `sectionRows` entry has the format `"<sectionRow>:<startRow>(<rowspan>), <startRow>(<rowspan>), ..."` where `rowspan` is inclusive of the start row (so `18(3)` spans rows 18, 19, 20).

### Canonical Domain types

```csharp
public sealed record GeneralDataAnswerCell(
    int RowOffset, string Column, string Text,
    string? DvType, string? DvFormula, string? CfOperator);

public sealed record GeneralDataExplanationCell(
    int RowOffset, string Text,
    string? DvType, string? DvFormula, string? CfOperator);

public sealed record GeneralDataQuestion(
    string SectionName,
    string QuestionText,
    string? QuestionNumber,
    int RowNumber,
    IReadOnlyList<string> RowNumberLabels,
    IReadOnlyList<GeneralDataAnswerCell> AnswerCells,
    IReadOnlyList<GeneralDataExplanationCell> ExplanationCells);
```

### Config and parser

`GeneralDataConfig` (in `ItrqTool.Tasks.GeneralDataDiff`) parses its `SectionRows` strings into `IReadOnlyList<SectionDefinition>` via the `ParsedSections` derived property. `GeneralDataQuestionParser.Parse(rows, config)` is a public static method that walks each section's enumerated questions and produces the list of `GeneralDataQuestion`. Cell inclusion: a D/E/F or G cell joins its list iff `TextValue` is non-empty after trimming (DV/CF on otherwise empty cells does NOT trigger inclusion). Question text comes from column C of the question's first row only; continuation rows' column C is ignored.

Architectural note: General Data deviates from the RLQ pattern by exposing the parser as a standalone public static class (`GeneralDataQuestionParser`) rather than embedding it as a private static method on the task class. Rationale: enables independent testing of the parser before the task orchestrator (Phase 2) exists. Phase 2's `GeneralDataDiffTask` will call `GeneralDataQuestionParser.Parse` directly.

The "third-sibling abstraction trigger" did NOT fire at this point: General Data's multi-row question structure is sufficiently different from CLQ/RLQ's one-row-per-question model that sharing parser code would force accidental coupling. Re-evaluate at the fourth sheet (Risk Level Exposure).

---

## Presentation layer conventions

- Framework: WPF on .NET 10. Target `net10.0-windows`.
- Pattern: MVVM using CommunityToolkit.Mvvm source generators.
- Use `[ObservableProperty]` for bindable properties. Use `[RelayCommand]` for commands.
- ViewModels are in `ItrqTool.Presentation/ViewModels/`.
- Views (XAML) are in `ItrqTool.Presentation/Views/`.

**UI model records** (never expose domain types to the view layer):

```csharp
// Bindable surrogate for WorkflowDefinition in list views
public record WorkflowListItem(string Id, string Name);

// Bindable projection of a WorkflowLoadFailure for the list-view banner
public record WorkflowLoadFailureItem(string FileName, string ErrorMessage);
// FileName is the file name only (no directory path); ErrorMessage forwarded verbatim.

// One row in the task list panel
public record TaskRowItem(
    string TaskId,
    string DisplayName,
    TaskRowStatus Status,    // Pending, Ready, Running, Completed, Failed
    string? Duration         // null until completed, e.g. "1.2s"
);

public enum TaskRowStatus { Pending, Ready, Running, Completed, Failed }

// The right-hand result panel
public record TaskResultViewModel(
    string TaskName,
    bool Succeeded,
    string Duration,
    IReadOnlyList<TaskMessageViewModel> Messages
);

// Presentation-side mirror of ItrqTool.Domain.MessageSeverity — keeps Domain out of XAML bindings
public enum TaskMessageSeverity { Info, Warning, Error }

public record TaskMessageViewModel(
    TaskMessageSeverity Severity,   // Info, Warning, Error
    string Text,
    string Timestamp                // formatted for display
);

// One row in the live in-app log panel
// Category is the full source context ("ItrqTool.Tasks.NoOpTask");
// ShortCategory is the last segment ("NoOpTask").
public record LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string ShortCategory,
    string Message);
```

**WorkflowRunViewModel responsibilities:**

`WorkflowRunViewModel` owns the `WorkflowSession` lifecycle for one selected workflow.
On `InitializeFor(definition)`, it asks the `WorkflowSessionFactory` for a session, then
projects the session's topological order into the bindable `Tasks` collection (one
`TaskRowItem` per node).

Each `TaskRowItem` reflects a per-row status derived from the session:
- `i < session.CurrentIndex` → `Completed`
- `i == session.CurrentIndex`, Status = Running → `Running`
- `i == session.CurrentIndex`, Status = Failed → `Failed`
- `i == session.CurrentIndex`, Status = ReadyToRun/AwaitingReview → `Ready`
- `i > session.CurrentIndex` → `Pending`

`RunTaskCommand` awaits `session.RunCurrentTaskAsync(CancellationToken.None)`, then
rebuilds `Tasks` from the new session state, sets `SelectedResult` to the just-completed
task's result, and sets `SelectedTask` to that row.

`SelectedTask` is two-way bound to the ListBox SelectedItem. When the user clicks a
different row, `SelectedResult` is updated to that row's historical `TaskResult` (via
`session.GetResult(index)`) — or null if the row has not yet been run.

`RunButtonLabel` mirrors `session.Status`:
- `ReadyToRun` → `"Run first task"`
- `AwaitingReview` → `"Run next task"`
- `Running` → `"Running…"`
- `Completed` → `"Workflow completed"`
- `Failed` → `"Workflow failed"`

Empty workflows (no tasks): `RunButtonLabel = "No tasks to run"`, `CanRun = false`.

`BackCommand` is disabled while `session.Status == Running`.

`OpenWorkingFolderCommand` launches the workflow's working directory in Windows Explorer
(via `explorer.exe <path>`). It is enabled iff a session has been initialized (`_session
is not null`). If the directory does not yet exist on disk (no task has run, so the lazy
wipe-or-create hasn't fired), the command creates it before launching Explorer — opening
an empty folder is a reasonable affordance and lets the user drop files there manually
before a task that consumes them. Explorer launch failures are caught and swallowed
non-fatally.

The user can click any completed task row to set `SelectedResult` to that task's
historical result. Clicking a pending or ready task sets `SelectedResult` to null.

The DI registrations are extracted into static methods in `ItrqTool.Presentation`:

```
AddItrqToolServices(this IServiceCollection services,
                    string workflowsDirectoryPath,
                    string workflowDataRoot)
```

App.OnStartup builds IConfiguration from appsettings.json, resolves paths with
`Environment.ExpandEnvironmentVariables`, configures `Log.Logger` (Serilog), and
calls `AddItrqToolServices`. Integration tests also call AddItrqToolServices with
a per-test temp workflows directory and a per-test workflowDataRoot.
Treat `AddItrqToolServices` as the single source of truth for the production object
graph — never duplicate registrations elsewhere.

**Navigation and shell:**

The application uses a shell pattern. `ShellViewModel` is registered as a singleton and
is the DataContext of `MainWindow`. It exposes a single bindable property `CurrentViewModel`
(typed as `ObservableObject`). `MainWindow.xaml` hosts a `ContentControl` bound to
`CurrentViewModel`, with `<DataTemplate>`s in `<Window.Resources>` mapping each child VM
type to its `UserControl`.

Navigation is event-based. `WorkflowListViewModel` raises `WorkflowSelected(WorkflowDefinition)`;
`WorkflowRunViewModel` raises `BackRequested()`. `ShellViewModel` subscribes to both in its
constructor and updates `CurrentViewModel` accordingly. No messenger or navigation service.

On entering the list view, the shell calls `WorkflowListViewModel.Load()` so the list
reflects the current state of disk. On entering the run view, the shell calls
`WorkflowRunViewModel.InitializeFor(definition)`.

**WorkflowListViewModel** exposes loaded workflows AND load failures from
`IWorkflowLoader.LoadAll()`. Failures are projected into bindable
`WorkflowLoadFailureItem` records (file name only, error message verbatim from
`WorkflowLoadFailure.ErrorMessage`). The list view shows a banner above the workflow
list whenever `Failures.Count > 0`; the banner is collapsible via `ShowFailureDetails`
(toggled by `ToggleFailureDetailsCommand`). `ShowFailureDetails` resets to false on
every `Load()` call.

---

## Logging

Microsoft.Extensions.Logging is the standard logging API throughout the app — every
layer logs through `ILogger<T>`. The composition root registers two providers:

- **UiLogSinkProvider** (`ItrqTool.Presentation.Logging`) — pushes events into an
  observable in-memory `UiLogSink` bound to the run-view log panel.
- **Serilog** (`Presentation`) — writes a rolling daily file under
  `%USERPROFILE%\Documents\ItrqTool\logs` by default; configurable via
  `ItrqTool:LogsDirectory` in appsettings.json. Used for crash/postmortem forensics.

Both providers receive every log event MEL dispatches. Minimum level: Information.
Debug and Trace events are suppressed.

The UI sink is cleared on every `WorkflowRunViewModel.InitializeFor`, so each
workflow run starts with a fresh in-app log. The file log is NOT cleared — it
accumulates over the day and rolls at midnight; 14 days of files are retained.

File output template:
```
{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}
```

Tasks SHOULD inject `ILogger<TTask>` and use it for diagnostic progress messages
(Information for normal steps, Warning for recoverable issues, Error for failures).
`TaskResult.Messages` SHOULD be reserved for curated outcome summaries shown in the
result panel after the task completes — typically 1–3 messages.

---

## Testing requirements

### Architecture tests (`ItrqTool.Architecture.Tests`)
Must cover all five dependency rules listed in the Non-negotiable rules section.
Use `NetArchTest.Rules` with `FluentAssertions`.

### Domain tests (`ItrqTool.Domain.Tests`)
- `WorkflowGraph`: topological sort correctness, cycle detection (must throw), 
  missing input reference detection (must throw), single-node graph, 
  disconnected multi-root graph.
- `ExcelCellValue.As<T>()`: type resolution for all supported CLR types.

### Application tests (`ItrqTool.Application.Tests`)
- `WorkflowSession`: all status transitions, input path resolution from upstream outputs,
  output path placement in working directory, hard stop on `Succeeded = false`,
  cancellation propagation, `Completed` state after last task succeeds,
  wipe-on-first-execution (pre-populated stale files deleted before first task's output written).
- Wipe-on-first-execution: pre-populate the working directory with stale files; assert they
  are deleted before the first task's output is written.

### Infrastructure tests (`ItrqTool.Infrastructure.Tests`)
- `JsonWorkflowLoader`: empty directory, valid file with empty task list,
  valid file with tasks, malformed JSON, missing required field, workflow
  with cycle, workflow with missing reference, mix of valid and invalid
  files, non-JSON files ignored, directory does not exist (throws
  DirectoryNotFoundException).

### Task tests (`ItrqTool.Tasks.Tests`)
Each task implementation must have:
- A success path test with a representative input file (use temp files).
- A failure path test (malformed input, missing expected column, etc.).
- A cancellation test verifying `OperationCanceledException` propagates.

Implemented tasks with test coverage:
- `ControlLevelQuestionDiffTask` (TaskType `"ControlLevelQuestionDiff"`) — compares the
  Control Level Questions sheet between two reference years; produces an interactive HTML
  diff report.
- `RiskLevelQuestionDiffTask` (TaskType `"RiskLevelQuestionDiff"`) — compares the Risk
  Level Questions sheet between two reference years; produces the same interactive HTML
  report shape as CLQ with the addition of an explanation diff block per changed question.

**Current test counts (as of RLQ phase-F merge):**
Architecture 14, Domain 13, Application 12, Tasks 185, Infrastructure 58, Integration 40
— **322 total**.

### Integration tests (`ItrqTool.Integration.Tests`)
- Full end-to-end execution: writes `smoketest.json` into a temp workflows
  directory, calls `CompositionRoot.AddItrqToolServices`, builds the
  ServiceProvider, resolves `IWorkflowLoader` and `WorkflowSessionFactory`,
  loads the workflow, runs every task through to `Completed`, asserts
  every declared output file exists and the recorded `TaskResult`s are
  all `Succeeded`.
- DI smoke: asserts every `IWorkflowTask` discovered by Scrutor is
  resolvable through `ITaskRegistry.FindTask(task.TaskType)`.
- Second session run: run a workflow to completion, write a stale file into the
  working directory, create a fresh session for the same workflow, run one task,
  assert the stale file is gone and the first output file is freshly written.

---

## DI registration (composition root in `ItrqTool.Presentation`)

```csharp
// App.xaml.cs — configure Log.Logger (Serilog) BEFORE calling AddItrqToolServices.
// Then:
services.AddItrqToolServices(workflowsDirectoryPath, workflowDataRoot);

// Inside AddItrqToolServices (single-param implementation):
services.AddLogging(b => { b.SetMinimumLevel(LogLevel.Information); b.AddSerilog(Log.Logger, dispose: false); });
services.AddSingleton<IUiLogSink>(_ => new UiLogSink(Application.Current?.Dispatcher));
services.AddSingleton<ILoggerProvider>(sp => new UiLogSinkProvider(sp.GetRequiredService<IUiLogSink>()));

services.AddSingleton<IExcelReader, ClosedXmlExcelReader>();
services.AddSingleton<IWorkflowLoader, JsonWorkflowLoader>(...);

// Tasks — automatic registration via Scrutor
services.Scan(scan => scan
    .FromAssemblyOf<IWorkflowTaskMarker>()
    .AddClasses(c => c.AssignableTo<IWorkflowTask>())
    .AsImplementedInterfaces()
    .WithTransientLifetime());

services.AddSingleton<ITaskRegistry, DependencyInjectionTaskRegistry>();
services.AddSingleton<WorkflowSessionFactory>(sp =>
    new WorkflowSessionFactory(
        workflowDataRoot,
        sp.GetRequiredService<ITaskRegistry>(),
        sp.GetRequiredService<ILogger<WorkflowSession>>()));

services.AddSingleton<WorkflowListViewModel>();
services.AddSingleton<WorkflowRunViewModel>();    // DI resolves (WorkflowSessionFactory, IUiLogSink)
services.AddSingleton<ShellViewModel>();
services.AddSingleton<MainWindow>();
```

`IWorkflowTaskMarker` is an empty marker interface in `ItrqTool.Tasks` used solely
to give Scrutor an assembly anchor. It has no members.

---

## Adding new capabilities — checklists

### Adding a new task type
1. Create `{Name}Task.cs` in `ItrqTool.Tasks/Tasks/`.
2. Implement `IWorkflowTask`. Set `TaskType` to the exact string.
3. Add the task entry to the relevant workflow JSON in `/workflows/`.
4. Write tests in `ItrqTool.Tasks.Tests`.
5. Run architecture tests.

### Adding a new workflow
1. Create `{name}.json` in `/workflows/`.
2. All referenced `"type"` values must match existing task `TaskType` strings.
3. Verify the JSON loads without error by running the application.
4. No code changes required.

### Adding a new infrastructure capability (e.g. CSV reading)
1. Define the interface in `ItrqTool.Domain`.
2. Implement it in `ItrqTool.Infrastructure` using whatever NuGet package is appropriate.
   Add the package only to `ItrqTool.Infrastructure`.
3. Register the implementation in the composition root.
4. Update this file: add the package to the technology stack table, document the interface.

### Changing a domain type signature
1. Update the canonical definition in this file first.
2. Then update the code.
3. Architecture tests and unit tests will identify all call sites that need updating.

---

## Deployment

The application is published as a framework-dependent, single-file
executable for win-x64. Target machines must have
Microsoft.WindowsDesktop.App 10.0.x installed (verified on all
intended deployment machines).

Publish configuration:
  - Target framework: net10.0-windows
  - Runtime identifier: win-x64
  - Self-contained: false (framework-dependent)
  - PublishSingleFile: true
  - EnableCompressionInSingleFile: false (compression requires self-contained;
    not supported for framework-dependent single-file builds — NETSDK1176)
  - PublishReadyToRun: true
  - DebugType: embedded (PDB embedded in the single-file exe)
  - PublishTrimmed: false (trimming disabled — WPF, Scrutor reflection
    scanning, System.Text.Json reflection, and CommunityToolkit.Mvvm
    source generators are not safe to trim)

Publish output layout (under `publish/` at the repo root):

    publish/
      ItrqTool.exe          single-file, PDB embedded
      appsettings.json
      workflows/            empty — deployed users start with no workflows
      README.txt            quick-start guide generated by publish.ps1

The `publish/` directory is .gitignore'd. After publishing, the
contents of that directory are zipped and handed to the user. The
user extracts the zip anywhere they have write access and runs the
exe directly — no installer, no admin rights.

Publishing is driven by `publish.ps1` at the repo root, which invokes
`dotnet publish` with the canonical flags, stages `appsettings.json`
alongside the exe, wipes the published `workflows/` directory clean
(developer-side workflow JSONs are reference-only and never ship to
end users), and writes a `README.txt` quick-start guide for end users.
Never invoke `dotnet publish` manually for a release build; always go
through `publish.ps1` so the output is reproducible.

Runtime paths on a deployed install:
  - `AppContext.BaseDirectory` resolves to the directory containing
    ItrqTool.exe. This is where the app looks for `appsettings.json`
    and the `workflows/` subdirectory.
  - Working data and logs live under the paths configured in
    appsettings.json (defaults: %USERPROFILE%\Documents\ItrqTool
    and %USERPROFILE%\Documents\ItrqTool\logs). These persist across
    app installs and upgrades.

---

## Maintaining this file

Update `CLAUDE.md` whenever:
- A new domain type is introduced or an existing one changes signature.
- A new NuGet package is added to any project.
- A new project is added to the solution.
- A new convention is established (e.g. a new naming rule, a new file location).
- A rule is intentionally relaxed or tightened.

`CLAUDE.md` is read at the start of every session. If it is out of date,
subsequent sessions will drift from the established architecture.

Per-developer artifacts not in version control:
  - `.claude/settings.local.json` — Claude Code's per-machine permission
    state. Accumulates as new tool permissions are granted; review it
    directly on the developer's machine when auditing what Claude Code
    can do. Not tracked in git.
