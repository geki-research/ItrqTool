namespace ItrqTool.Domain;

public sealed class WorkflowDefinitionException : Exception
{
    public WorkflowDefinitionException(string message) : base(message) { }
    public WorkflowDefinitionException(string message, Exception inner) : base(message, inner) { }
}
