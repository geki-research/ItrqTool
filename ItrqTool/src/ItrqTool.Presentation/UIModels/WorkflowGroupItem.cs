using System.Collections.ObjectModel;

namespace ItrqTool.Presentation.UIModels;

public record WorkflowGroupItem(
    string GroupName,
    ObservableCollection<WorkflowListItem> Workflows
);
