namespace ItrqTool.Tasks.GeneralDataDiff;

public sealed record AddedQuestion(GeneralDataQuestion Question);

public sealed record RemovedQuestion(GeneralDataQuestion Question);

/// <summary>
/// One per-cell diff entry for answer cells (D/E/F). OldText is null when the
/// cell was added on the new side (not present in old); NewText is null when
/// the cell was removed (not present in new). DV/CF carried in raw form
/// (DvType + DvFormula); the mapping layer formats to a display string.
/// </summary>
public sealed record AnswerCellChange(
    int     RowOffset,
    string  Column,
    string? OldText,
    string? NewText,
    string? OldDvType,
    string? OldDvFormula,
    string? OldCfOperator,
    string? NewDvType,
    string? NewDvFormula,
    string? NewCfOperator,
    bool    TextChanged,
    bool    DvChanged,
    bool    CfChanged
);

/// <summary>
/// Per-cell diff entry for explanation cells (column G). Same semantics as
/// AnswerCellChange minus the Column field (always G).
/// </summary>
public sealed record ExplanationCellChange(
    int     RowOffset,
    string? OldText,
    string? NewText,
    string? OldDvType,
    string? OldDvFormula,
    string? OldCfOperator,
    string? NewDvType,
    string? NewDvFormula,
    string? NewCfOperator,
    bool    TextChanged,
    bool    DvChanged,
    bool    CfChanged
);

public sealed record ChangedQuestion(
    GeneralDataQuestion OldQuestion,
    GeneralDataQuestion NewQuestion,
    double  SimilarityScore,
    double? SecondBestSimilarity,
    bool    TextChanged,                  // base similarity on QuestionText < 1.0
    bool    NumberChanged,
    bool    AnswerCellsChanged,           // AnswerCellChanges non-empty
    bool    ExplanationCellsChanged,      // ExplanationCellChanges non-empty
    IReadOnlyList<AnswerCellChange>      AnswerCellChanges,
    IReadOnlyList<ExplanationCellChange> ExplanationCellChanges
);

public sealed record UnchangedQuestion(
    GeneralDataQuestion Question,         // the new-year question (current side)
    double? SecondBestSimilarity,
    int     PreviousRowNumber             // matched previous-year question's row
);

public sealed record DiffResult(
    IReadOnlyList<AddedQuestion>     Added,
    IReadOnlyList<RemovedQuestion>   Removed,
    IReadOnlyList<ChangedQuestion>   Changed,
    IReadOnlyList<UnchangedQuestion> Unchanged
);
