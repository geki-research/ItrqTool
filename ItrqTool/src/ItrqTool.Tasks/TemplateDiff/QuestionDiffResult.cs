namespace ItrqTool.Tasks.TemplateDiff;

public sealed record AddedQuestion(AuditQuestion Question);

public sealed record RemovedQuestion(AuditQuestion Question);

public sealed record ChangedQuestion(
    AuditQuestion OldQuestion,
    AuditQuestion NewQuestion,
    double SimilarityScore     // [0.0, 1.0]
);

public sealed record ValidationChange(
    AuditQuestion OldQuestion,
    AuditQuestion NewQuestion,
    string? OldDvType,
    string? NewDvType,
    string? OldCfOperator,
    string? NewCfOperator
);

public sealed record UnchangedQuestion(AuditQuestion Question);

public sealed record DiffResult(
    IReadOnlyList<AddedQuestion>      Added,
    IReadOnlyList<RemovedQuestion>    Removed,
    IReadOnlyList<ChangedQuestion>    Changed,
    IReadOnlyList<ValidationChange>   ValidationChanges,
    IReadOnlyList<UnchangedQuestion>  Unchanged
);
