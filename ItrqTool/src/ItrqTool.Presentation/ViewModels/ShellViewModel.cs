using CommunityToolkit.Mvvm.ComponentModel;
using ItrqTool.Domain;

namespace ItrqTool.Presentation.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly WorkflowListViewModel _listVm;
    private readonly WorkflowRunViewModel _runVm;

    [ObservableProperty]
    private ObservableObject _currentViewModel;

    public ShellViewModel(WorkflowListViewModel listVm, WorkflowRunViewModel runVm)
    {
        _listVm = listVm;
        _runVm = runVm;

        _listVm.WorkflowSelected += OnWorkflowSelected;
        _runVm.BackRequested += OnBackRequested;

        _currentViewModel = _listVm;
        _listVm.Load();
    }

    private void OnWorkflowSelected(WorkflowDefinition definition)
    {
        _runVm.InitializeFor(definition);
        CurrentViewModel = _runVm;
    }

    private void OnBackRequested()
    {
        _listVm.Load();
        CurrentViewModel = _listVm;
    }
}
