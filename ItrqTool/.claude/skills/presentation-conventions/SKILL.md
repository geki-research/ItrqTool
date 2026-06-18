---
name: presentation-conventions
description: WPF/MVVM Presentation-layer reference for ItrqTool. The framework/pattern conventions (WPF on .NET 10, CommunityToolkit.Mvvm source generators, ViewModels/ and Views/ locations), the UI-model surrogate records (WorkflowListItem, WorkflowGroupItem, WorkflowLoadFailureItem, TaskRowItem/TaskRowStatus, TaskParameterItem, LogEntry) that keep Domain types off the bindable surface (rule 5), WorkflowRunViewModel responsibilities (TaskRowItem per-row status derivation, RunTaskCommand, SelectedTask configuration-viewer, result-display-to-log translation, RunButtonLabel/BackCommand/OpenWorkingFolderCommand behaviour), the AddItrqToolServices composition-root entry point, the event-based shell/navigation pattern (ShellViewModel/CurrentViewModel/MainWindow DataTemplates), and WorkflowListViewModel load-failure banner behaviour. Load when working on any view model, XAML view, UI-model record, navigation, or the run-view log/result display.
---

# Presentation layer conventions

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
