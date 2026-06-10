namespace ItrqTool.Domain.Validation;

public record FeedbackChecklistRow(
    int                Counter,
    string             Worksheet,
    string?            QuestionNumber,
    string             CellAddresses,
    string?            QuestionText,
    string?            RequestedData,
    string?            ProvidedBy,
    FindingEvaluation  Evaluation,
    string             CheckResult
);
