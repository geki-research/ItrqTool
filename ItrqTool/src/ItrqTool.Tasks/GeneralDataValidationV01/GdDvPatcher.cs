using ItrqTool.Domain;
using ItrqTool.Tasks.Shared;   // DvListParser

namespace ItrqTool.Tasks.GeneralDataValidationV01;

// ── GdDvPatcher — per-ANSWER DV stamping for GD (signature FROZEN for C2) ──
//
// The GD fork of QuestionnaireValidation.Parsing.DvPatcher. That core patcher is
// T : IAlignmentIdentity, keyed on q.RowNumber, and stamps ONE column per call. GD needs DV on
// TWO cells per ANSWER — H (answer-DV) and L (material-change-DV) — each at the answer's own
// AnchorRow, and GdAnswer is NOT IAlignmentIdentity. So the core patcher cannot be reused
// directly; GD reuses its *idea* (ReadCells over a column span + a `with`-updater) but forks the
// signature to answer grain × two roles.
//
// Per workbook, between GdV01QuestionParser.Parse and GdV01Aligner.Align (Chunk D wires this):
//   1. collect every answer's AnchorRow across questions.SelectMany(q => q.Answers);
//   2. ReadCells the H column over [minAnchor:maxAnchor] and the L column over [minAnchor:maxAnchor];
//   3. rebuild each GdV01Question with its Answers re-stamped: each GdAnswer `with` its 8 DV fields
//      (4 answer + 4 material-change) from the H/L cell at its AnchorRow, plus the 2 List-value
//      lists for the INLINE List case (DvListParser.ClassifySource == Inline → ParseInline),
//      mirroring RlqV01Profile's inline DV-role lambda;
//   4. a range-ref / named-range List resolution pass for List cells whose inline path returned
//      null — see the C2 boundary below.
//
// ── C1 vs C2 boundary ──
// IMPLEMENTED in C1 (steps 1–3): anchor-row span, H/L ReadCells, and the 8-field + inline-List
//   `with`-stamp. This is the low-risk part — a field copy off ExcelCellStructure plus the exact
//   inline-List idiom already frozen in RlqV01Profile.
// DEFERRED to C2 (step 4): the per-(answer, role) range-ref / named-range resolution.
//   DvRangeRefResolver.Resolve<T> is T : class and selector-driven but assumes ONE list per
//   record; GD has TWO roles (H + L) at answer grain, so it must be invoked per (answer, role) —
//   adapt, not call-as-is (recon ⚠4). The hook is ResolveRangeAndNamedLists below; in C1 it
//   returns its input unchanged so inline-only workbooks are already fully patched and the
//   signature is frozen.
//
// SIGNATURE FROZEN for C2 — the public Patch(...) shape below must not change; C2 fills step 4.

public static class GdDvPatcher
{
    public static IReadOnlyList<GdV01Question> Patch(
        IExcelStructureReader reader,
        string filePath,
        string sheetName,
        GdV01Config config,
        IReadOnlyList<GdV01Question> questions)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(questions);

        var anchors = questions.SelectMany(q => q.Answers).Select(a => a.AnchorRow).ToList();
        if (anchors.Count == 0) return questions;

        int minRow = anchors.Min();
        int maxRow = anchors.Max();

        var answerCol = config.AnswerColumn.ToUpperInvariant();
        var matChgCol = config.MaterialChangeColumn.ToUpperInvariant();

        var hCells = reader.ReadCells(filePath, sheetName, [$"{answerCol}{minRow}:{answerCol}{maxRow}"]);
        var lCells = reader.ReadCells(filePath, sheetName, [$"{matChgCol}{minRow}:{matChgCol}{maxRow}"]);

        var patched = questions
            .Select(q => q with { Answers = q.Answers.Select(a => StampInline(a, answerCol, matChgCol, hCells, lCells)).ToList() })
            .ToList();

        // C2 fill: range-ref / named-range List resolution for List cells whose inline path is null.
        return ResolveRangeAndNamedLists(reader, filePath, sheetName, config, patched);
    }

    // Steps 2–3: stamp the 8 DV fields + the 2 inline-List value lists onto one answer from its
    // H/L cells at AnchorRow. Cells absent from the read (no DV applied) leave the fields null.
    private static GdAnswer StampInline(
        GdAnswer answer,
        string answerCol,
        string matChgCol,
        IReadOnlyDictionary<string, ExcelCellStructure> hCells,
        IReadOnlyDictionary<string, ExcelCellStructure> lCells)
    {
        var result = answer;

        if (hCells.TryGetValue($"{answerCol}{answer.AnchorRow}", out var h))
            result = result with
            {
                AnswerDvType       = h.DataValidationType,
                AnswerDvFormula    = h.DataValidationFormula,
                AnswerDvOperator   = h.DataValidationOperator,
                AnswerDvFormula2   = h.DataValidationFormula2,
                AnswerDvListValues = InlineListValues(h),
            };

        if (lCells.TryGetValue($"{matChgCol}{answer.AnchorRow}", out var l))
            result = result with
            {
                MaterialChangeDvType       = l.DataValidationType,
                MaterialChangeDvFormula    = l.DataValidationFormula,
                MaterialChangeDvOperator   = l.DataValidationOperator,
                MaterialChangeDvFormula2   = l.DataValidationFormula2,
                MaterialChangeDvListValues = InlineListValues(l),
            };

        return result;
    }

    // The inline-List idiom frozen in RlqV01Profile: a List-typed cell whose source classifies as
    // Inline → its parsed members; otherwise null (range-ref / named-range resolved in step 4).
    private static IReadOnlyList<string>? InlineListValues(ExcelCellStructure cell)
        => string.Equals(cell.DataValidationType, "List", StringComparison.OrdinalIgnoreCase)
           && DvListParser.ClassifySource(cell.DataValidationFormula ?? "") == DvListSourceKind.Inline
            ? DvListParser.ParseInline(cell.DataValidationFormula ?? "")
            : null;

    // ── C2 FILL (step 4) ──
    // Per (answer, role) range-ref / named-range List resolution for List cells whose inline path
    // returned null. DvRangeRefResolver.Resolve<T> assumes one list per record; GD has two roles ×
    // N answers, so C2 invokes resolution per (answer, role) — adapt, not call-as-is (recon ⚠4).
    // C1: identity pass (inline-only workbooks are already fully patched by StampInline).
    private static IReadOnlyList<GdV01Question> ResolveRangeAndNamedLists(
        IExcelStructureReader reader,
        string filePath,
        string sheetName,
        GdV01Config config,
        IReadOnlyList<GdV01Question> questions)
        => questions;
}
