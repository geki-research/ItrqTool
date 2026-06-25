namespace ItrqTool.Tasks.GeneralDataValidationV01;

// One explanation triplet from a single sheet row of a GD question. GD questions are
// multi-row: every row carries its own requested / previous / current explanation cells
// (columns I / J / K), accumulated in row order under the answer the row belongs to. A
// collapsed bare-qid question (one implicit answer, one row) still carries exactly one
// triplet, usually all-blank.
//
// RowNumber is the triplet's ACTUAL worksheet row (the row the I/J/K cells live on), so a
// per-row check can address an individual explanation cell (e.g. K{RowNumber}) rather than
// the answer's anchor row. Explanation cells are NOT merged across the answer.
//
// GD's own type (deliberately not the RLQ-namespaced RlqExplanationRow) to avoid cross-stack
// coupling, mirroring its shape.
public sealed record GdExplanationRow(string? Requested, string? Previous, string? Current, int RowNumber);
