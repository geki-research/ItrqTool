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
            .Select(wf => new WorkflowListItem(
                wf.Id,
                string.IsNullOrWhiteSpace(wf.Name) ? wf.Id.Split(':').Last() : wf.Name,
                wf.Group))
            .ToList();

        WorkflowGroups = new ObservableCollection<WorkflowGroupItem>(BuildTree(items));
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

    private static List<WorkflowGroupItem> BuildTree(List<WorkflowListItem> items)
    {
        var rootNodes = new Dictionary<string, GroupNode>(StringComparer.OrdinalIgnoreCase);
        GroupNode? ungrouped = null;

        foreach (var item in items)
        {
            if (item.Group is null)
            {
                ungrouped ??= new GroupNode("Ungrouped");
                ungrouped.Workflows.Add(item);
            }
            else
            {
                var segments = item.Group.Split(':');
                var dict = rootNodes;
                GroupNode? node = null;
                foreach (var seg in segments)
                {
                    if (!dict.TryGetValue(seg, out node))
                    {
                        node = new GroupNode(seg);
                        dict[seg] = node;
                    }
                    dict = node.SubGroups;
                }
                node!.Workflows.Add(item);
            }
        }

        var result = new List<WorkflowGroupItem>();
        result.AddRange(
            rootNodes.Values
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.ToItem()));
        if (ungrouped is not null) result.Add(ungrouped.ToItem());
        return result;
    }

    private sealed class GroupNode(string name)
    {
        public string Name { get; } = name;
        public Dictionary<string, GroupNode> SubGroups { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<WorkflowListItem> Workflows { get; } = [];

        public WorkflowGroupItem ToItem() => new(
            Name,
            [.. SubGroups.Values
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.ToItem())],
            [.. Workflows.OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)]);
    }
}
