using ClosedXML.Excel;
using ItrqTool.Integration.Tests.WorksheetStructure;
using ItrqTool.Tasks.GeneralDataValidationV01;
using ItrqTool.Tasks.GeneralDataValidationV02;

namespace ItrqTool.Integration.Tests.GeneralDataInject;

/// <summary>
/// Per-answer spec for one GD inject fixture answer. The SAME spec drives BOTH the v01
/// previous-response workbook and the v02 current-template workbook, so the answer's identity
/// (<see cref="AnswerId"/>) and its explanation-row count line up index-for-index across the
/// two books — exactly what the inject's <c>PairByAnswerId</c> + position-aligned K→J logic needs.
/// <para>
/// The H data-validation category is settable INDEPENDENTLY per side
/// (<see cref="V01AnswerDvType"/> vs <see cref="V02AnswerDvType"/>) so a single answer can pair,
/// e.g., v01 <c>WholeNumber</c> ↔ v02 <c>Decimal</c> — driving one type-compatibility-policy arm.
/// One DV type per cell (no conflicting DV requirements packed onto one cell).
/// </para>
/// </summary>
/// <param name="AnswerId">XrefId &lt;answer-id&gt; segment, e.g. "a1". Unique within the question.</param>
/// <param name="V01AnswerDvType">DV category applied to the v01 H cell ("WholeNumber" | "Decimal" | "List").</param>
/// <param name="V01AnswerValue">Value written into the v01 H anchor cell — the inject SOURCE native.</param>
/// <param name="V02AnswerDvType">DV category applied to the v02 H cell — the inject TARGET category.</param>
/// <param name="ProvidedBy">v01 provided-by (column O); the inject carries it to v02 column P.</param>
/// <param name="Explanations">v01 current-explanation (column K) values, one per explanation row of this answer.</param>
public sealed record GdInjectAnswerSpec(
    string AnswerId,
    string V01AnswerDvType,
    object V01AnswerValue,
    string V02AnswerDvType,
    string? ProvidedBy,
    IReadOnlyList<string> Explanations);

/// <summary>
/// Fixture builder for the GD inject end-to-end policy trial. Writes a <c>General Data</c> sheet
/// into a workbook from a small per-answer spec, mirroring the real GD geometry produced by
/// <see cref="ItrqTool.Integration.Tests.GdV01.GdV01WorkbookWriter"/>: one question whose answers
/// each occupy one-or-more explanation rows, once-per-answer cells at the answer anchor row, per-row
/// explanation triplets, and the structure header stamped at row 2 so the inject's structure gate
/// passes.
/// <para>
/// v01 geometry: qid/xref in <see cref="GdV01Config.XrefIdColumn"/> (Q), display block C/D, the
/// once-per-answer G/H/O at the answer anchor row, per-row I/J/K explanation triplets, provided-by
/// in O. v02 geometry (+1 shift after L): qid/xref in <see cref="GdV02Config.XrefIdColumn"/> (R),
/// HowExplanation in M, provided-by in P; G/J/P/M left EMPTY (the inject fills G/J/P, never M), the
/// answer H carrying ONLY the per-answer DV-type (no value) for the target side.
/// </para>
/// <para>
/// Every row's full xref is made unique with a trailing <c>:e{n}</c> explanation-id segment so the
/// parser's duplicate-full-xref guard never trips on a multi-row answer; the qid (segment 0) and the
/// answer-id (segment 1) are what the aligner / mapper key on. Both books use the SAME first row and
/// the SAME per-answer explanation counts, so the two layouts are row-identical.
/// </para>
/// </summary>
public static class GdInjectWorkbookWriter
{
    public const string SheetName = "General Data";

    /// <summary>
    /// Anchor (top) row of each answer, given the question's first data row and the per-answer
    /// explanation counts. Answer <c>i</c> spans <c>answers[i].Explanations.Count</c> rows
    /// (min 1); the next answer starts immediately below. Identical for both books since they
    /// share <paramref name="firstRow"/> and the spec.
    /// </summary>
    public static IReadOnlyList<int> AnchorRows(int firstRow, IReadOnlyList<GdInjectAnswerSpec> answers)
    {
        var rows = new List<int>(answers.Count);
        var row = firstRow;
        foreach (var a in answers)
        {
            rows.Add(row);
            row += Math.Max(1, a.Explanations.Count);
        }
        return rows;
    }

    /// <summary>
    /// Writes the v01 previous-response workbook: one question (qid = <paramref name="qid"/>, all
    /// answers share it) in the section whose header sits at <paramref name="sectionHeaderRow"/>.
    /// Each answer's H carries its v01 value + DV at the anchor row, O carries provided-by, and each
    /// explanation row carries its K value. Once-per-answer G/H/O are merged when an answer spans &gt;1 row.
    /// </summary>
    public static void WritePrevious(
        string path, GdV01Config cfg, int sectionHeaderRow, string sectionName,
        int firstRow, string qid, string questionText, IReadOnlyList<GdInjectAnswerSpec> answers)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName);
        StructureHeaderStamper.Stamp(ws, "gd", "v01");

        ws.Cell(sectionHeaderRow, cfg.TextColumn).Value = sectionName;

        var anchors = AnchorRows(firstRow, answers);
        for (var ai = 0; ai < answers.Count; ai++)
        {
            var spec = answers[ai];
            var anchor = anchors[ai];
            var span = Math.Max(1, spec.Explanations.Count);

            // Display block C/D once per QUESTION, on the question's first row only.
            if (anchor == firstRow)
            {
                ws.Cell(anchor, cfg.QuestionNumberColumn).Value = "1";
                ws.Cell(anchor, cfg.TextColumn).Value           = questionText;
            }

            // Once-per-answer cells at the anchor row: H (value + DV), O (provided-by).
            SetNative(ws.Cell(anchor, cfg.AnswerColumn), spec.V01AnswerValue);
            ApplyDv(ws.Cell(anchor, cfg.AnswerColumn), spec.V01AnswerDvType);
            if (spec.ProvidedBy != null) ws.Cell(anchor, cfg.ProvidedByColumn).Value = spec.ProvidedBy;

            // Per-row explanation triplets (K = current explanation) + unique full xref per row.
            for (var ei = 0; ei < span; ei++)
            {
                var row = anchor + ei;
                ws.Cell(row, cfg.XrefIdColumn).Value = $"{qid}:{spec.AnswerId}:e{ei + 1}";
                if (ei < spec.Explanations.Count)
                    ws.Cell(row, cfg.CurrentExplanationColumn).Value = spec.Explanations[ei];
            }

            // Merge once-per-answer columns over a multi-row answer (value sits on the anchor row).
            if (span > 1)
                foreach (var col in new[] { cfg.PreviousAnswerColumn, cfg.AnswerColumn, cfg.ProvidedByColumn })
                    ws.Range($"{col}{anchor}:{col}{anchor + span - 1}").Merge();
        }

        wb.SaveAs(path);
    }

    /// <summary>
    /// Writes the v02 current-template workbook: the same question/answer identities + OriginalText
    /// (→ Agree), with G/J/P/M left blank (inject targets / not-a-target) and the answer H carrying
    /// ONLY the per-answer target DV-type (no value). Each explanation row carries the R xref so the
    /// parser materialises an explanation triplet (the K→J write target).
    /// </summary>
    public static void WriteCurrent(
        string path, GdV02Config cfg, int sectionHeaderRow, string sectionName,
        int firstRow, string qid, string questionText, IReadOnlyList<GdInjectAnswerSpec> answers)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName);
        StructureHeaderStamper.Stamp(ws, "gd", "v02");

        ws.Cell(sectionHeaderRow, cfg.TextColumn).Value = sectionName;

        var anchors = AnchorRows(firstRow, answers);
        for (var ai = 0; ai < answers.Count; ai++)
        {
            var spec = answers[ai];
            var anchor = anchors[ai];
            var span = Math.Max(1, spec.Explanations.Count);

            if (anchor == firstRow)
            {
                ws.Cell(anchor, cfg.QuestionNumberColumn).Value = "1";
                ws.Cell(anchor, cfg.TextColumn).Value           = questionText;
            }

            // Target H: DV only, no value (the inject fills G, never H).
            ApplyDv(ws.Cell(anchor, cfg.AnswerColumn), spec.V02AnswerDvType);

            for (var ei = 0; ei < span; ei++)
                ws.Cell(anchor + ei, cfg.XrefIdColumn).Value = $"{qid}:{spec.AnswerId}:e{ei + 1}";

            if (span > 1)
                ws.Range($"{cfg.AnswerColumn}{anchor}:{cfg.AnswerColumn}{anchor + span - 1}").Merge();
        }

        wb.SaveAs(path);
    }

    // One DV type per cell. Categories map to the XLAllowedValues names the structure reader surfaces
    // (XLAllowedValues.ToString()): "WholeNumber", "Decimal", "List".
    private static void ApplyDv(IXLCell cell, string dvType)
    {
        switch (dvType)
        {
            case "WholeNumber": cell.CreateDataValidation().WholeNumber.EqualOrGreaterThan(0); break;
            case "Decimal":     cell.CreateDataValidation().Decimal.EqualOrGreaterThan(0); break;
            case "List":        cell.CreateDataValidation().List("\"yes,no\""); break;
            default: throw new ArgumentException($"Unsupported DV type: {dvType}", nameof(dvType));
        }
    }

    // Strings write as text; everything else writes as a number (the WholeNumber/Decimal native).
    private static void SetNative(IXLCell cell, object value)
    {
        if (value is string s) cell.Value = s;
        else cell.Value = Convert.ToDouble(value);
    }
}
