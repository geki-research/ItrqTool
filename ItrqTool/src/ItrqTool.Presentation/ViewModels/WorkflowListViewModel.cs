using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ItrqTool.Application;
using ItrqTool.Domain;

namespace ItrqTool.Presentation.ViewModels;

public partial class WorkflowListViewModel : ObservableObject
{
    private readonly IWorkflowLoader _loader;

    [ObservableProperty]
    private ObservableCollection<WorkflowDefinition> _workflows = [];

    public WorkflowListViewModel(IWorkflowLoader loader)
        => _loader = loader;

    public void LoadWorkflows()
        => Workflows = new ObservableCollection<WorkflowDefinition>(_loader.LoadAll());
}
