# CLAUDE.md — Architectural governance

Read this file fully before writing any code, creating any file, or adding any NuGet package.
This document is the authoritative source of truth for every structural and convention decision
in this codebase. When in doubt, follow what is written here rather than general .NET conventions.

This file holds always-on governance: hard constraints, architecture overview, conventions,
build/test commands, and the test-count baseline. Task-specific reference (per-sheet diff specs,
publishing) lives in on-demand **skills** under `.claude/skills/<name>/SKILL.md` — see
"Per-sheet specifics" below and "Maintaining this file" for the layering rule.

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

**Detect-everything (diff tasks).** The questionnaire diff tasks are *maximal
change-detectors, not noise-reducers.* Every detected delta is surfaced:
question-text and number changes, the full data-validation rule (type, operator,
and both values), and the full conditional-formatting rule (type, operator, and
both values). Blanket CF additions, wholesale renumbering, operator-only changes,
second-value changes — all are reported. Muting "presentational noise" is
explicitly **not** a goal; CF on List/dropdown cells is compared like any other
(the former List-CF mute has been removed). When a delta is ambiguous, surface it.

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
| OPC/OOXML packaging (ClosedXML dependency) | System.IO.Packaging | 10.0.7 | Infrastructure (Tasks.Tests for fixtures) |
| Testing framework | xUnit | 2.9.2 | all test projects |
| Mocking | NSubstitute | 5.3.0 | Application.Tests, Tasks.Tests |
| Assertions | FluentAssertions | 6.12.2 | all test projects |
| Architecture tests | NetArchTest.Rules | 1.3.2 | Architecture.Tests |

Do not add NuGet packages not listed here without documenting the reason in this file.

---

## Domain model

The **core task contract** every task touches is kept inline here. Set the
`TaskType` string to exactly match the workflow JSON `"type"` (non-negotiable rule 4).

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
```

### Other Domain types — source is authoritative

For every other Domain type, the source under `src/ItrqTool.Domain/` is the
authoritative definition of signatures. Do not maintain a second copy here; read
the file. Index of type family → location:

| Type family | Source file(s) |
|---|---|
| Workflow graph: `WorkflowDefinition`, `TaskNode`, `TaskOutputRef` | `WorkflowDefinition.cs`, `TaskNode.cs`, `TaskOutputRef.cs` |
| Workflow loading: `IWorkflowLoader`, `WorkflowLoadResult`, `WorkflowLoadFailure` | `IWorkflowLoader.cs`, `WorkflowLoadResult.cs`, `WorkflowLoadFailure.cs` |
| Excel I/O: `IExcelReader`, `ExcelSheet`, `ExcelCellValue`, `IExcelWriter`, `ExcelWorkbookData`, `ExcelSheetData` | `IExcelReader.cs`, `ExcelSheet.cs`, `ExcelCellValue.cs`, `IExcelWriter.cs` |
| Excel structure metadata: `IExcelStructureReader`, `ExcelRowStructure`, `ExcelCellStructure` (DV type/formula/operator/formula2, CF operator/type/value/value2) | `IExcelStructureReader.cs` |
| CLQ/RLQ reporting: `HtmlDiffReportData`, `HtmlDiffQuestion`, `HtmlDiffChangedQuestion`, `HtmlDiffUnchangedQuestion`, `IHtmlReportWriter` | `Reporting/HtmlDiffReportData.cs`, `Reporting/IHtmlReportWriter.cs` |
| General Data reporting: `HtmlDiffGeneralDataReportData` + `HtmlDiffGeneralData*` members, `IHtmlGeneralDataDiffReportWriter` | `Reporting/HtmlDiffGeneralDataReportData.cs`, `Reporting/IHtmlGeneralDataDiffReportWriter.cs` |
| Cell-range reporting: `HtmlDiffCellRangeReportData`, `HtmlDiffCellRangeChangedCell`, `HtmlDiffCellRangeUnchangedCell`, `IHtmlCellRangeDiffReportWriter` | `Reporting/HtmlDiffCellRangeReportData.cs`, `Reporting/IHtmlCellRangeDiffReportWriter.cs` |

Semantic notes that are NOT obvious from the signatures (the `WorkflowDefinition`
group-derivation rules, the `CfChanged` "false when DvType == List" rule, the
DV/CF capture-and-display rules, and the sheet-order-tabs report behaviour) are
documented where they apply: group derivation under "Workflow definition format"
below; DV/CF and sheet-order-tabs in the diff skills (see next note).

### Per-sheet specifics

CLQ / RLQ / GD per-sheet diff specs and the cross-sheet diff conventions are
documented in skills, loaded on demand:
`.claude/skills/{clq-diff,rlq-diff,gd-diff,diff-task-conventions}/SKILL.md`.
A prompt may name a skill explicitly to force-load it.

### Task families

Diff tasks come in two families:
- **Special-purpose, per-sheet** (`ControlLevelQuestionDiff`, `RiskLevelQuestionDiff`,
  `GeneralDataDiff`): each has its own config record, parser, and engine because the sheet
  structure and the matching it requires demand it; settings load from external configuration
  file(s) named by node parameters. CLQ/RLQ share `HtmlQuestionDiffReportWriter`; GD has its own.
- **General-purpose, parameter-driven** (`CellRangeDiff`): compares two workbooks cell-by-cell
  at the same address over configured ranges — no matching, no sections — and is reusable across
  structurally simple sheets. It has **no config file**: all settings are inline parameters on
  the workflow-JSON node — `file1Path`, `file2Path`, `sheet1Name`, `sheet2Name`, `ranges`
  (a semicolon-delimited A1 string, e.g. `"B2:F40;H2:H40"`), `compareScope` (`Value` |
  `ValueAndDvCf`, required), optional `reportTitle`; output via the node's `outputs.report`.
  Full contract and report shape: see the `cell-range-diff` skill.

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

Raw cell content plus Excel structural metadata (data-validation type/formula,
conditional-formatting operator) that `IExcelReader` does not expose is read via
`IExcelStructureReader` — used by the diff tasks. See the diff skills for the
DV/CF capture, display, and comparison rules.

---

## Presentation layer conventions

- Framework: WPF on .NET 10. Target `net10.0-windows`.
- Pattern: MVVM using CommunityToolkit.Mvvm source generators.
- Use `[ObservableProperty]` for bindable properties. Use `[RelayCommand]` for commands.
- ViewModels are in `ItrqTool.Presentation/ViewModels/`.
- Views (XAML) are in `ItrqTool.Presentation/Views/`.

**UI model records** (never expose domain types to the view layer — non-negotiable
rule 5). These bindable surrogate types live in `src/ItrqTool.Presentation/UIModels/`
(authoritative for signatures): `WorkflowListItem`, `WorkflowGroupItem`,
`WorkflowLoadFailureItem`, `TaskRowItem` + `TaskRowStatus`, `TaskParameterItem`,
`LogEntry`. Read the source for fields; the load-bearing rule is that none of these
leak a Domain type onto a bindable surface.

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

`RunTaskCommand` awaits `session.RunCurrentTaskAsync(CancellationToken.None)`, then marks
the just-run row `Completed` or `Failed` with its formatted duration, appends the task's
result messages to the live log (see "Result display" below), sets `SelectedTask` to that
row, and re-syncs the remaining row statuses and the run button from the new session state.

`SelectedTask` is two-way bound to the task ListBox's SelectedItem. Selecting a row drives
a **configuration viewer**, not a result panel: `SelectedTaskDisplayName` and
`SelectedTaskParameters` (the node's static workflow-JSON parameters, projected as
`TaskParameterItem` rows) are populated from the selected node. There is no per-row
historical-result view.

**Result display.** Task results are not shown in a dedicated bindable result panel. On
completion, `WorkflowRunViewModel` keeps the Domain `TaskResult` private and translates its
messages into `LogEntry` rows (mapping `MessageSeverity` → log level) via a private
`AppendResultToLog`, pushing them into the shared `IUiLogSink`; the live log panel binds to
`LogSink.Entries`. This translation is the boundary that keeps Domain `TaskResult` /
`MessageSeverity` off the bindable surface (rule 5). `CopyLogCommand` copies the current log
to the clipboard.

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

Per-sheet diff-task test specifics are documented in the relevant sheet skill
(`.claude/skills/{clq-diff,rlq-diff,gd-diff}/`).

**Current test counts (baseline — the always-on verification anchor):**
Architecture 14, Domain 13, Application 12, Tasks 352, Infrastructure 107, Integration 40
— **538 total**.

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

The diff-report writers (`IHtmlReportWriter`, `IHtmlGeneralDataDiffReportWriter`,
`IHtmlCellRangeDiffReportWriter`) are also registered here as singletons — see the diff skills.

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
1. Update the source in `src/ItrqTool.Domain/` (authoritative for signatures).
2. Update the relevant call sites.
3. Architecture tests and unit tests will identify all call sites that need updating.

---

## Deployment

Publishing & runtime paths: see `.claude/skills/deployment/SKILL.md`.

---

## Maintaining this file

This codebase uses Claude Code's layered documentation model:

- **CLAUDE.md = always-on governance.** Loaded every session. Holds: the hard
  constraints, architecture overview, conventions, build/test commands, and the
  test-count baseline. The "why" and "where," not the "what."
- **Skills (`.claude/skills/<name>/SKILL.md`) = on-demand task knowledge.** Only a
  skill's `name` + `description` are loaded at startup; the body loads when Claude
  judges the task relevant, or when a prompt names the skill explicitly to
  force-load it. Sheet-specific specs and infrequent task guides (publishing) live
  here. Current skills: `diff-task-conventions`, `clq-diff`, `rlq-diff`, `gd-diff`,
  `cell-range-diff`, `deployment`.

**Hard constraints never move to a skill.** Every non-negotiable rule, the
conservative-input posture, and the detect-everything principle stay in CLAUDE.md
verbatim. A skill that failed to trigger would leave a load-bearing constraint out
of context — so they must always be in the always-on file.

Update `CLAUDE.md` whenever:
- A new domain type is introduced or an existing one changes signature (update the
  inline core-contract block or the source-pointer index; full signatures live in
  the source).
- A documented domain type, UI-model type, view model, or file is **removed or renamed**
  (update the pointer or delete the reference — never leave a dangling name; this is the
  gap that produced the phantom result-panel VMs).
- A new NuGet package is added to any project.
- A new project is added to the solution.
- A new convention is established (e.g. a new naming rule, a new file location).
- A rule is intentionally relaxed or tightened.

Update the relevant **skill** when sheet-specific or task-specific reference
changes (a config format, a parser detail, a report shape, publish flags). Add a
new skill when a new sheet or lifecycle step arrives; keep its `description`
concrete and keyword-rich so it triggers reliably.

`CLAUDE.md` is read at the start of every session. If it is out of date,
subsequent sessions will drift from the established architecture.

Per-developer artifacts not in version control:
  - `.claude/settings.local.json` — Claude Code's per-machine permission
    state. Accumulates as new tool permissions are granted; review it
    directly on the developer's machine when auditing what Claude Code
    can do. Not tracked in git.
