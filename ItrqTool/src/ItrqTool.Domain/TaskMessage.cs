namespace ItrqTool.Domain;

public record TaskMessage(
    MessageSeverity Severity,
    string Text,
    DateTimeOffset Timestamp
);
