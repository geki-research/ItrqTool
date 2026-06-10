namespace ItrqTool.Domain.Validation;

public record ValidationReport(
    string                          Sheet,
    string                          TaskType,
    IReadOnlyList<ValidationFinding> Findings
);
