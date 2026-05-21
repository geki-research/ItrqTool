namespace ItrqTool.Tasks.RiskLevelQuestionDiff;

public sealed record AddedQuestion(RiskLevelQuestion Question);

public sealed record RemovedQuestion(RiskLevelQuestion Question);

public sealed record ChangedQuestion(
    RiskLevelQuestion OldQuestion,
    RiskLevelQuestion NewQuestion,
    double  SimilarityScore,
    double? SecondBestSimilarity,
    bool    TextChanged,         // base similarity < 1.0 on QuestionText
    bool    ExplanationChanged,  // base similarity < 1.0 on ExplanationPrompt
    bool    NumberChanged,
    bool    DvChanged,
    bool    CfChanged            // false when old DvType == "List"
);

public sealed record UnchangedQuestion(
    RiskLevelQuestion Question,
    double? SecondBestSimilarity
);

public sealed record DiffResult(
    IReadOnlyList<AddedQuestion>     Added,
    IReadOnlyList<RemovedQuestion>   Removed,
    IReadOnlyList<ChangedQuestion>   Changed,
    IReadOnlyList<UnchangedQuestion> Unchanged
);
