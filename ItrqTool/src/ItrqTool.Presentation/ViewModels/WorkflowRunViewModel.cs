using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ItrqTool.Application;
using ItrqTool.Domain;
using ItrqTool.Presentation.UIModels;

namespace ItrqTool.Presentation.ViewModels;

public partial class WorkflowRunViewModel : ObservableObject
{
    private readonly WorkflowSessionFactory _sessionFactory;

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

    public event Action? BackRequested;

    public WorkflowRunViewModel(WorkflowSessionFactory sessionFactory)
        => _sessionFactory = sessionFactory;

    public void InitializeFor(WorkflowDefinition definition)
        => WorkflowName = definition.Name;

    [RelayCommand]
    private void Back() => BackRequested?.Invoke();

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunTaskAsync(CancellationToken ct) => Task.CompletedTask;

    private void RefreshFromSession() { }
}
