namespace ItrqTool.Tasks.QuestionnaireValidation.Checks;

public readonly record struct ExplanationRowView(string? Requested, string? Current, int RowNumber, string? ProvidedBy);
