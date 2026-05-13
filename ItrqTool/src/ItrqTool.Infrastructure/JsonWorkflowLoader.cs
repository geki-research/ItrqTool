using ItrqTool.Domain;

namespace ItrqTool.Infrastructure;

public sealed class JsonWorkflowLoader : IWorkflowLoader
{
    private readonly string _workflowsDirectory;

    public JsonWorkflowLoader(string workflowsDirectory)
        => _workflowsDirectory = workflowsDirectory;

    public IReadOnlyList<WorkflowDefinition> LoadAll()
        => throw new NotImplementedException();
}
