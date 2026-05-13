namespace ItrqTool.Domain;

public record TaskResult(
    bool Succeeded,
    IReadOnlyList<TaskMessage> Messages,
    TimeSpan Duration
);
