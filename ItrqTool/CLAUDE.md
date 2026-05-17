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

## Solution structure

```
ItrqTool.sln
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
    └── ItrqTool.Tasks.Tests/         References: ItrqTool.Tasks, ItrqTool.Domain, NSubstitute
```

Workflow definition files live in a `/workflows` directory at the solution root.
They are copied to the output directory by the Presentation project's `.csproj`.

---

## Technology stack — exact packages

| Concern | Package | Project |
|---|---|---|
| Runtime | .NET 10 | all |
| UI framework | WPF (built-in) | Presentation |
| MVVM | CommunityToolkit.Mvvm | Presentation |
| DI container | Microsoft.Extensions.DependencyInjection 10.0.8 | Presentation |
| DI assembly scanning | Scrutor | Presentation |
| Configuration | Microsoft.Extensions.Configuration + Json 10.0.8 | Presentation |
| Logging (file + UI) | Serilog + Serilog.Sinks.File + Serilog.Extensions.Logging | Presentation |
| Logging abstraction | Microsoft.Extensions.Logging.Abstractions 10.0.8 | Domain, Application, Tasks |
| Excel reading | ClosedXML | Infrastructure only |
| Testing framework | xUnit | all test projects |
| Mocking | NSubstitute | Application.Tests, Tasks.Tests |
| Assertions | FluentAssertions | all test projects |
| Architecture tests | NetArchTest.Rules | Architecture.Tests |

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
    string WorkingDirectory
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
    IReadOnlyList<TaskNode> Nodes
);

public record TaskNode(
    string Id,
    string TaskType,
    IReadOnlyDictionary<string, TaskOutputRef> Inputs,       // localKey → (upstreamTaskId, outputKey)
    IReadOnlyDictionary<string, string> OutputFileNames      // logicalKey → filename
);

public record TaskOutputRef(string TaskId, string OutputKey);

// ── Excel reading ──────────────────────────────────────────────────────────────

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

## Maintaining this file

Update `CLAUDE.md` whenever:
- A new domain type is introduced or an existing one changes signature.
- A new NuGet package is added to any project.
- A new project is added to the solution.
- A new convention is established (e.g. a new naming rule, a new file location).
- A rule is intentionally relaxed or tightened.

`CLAUDE.md` is read at the start of every session. If it is out of date,
subsequent sessions will drift from the established architecture.
