namespace ItrqTool.Domain.Validation;

public record ValidationFinding(
    ValidationCheck  Check,
    FindingEvaluation Evaluation,
    string           CellAddresses,
    string?          QuestionNumber,
    string?          QuestionText,
    string?          RequestedData,
    string?          ProvidedBy,
    string           CheckResult
);
