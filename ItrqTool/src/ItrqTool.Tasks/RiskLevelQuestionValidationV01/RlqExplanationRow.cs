namespace ItrqTool.Tasks.RiskLevelQuestionValidationV01;

// One explanation triplet from a single sheet row of an RLQ question. RLQ questions are
// multi-row: every row in a question's contiguous-XrefId group carries its own
// requested / previous / current explanation cells (columns I / J / K), accumulated in
// row order. A question with no explanation requests still occupies one row, whose triplet
// is all-blank.
//
// RowNumber is the triplet's ACTUAL worksheet row (the row the I/J/K cells live on), so a
// per-row check can address an individual explanation cell (e.g. K{RowNumber}) rather than
// the question's anchor row. It is the last positional field; the once-per-question fields
// live on the group's first row (RlqV01Question.RowNumber), but each triplet keeps its own
// row because explanation cells are NOT merged across the group.
public sealed record RlqExplanationRow(string? Requested, string? Previous, string? Current, int RowNumber);
