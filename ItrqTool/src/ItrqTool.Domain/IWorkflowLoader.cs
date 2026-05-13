namespace ItrqTool.Domain;

public interface IWorkflowLoader
{
    IReadOnlyList<WorkflowDefinition> LoadAll();
}
