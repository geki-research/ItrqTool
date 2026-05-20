namespace ItrqTool.Presentation.UIModels;

public sealed class WorkflowGroupItem
{
    public string GroupName { get; }

    // Sub-groups first, then workflow leaves — both sorted alphabetically.
    // Contains WorkflowGroupItem and WorkflowListItem instances.
    public IReadOnlyList<object> Children { get; }

    public WorkflowGroupItem(
        string groupName,
        IReadOnlyList<WorkflowGroupItem> subGroups,
        IReadOnlyList<WorkflowListItem> workflows)
    {
        GroupName = groupName;
        Children = [.. subGroups, .. workflows];
    }
}
