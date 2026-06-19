namespace ItrqTool.Tasks.RiskLevelQuestionValidationV01;

// One explanation triplet from a single sheet row of an RLQ question. RLQ questions are
// multi-row: every row in a question's contiguous-XrefId group carries its own
// requested / previous / current explanation cells (columns I / J / K), accumulated in
// row order. A question with no explanation requests still occupies one row, whose triplet
// is all-blank.
public sealed record RlqExplanationRow(string? Requested, string? Previous, string? Current);
