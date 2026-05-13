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
   `Domain` must have zero external NuGet or project references.

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
| DI container | Microsoft.Extensions.DependencyInjection | Presentation |
| DI assembly scanning | Scrutor | Presentation |
| Configuration | Microsoft.Extensions.Configuration + Json | Presentation |
| Logging | Serilog + Serilog.Sinks.File | Infrastructure |
| Logging abstraction | Microsoft.Extensions.Logging | Domain, Application, Tasks |
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
`<workflowDataRoot>/<workflow.Id>`. The root is configured in
appsettings.json under `"ItrqTool:WorkflowDataRoot"` and supports
Windows environment-variable expansion (`%USERPROFILE%`, `%LOCALAPPDATA%`,
etc.). Default if the key is missing or empty: `%USERPROFILE%\Documents\ItrqTool`.

Files persist between tasks within a session, allowing the user to
manually edit intermediate files between steps. On the first task
execution of a new session, WorkflowSession wipes the working
directory's contents before running the task — stale files from a
previous run never bleed into a new one. Subsequent task executions
within the same session do NOT wipe.

The application does NOT clean up working directories on close or
startup. Users grab their deliverables from the workflow's folder
whenever they choose.

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
```

**WorkflowRunViewModel responsibilities:**
- Hold the `WorkflowSession` instance across user interactions.
- Expose `ObservableCollection<TaskRowItem>` for the task list.
- Expose `TaskResultViewModel?` for the result panel (null = no result selected).
- Expose `string? RunButtonLabel` and `bool CanRun` derived from session status.
- The `RunTaskCommand` calls `session.RunCurrentTaskAsync()`, then calls
  `RefreshFromSession()` to sync all observable collections.

The user can click any completed task row to set `SelectedResult` to that task's
historical result. Clicking a pending or ready task sets `SelectedResult` to null.

The DI registrations are extracted into a static method
`CompositionRoot.AddItrqToolServices(this IServiceCollection services,
string workflowsDirectoryPath, string workflowDataRoot)`
in `ItrqTool.Presentation`. App.OnStartup builds IConfiguration from
appsettings.json, resolves the workflow data root with
`Environment.ExpandEnvironmentVariables`, creates the directory if
missing, and passes both paths to `AddItrqToolServices`.
Integration tests call the same method with per-test temp directories.
Treat `AddItrqToolServices` as the single source of truth for the
production object graph — never duplicate registrations elsewhere.

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

---

## DI registration (composition root in `ItrqTool.Presentation`)

```csharp
// Program.cs or App.xaml.cs — single composition root

var services = new ServiceCollection();

// Infrastructure
services.AddSingleton<IExcelReader, ClosedXmlExcelReader>();
services.AddSingleton<IWorkflowLoader, JsonWorkflowLoader>();
// Serilog file sink — configure once here

// Application
services.AddSingleton<WorkflowSessionFactory>();

// Tasks — automatic registration via Scrutor
services.Scan(scan => scan
    .FromAssemblyOf<IWorkflowTaskMarker>()   // marker interface in ItrqTool.Tasks
    .AddClasses(c => c.AssignableTo<IWorkflowTask>())
    .AsImplementedInterfaces()
    .WithTransientLifetime());

// Build the task registry from all registered IWorkflowTask instances
services.AddSingleton<ITaskRegistry, DependencyInjectionTaskRegistry>();

// Presentation
services.AddTransient<WorkflowRunViewModel>();
services.AddTransient<WorkflowListViewModel>();
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
