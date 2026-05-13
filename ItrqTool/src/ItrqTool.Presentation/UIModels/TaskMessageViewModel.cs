namespace ItrqTool.Presentation.UIModels;

public record TaskMessageViewModel(
    TaskMessageSeverity Severity,
    string Text,
    string Timestamp
);
