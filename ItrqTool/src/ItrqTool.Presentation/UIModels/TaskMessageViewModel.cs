using ItrqTool.Domain;

namespace ItrqTool.Presentation.UIModels;

public record TaskMessageViewModel(
    MessageSeverity Severity,
    string Text,
    string Timestamp
);
