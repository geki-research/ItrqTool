namespace ItrqTool.Domain.Validation;

public record FeedbackChecklistWriterOptions(
    string                                        SheetName,
    int                                           DataStartRow,
    IReadOnlyDictionary<ChecklistColumn, string>  ColumnMap
);
