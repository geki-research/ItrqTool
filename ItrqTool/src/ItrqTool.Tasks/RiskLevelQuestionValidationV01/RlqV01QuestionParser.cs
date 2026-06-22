using ItrqTool.Domain;
using ItrqTool.Tasks.QuestionnaireValidation.Parsing;

namespace ItrqTool.Tasks.RiskLevelQuestionValidationV01;

/// <summary>
/// RLQ_v01 multi-row question parser. Unlike the shared single-row
/// <see cref="QuestionParser"/>, an RLQ question spans one OR MORE contiguous sheet rows:
/// a question has exactly one answer (column H) plus zero or more explanation requests, and
/// with 2+ explanation requests it occupies that many rows.
/// <para>
/// The grouping key is the XrefId (column Q), which is repeated identically on every row of
/// a question. A maximal run of contiguous rows with the same non-blank XrefId is one
/// question. The other once-per-question fields are merged cells: their value sits on the
/// group's FIRST (top) row and reads blank on continuation rows, so they are read from the
/// first row. The explanation triplet (I / J / K) is per-row and accumulated across the
/// group in row order.
/// </para>
/// <para>
/// Sections only — there are no chapters. Section-header rows come from the
/// <see cref="QuestionnaireLayout"/>; the section name is read from each header's name
/// column (column D for RLQ). Rows outside any section's question-row extent are skipped,
/// consistent with <see cref="QuestionParser"/>.
/// </para>
/// </summary>
public static class RlqV01QuestionParser
{
    public static IReadOnlyList<RlqV01Question> Parse(
        IReadOnlyList<ExcelRowStructure> rows,
        QuestionnaireLayout layout,
        RlqV01Config config,
        ICollection<TaskMessage> messages)
    {
        var sectionByRow = layout.Sections.ToDictionary(s => s.SectionRow);

        var questions = new List<RlqV01Question>();
        string currentSection = "";
        LayoutSection? currentSectionDef = null;

        // Open question group state. Rows is non-null exactly when a group is open.
        List<ExcelRowStructure>? groupRows = null;
        string? groupXref = null;
        string groupSection = "";

        void CloseGroup()
        {
            if (groupRows is null) return;
            questions.Add(BuildQuestion(groupRows, groupXref, groupSection, config));
            groupRows = null;
            groupXref = null;
        }

        foreach (var row in rows) // IExcelStructureReader guarantees ascending row-number order
        {
            if (sectionByRow.TryGetValue(row.RowNumber, out var secDef))
            {
                CloseGroup(); // a section boundary always closes any open question
                currentSection = GetCellText(row, secDef.NameColumn) ?? "";
                currentSectionDef = secDef;
                continue;
            }

            if (currentSectionDef is null) continue;
            if (row.RowNumber < currentSectionDef.FirstQuestionRow) continue;
            if (row.RowNumber > currentSectionDef.LastQuestionRow)
            {
                CloseGroup(); // left the section's question extent
                continue;
            }

            var xref = NormalizeXref(GetCellText(row, config.XrefIdColumn));

            if (xref is null)
            {
                // A blank XrefId cannot be grouped. Close any open group and emit the row as a
                // degenerate one-row record (XrefId = null) so it flows through ClassifyKeys →
                // MalformedKeyReason.Blank → MalformedKeyCheck (parity with CLQ blank-key handling),
                // surfacing a Fatal structure.xrefid-empty-or-duplicated finding rather than a soft warning.
                CloseGroup();
                questions.Add(BuildQuestion([row], null, currentSection, config));
                continue;
            }

            if (groupRows is not null && xref == groupXref)
            {
                groupRows.Add(row); // continuation row of the current question
            }
            else
            {
                CloseGroup();
                groupRows = [row];
                groupXref = xref;
                groupSection = currentSection;
            }
        }

        CloseGroup(); // emit the final open group

        return questions;
    }

    private static RlqV01Question BuildQuestion(
        List<ExcelRowStructure> groupRows, string? xref, string sectionName, RlqV01Config config)
    {
        var first = groupRows[0]; // once-per-question fields live on the first (top) row
        var originalText = GetCellText(first, config.TextColumn) ?? "";

        var explanations = groupRows
            .Select(r => new RlqExplanationRow(
                Requested: GetCellText(r, config.RequestedExplanationColumn),
                Previous:  GetCellText(r, config.PreviousExplanationColumn),
                Current:   GetCellText(r, config.CurrentExplanationColumn),
                RowNumber: r.RowNumber))
            .ToList();

        return new RlqV01Question(
            RowNumber:        first.RowNumber,
            XrefId:           xref,
            OriginalText:     originalText,
            QuestionText:     originalText, // no number prefix to strip
            SectionName:      sectionName,
            QuestionNumber:   GetCellText(first, config.QuestionNumberColumn),
            Guidance:         GetCellText(first, config.GuidanceColumn),
            RequestedType:    GetCellText(first, config.RequestedTypeColumn),
            PreviousAnswer:   GetCellText(first, config.PreviousAnswerColumn),
            Answer:           GetCellText(first, config.AnswerColumn),
            AnswerDvType:     null,
            AnswerDvFormula:  null,
            AnswerDvOperator: null,
            AnswerDvFormula2: null,
            MaterialChange:           GetCellText(first, config.MaterialChangeColumn),
            MaterialChangeDvType:     null,
            MaterialChangeDvFormula:  null,
            MaterialChangeDvOperator: null,
            MaterialChangeDvFormula2: null,
            ProvidedBy:               GetCellText(first, config.ProvidedByColumn),
            ExplanationRows:  explanations);
    }

    // Trim and treat whitespace-only as no XrefId (cannot group on blank).
    private static string? NormalizeXref(string? raw)
    {
        var trimmed = raw?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string? GetCellText(ExcelRowStructure row, string column)
        => row.CellsByColumn.TryGetValue(column.ToUpperInvariant(), out var cell) ? cell.TextValue : null;
}
