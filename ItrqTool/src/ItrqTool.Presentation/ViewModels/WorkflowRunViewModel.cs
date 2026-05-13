using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ItrqTool.Application;
using ItrqTool.Presentation.UIModels;

namespace ItrqTool.Presentation.ViewModels;

public partial class WorkflowRunViewModel : ObservableObject
{
    private readonly WorkflowSessionFactory _sessionFactory;

    [ObservableProperty]
    private ObservableCollection<TaskRowItem> _tasks = [];

    [ObservableProperty]
    private TaskResultViewModel? _selectedResult;

    [ObservableProperty]
    private string _runButtonLabel = "Run";

    [ObservableProperty]
    private bool _canRun;

    public WorkflowRunViewModel(WorkflowSessionFactory sessionFactory)
        => _sessionFactory = sessionFactory;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunTaskAsync(CancellationToken ct) => Task.CompletedTask;

    private void RefreshFromSession() { }
}
