namespace ItrqTool.Presentation.UIModels;

public record TaskResultViewModel(
    string TaskName,
    bool Succeeded,
    string Duration,
    IReadOnlyList<TaskMessageViewModel> Messages
);
