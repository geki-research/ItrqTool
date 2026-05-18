using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ItrqTool.Application;
using ItrqTool.Domain;
using ItrqTool.Presentation.Logging;
using ItrqTool.Presentation.UIModels;

namespace ItrqTool.Presentation.ViewModels;

public partial class WorkflowRunViewModel : ObservableObject
{
    private readonly WorkflowSessionFactory _sessionFactory;
    private readonly IUiLogSink _logSink;

    private WorkflowSession? _session;
    private WorkflowDefinition? _definition;
    private IReadOnlyList<TaskNode> _order = Array.Empty<TaskNode>();
    private readonly Dictionary<string, int> _indexLookup = new();

    [ObservableProperty]
    private string _workflowName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<TaskRowItem> _tasks = new();

    [ObservableProperty]
    private TaskResultViewModel? _selectedResult;

    [ObservableProperty]
    private string _runButtonLabel = "Run";

    [ObservableProperty]
    private bool _canRun;

    [ObservableProperty]
    private TaskRowItem? _selectedTask;

    partial void OnSelectedTaskChanged(TaskRowItem? value) => UpdateResultForSelection();

    public IUiLogSink LogSink => _logSink;
    public string? SessionWorkingDirectory => _session?.WorkingDirectory;

    public event Action? BackRequested;

    public WorkflowRunViewModel(WorkflowSessionFactory sessionFactory, IUiLogSink logSink)
    {
        _sessionFactory = sessionFactory;
        _logSink = logSink;
    }

    public void InitializeFor(WorkflowDefinition definition)
    {
        _logSink.Clear();
        _definition = definition;
        WorkflowName = definition.Name;
        _session = _sessionFactory.Create(definition);
        _order = new WorkflowGraph(definition).GetTopologicalOrder();

        _indexLookup.Clear();
        Tasks.Clear();

        for (int i = 0; i < _order.Count; i++)
        {
            var node = _order[i];
            _indexLookup[node.Id] = i;
            Tasks.Add(new TaskRowItem(
                TaskId: node.Id,
                DisplayName: node.Id,
                Status: TaskRowStatus.Pending,
                Duration: null));
        }

        SelectedTask = null;
        SelectedResult = null;

        if (_order.Count == 0)
        {
            RunButtonLabel = "No tasks to run";
            CanRun = false;
        }
        else
        {
            SyncFromSession();
        }

        OpenWorkingFolderCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => BackRequested?.Invoke();

    private bool CanGoBack() => _session is null || _session.Status != WorkflowSessionStatus.Running;

    [RelayCommand]
    private void CopyLog()
    {
        var text = string.Join(Environment.NewLine, _logSink.Entries.Select(e =>
            $"{e.Timestamp:yyyy-MM-dd HH:mm:ss} [{e.Level}] {e.ShortCategory}: {e.Message}"));
        System.Windows.Clipboard.SetText(text);
    }

    [RelayCommand(CanExecute = nameof(CanOpenWorkingFolder))]
    private void OpenWorkingFolder()
    {
        if (_session is null) return;
        var path = _session.WorkingDirectory;
        Directory.CreateDirectory(path);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Failed to open working folder: {ex.Message}");
        }
    }

    private bool CanOpenWorkingFolder() => _session is not null;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunTaskAsync()
    {
        if (_session is null) return;

        int runningIndex = _session.CurrentIndex;
        var node = _order[runningIndex];

        Tasks[runningIndex] = Tasks[runningIndex] with { Status = TaskRowStatus.Running };
        CanRun = false;
        RunTaskCommand.NotifyCanExecuteChanged();
        RunButtonLabel = "Running…";
        BackCommand.NotifyCanExecuteChanged();

        var result = await _session.RunCurrentTaskAsync(CancellationToken.None);

        var resultVm = BuildResultVm(node, result);

        Tasks[runningIndex] = Tasks[runningIndex] with
        {
            Status = result.Succeeded ? TaskRowStatus.Completed : TaskRowStatus.Failed,
            Duration = FormatDuration(result.Duration)
        };

        SelectedResult = resultVm;
        SelectedTask = Tasks[runningIndex];

        SyncFromSession();
        BackCommand.NotifyCanExecuteChanged();
    }

    private void SyncFromSession()
    {
        if (_session is null) return;

        for (int i = 0; i < _order.Count; i++)
        {
            var target = ComputeStatus(i);
            var existing = Tasks[i];
            if (existing.Status != target)
                Tasks[i] = existing with { Status = target };
        }

        RunButtonLabel = _session.Status switch
        {
            WorkflowSessionStatus.ReadyToRun => "Run first task",
            WorkflowSessionStatus.AwaitingReview => "Run next task",
            WorkflowSessionStatus.Running => "Running…",
            WorkflowSessionStatus.Completed => "Workflow completed",
            WorkflowSessionStatus.Failed => "Workflow failed",
            _ => RunButtonLabel
        };

        CanRun = (_session.Status == WorkflowSessionStatus.ReadyToRun ||
                  _session.Status == WorkflowSessionStatus.AwaitingReview) &&
                 _order.Count > 0;
        RunTaskCommand.NotifyCanExecuteChanged();
    }

    private TaskRowStatus ComputeStatus(int i)
    {
        if (_session is null) return TaskRowStatus.Pending;
        if (i < _session.CurrentIndex) return TaskRowStatus.Completed;
        if (i > _session.CurrentIndex) return TaskRowStatus.Pending;
        return _session.Status switch
        {
            WorkflowSessionStatus.Running => TaskRowStatus.Running,
            WorkflowSessionStatus.Failed => TaskRowStatus.Failed,
            _ => TaskRowStatus.Ready
        };
    }

    private void UpdateResultForSelection()
    {
        if (_session is null || SelectedTask is null)
        {
            SelectedResult = null;
            return;
        }

        if (!_indexLookup.TryGetValue(SelectedTask.TaskId, out int index))
        {
            SelectedResult = null;
            return;
        }

        var historical = _session.GetResult(index);
        SelectedResult = historical is null ? null : BuildResultVm(_order[index], historical);
    }

    private TaskResultViewModel BuildResultVm(TaskNode node, TaskResult result) => new(
        TaskName: node.Id,
        Succeeded: result.Succeeded,
        Duration: FormatDuration(result.Duration),
        Messages: result.Messages.Select(TaskMessageMapper.ToUi).ToList());

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 60)
            return $"{duration.TotalSeconds:0.00}s";
        var minutes = (int)duration.TotalMinutes;
        var seconds = duration.Seconds;
        return $"{minutes}m {seconds:00}s";
    }
}
