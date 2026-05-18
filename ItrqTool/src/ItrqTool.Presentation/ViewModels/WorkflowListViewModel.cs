using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ItrqTool.Domain;
using ItrqTool.Presentation.UIModels;

namespace ItrqTool.Presentation.ViewModels;

public partial class WorkflowListViewModel : ObservableObject
{
    private readonly IWorkflowLoader _loader;
    private readonly Dictionary<string, WorkflowDefinition> _definitionsById = new();

    [ObservableProperty]
    private ObservableCollection<WorkflowGroupItem> _workflowGroups = [];

    [ObservableProperty]
    private WorkflowListItem? _selectedWorkflow;

    [ObservableProperty]
    private ObservableCollection<WorkflowLoadFailureItem> _failures = new();

    [ObservableProperty]
    private bool _hasFailures;

    [ObservableProperty]
    private bool _showFailureDetails;

    [ObservableProperty]
    private string _failureSummary = string.Empty;

    public event Action<WorkflowDefinition>? WorkflowSelected;

    public WorkflowListViewModel(IWorkflowLoader loader) => _loader = loader;

    public void Load()
    {
        _definitionsById.Clear();
        var result = _loader.LoadAll();
        foreach (var wf in result.Workflows)
            _definitionsById[wf.Id] = wf;

        var items = result.Workflows
            .Select(wf => new WorkflowListItem(wf.Id, wf.Name, wf.Group))
            .ToList();

        var groups = items
            .GroupBy(wf => wf.Group ?? "Ungrouped")
            .OrderBy(g => g.Key == "Ungrouped")
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new WorkflowGroupItem(
                g.Key,
                new ObservableCollection<WorkflowListItem>(
                    g.OrderBy(wf => wf.Name, StringComparer.OrdinalIgnoreCase))))
            .ToList();

        WorkflowGroups = new ObservableCollection<WorkflowGroupItem>(groups);
        SelectedWorkflow = null;

        Failures.Clear();
        foreach (var failure in result.Failures)
        {
            Failures.Add(new WorkflowLoadFailureItem(
                Path.GetFileName(failure.FilePath),
                failure.ErrorMessage));
        }
        HasFailures = Failures.Count > 0;
        FailureSummary = Failures.Count switch
        {
            0 => string.Empty,
            1 => "1 workflow file failed to load.",
            _ => $"{Failures.Count} workflow files failed to load."
        };
        ShowFailureDetails = false;
    }

    [RelayCommand(CanExecute = nameof(CanSelect))]
    private void SelectCurrent()
    {
        if (SelectedWorkflow is null) return;
        if (_definitionsById.TryGetValue(SelectedWorkflow.Id, out var def))
            WorkflowSelected?.Invoke(def);
    }

    private bool CanSelect() => SelectedWorkflow is not null;

    [RelayCommand]
    private void ToggleFailureDetails() => ShowFailureDetails = !ShowFailureDetails;

    partial void OnSelectedWorkflowChanged(WorkflowListItem? value)
        => SelectCurrentCommand.NotifyCanExecuteChanged();
}
