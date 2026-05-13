namespace ItrqTool.Domain;

public record WorkflowLoadResult(
    IReadOnlyList<WorkflowDefinition> Workflows,
    IReadOnlyList<WorkflowLoadFailure> Failures
);
