namespace ItrqTool.Presentation.UIModels;

public record TaskRowItem(
    string TaskId,
    string DisplayName,
    TaskRowStatus Status,
    string? Duration
);
