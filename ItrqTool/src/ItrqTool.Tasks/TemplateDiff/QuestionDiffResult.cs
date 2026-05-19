namespace ItrqTool.Tasks.TemplateDiff;

public sealed record AddedQuestion(AuditQuestion Question);

public sealed record RemovedQuestion(AuditQuestion Question);

public sealed record ChangedQuestion(
    AuditQuestion OldQuestion,
    AuditQuestion NewQuestion,
    double  SimilarityScore,
    bool    TextChanged,
    bool    NumberChanged,
    bool    DvChanged,
    bool    CfChanged
);

public sealed record UnchangedQuestion(AuditQuestion Question);

public sealed record DiffResult(
    IReadOnlyList<AddedQuestion>      Added,
    IReadOnlyList<RemovedQuestion>    Removed,
    IReadOnlyList<ChangedQuestion>    Changed,
    IReadOnlyList<UnchangedQuestion>  Unchanged
);
