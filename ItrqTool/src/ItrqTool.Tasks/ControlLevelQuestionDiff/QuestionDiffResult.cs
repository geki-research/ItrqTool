namespace ItrqTool.Tasks.ControlLevelQuestionDiff;

public sealed record AddedQuestion(AuditQuestion Question);

public sealed record RemovedQuestion(AuditQuestion Question);

public sealed record ChangedQuestion(
    AuditQuestion OldQuestion,
    AuditQuestion NewQuestion,
    double  SimilarityScore,
    double? SecondBestSimilarity,
    bool    TextChanged,
    bool    NumberChanged,
    bool    DvChanged,
    bool    CfChanged
);

public sealed record UnchangedQuestion(
    AuditQuestion Question,
    double? SecondBestSimilarity,
    int PreviousRowNumber
);

public sealed record DiffResult(
    IReadOnlyList<AddedQuestion>      Added,
    IReadOnlyList<RemovedQuestion>    Removed,
    IReadOnlyList<ChangedQuestion>    Changed,
    IReadOnlyList<UnchangedQuestion>  Unchanged
);
