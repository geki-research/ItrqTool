using System.Collections.ObjectModel;
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
    private ObservableCollection<WorkflowListItem> _workflows = new();

    [ObservableProperty]
    private WorkflowListItem? _selectedWorkflow;

    public event Action<WorkflowDefinition>? WorkflowSelected;

    public WorkflowListViewModel(IWorkflowLoader loader) => _loader = loader;

    public void Load()
    {
        _definitionsById.Clear();
        var result = _loader.LoadAll();
        foreach (var wf in result.Workflows)
            _definitionsById[wf.Id] = wf;
        Workflows = new ObservableCollection<WorkflowListItem>(
            result.Workflows.Select(wf => new WorkflowListItem(wf.Id, wf.Name)));
    }

    [RelayCommand(CanExecute = nameof(CanSelect))]
    private void SelectCurrent()
    {
        if (SelectedWorkflow is null) return;
        if (_definitionsById.TryGetValue(SelectedWorkflow.Id, out var def))
            WorkflowSelected?.Invoke(def);
    }

    private bool CanSelect() => SelectedWorkflow is not null;

    partial void OnSelectedWorkflowChanged(WorkflowListItem? value)
        => SelectCurrentCommand.NotifyCanExecuteChanged();
}
