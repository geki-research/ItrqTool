using ItrqTool.Domain;
using ItrqTool.Tasks.QuestionnaireValidation.Parsing;

namespace ItrqTool.Tasks.GeneralDataValidationV01;

/// <summary>
/// GD_v01 multi-row question parser. Unlike RLQ's <c>RlqV01QuestionParser</c> (which groups a
/// MAXIMAL CONTIGUOUS run of equal XrefId into one question), GD questions are NOT contiguous
/// by question-id: sibling qids interleave their rows (recon §3 deviation 1). The parser
/// therefore buckets by qid in a DICTIONARY, tolerating non-contiguous rows.
/// <para>
/// XrefId scheme (column Q): variable-depth, ':'-delimited (':' is the only structural
/// delimiter; qids contain hyphens but never colons). 1 segment = bare-qid (collapsed
/// single-answer question); 2 = <c>qid:answer-id</c>; 3 = <c>qid:answer-id:explanation-id</c>.
/// Question identity = the qid (substring before the first ':'); a row's answer is its
/// answer-id segment; every row also carries an I/J/K explanation triplet.
/// </para>
/// <para>
/// Once-per-answer cells (G/H/L/O) are merged across the answer's rows: their value sits on the
/// answer's anchor (min) row and reads blank on continuation rows, so they are captured on the
/// first (min-row) sighting of each (qid, answer-id). The display-block C/D (question number +
/// text) are merged across the DISPLAY question, which may span MULTIPLE qids (recon §3
/// deviation 3) — ClosedXML returns the value on the merge anchor and blank on continuation
/// rows (Step-0 finding), so C/D are resolved from the most-recent non-blank C/D at/above the
/// qid's first row via a running display-block tracker (reset at each section boundary).
/// </para>
/// <para>
/// Sections only — no chapters. Section-header rows come from the <see cref="QuestionnaireLayout"/>
/// and are skipped (this also skips the template's <c>B/O="&lt;header section&gt;"</c> sentinel
/// rows and their section-prefix Q values). Rows outside any section's question-row extent are
/// skipped, consistent with the shared <see cref="QuestionParser"/> / RLQ.
/// </para>
/// <para>
/// Malformed XrefIds are collected separately (the cross-year alignment + identity gate are a
/// later chunk): a BLANK Q, an UN-PARSEABLE Q (empty qid / blank segment / depth &gt; 3), and a
/// DUPLICATE full-XrefId (the full string, not the qid) each land in <see cref="GdV01ParseResult.Malformed"/>.
/// Out-of-sequence answer/explanation ids are NOT detected here (possible later addition).
/// </para>
/// </summary>
public static class GdV01QuestionParser
{
    public static GdV01ParseResult Parse(
        IReadOnlyList<ExcelRowStructure> rows,
        QuestionnaireLayout layout,
        GdV01Config config,
        ICollection<TaskMessage> messages)
    {
        var sectionByRow = layout.Sections.ToDictionary(s => s.SectionRow);

        var qidOrder = new List<string>();
        var byQid = new Dictionary<string, QidAccumulator>(StringComparer.Ordinal);
        var malformed = new List<GdMalformedXref>();
        var validFullXrefs = new List<(int Row, string Full)>();

        LayoutSection? currentSectionDef = null;
        string currentSection = "";
        string? lastC = null;   // running display-block question number (col C)
        string? lastD = null;   // running display-block question text   (col D)

        foreach (var row in rows) // IExcelStructureReader guarantees ascending row-number order
        {
            if (sectionByRow.TryGetValue(row.RowNumber, out var secDef))
            {
                currentSection = GetCellText(row, secDef.NameColumn) ?? "";
                currentSectionDef = secDef;
                lastC = null;   // a display block never spans a section boundary
                lastD = null;
                continue;
            }

            if (currentSectionDef is null) continue;
            if (row.RowNumber < currentSectionDef.FirstQuestionRow) continue;
            if (row.RowNumber > currentSectionDef.LastQuestionRow) continue; // outside the section extent

            // Running display-block C/D: a non-blank value on this row opens/continues the block
            // (merged anchor); blank means we are on a continuation row, so the previous value holds.
            var cText = GetCellText(row, config.QuestionNumberColumn);
            var dText = GetCellText(row, config.TextColumn);
            if (!string.IsNullOrWhiteSpace(cText)) lastC = cText;
            if (!string.IsNullOrWhiteSpace(dText)) lastD = dText;

            var raw = GetCellText(row, config.XrefIdColumn);
            if (!TryParseXref(raw, out var qid, out var aid, out var full, out var reason))
            {
                malformed.Add(new GdMalformedXref(row.RowNumber, NormalizeOrNull(raw), reason));
                continue;
            }

            validFullXrefs.Add((row.RowNumber, full));

            if (!byQid.TryGetValue(qid, out var qAcc))
            {
                qAcc = new QidAccumulator(qid, row.RowNumber, currentSection, lastC, lastD ?? "");
                byQid[qid] = qAcc;
                qidOrder.Add(qid);
            }

            var aidKey = aid ?? "";   // bare-qid → single implicit answer keyed by ""
            if (!qAcc.ByAid.TryGetValue(aidKey, out var aAcc))
            {
                aAcc = new AnswerAccumulator(
                    answerId:       aid,
                    anchorRow:      row.RowNumber,                              // first (min) sighting = anchor
                    previousAnswer: GetCellText(row, config.PreviousAnswerColumn),
                    answer:         GetCellText(row, config.AnswerColumn),
                    materialChange: GetCellText(row, config.MaterialChangeColumn),
                    providedBy:     GetCellText(row, config.ProvidedByColumn));
                qAcc.ByAid[aidKey] = aAcc;
                qAcc.Answers.Add(aAcc);
            }

            // Every row of the answer contributes one explanation triplet, in row order.
            aAcc.Explanations.Add(new GdExplanationRow(
                Requested: GetCellText(row, config.RequestedExplanationColumn),
                Previous:  GetCellText(row, config.PreviousExplanationColumn),
                Current:   GetCellText(row, config.CurrentExplanationColumn),
                RowNumber: row.RowNumber));
        }

        // Duplicate full-XrefId detection: one malformed entry per offending row (mirrors the
        // engine's ClassifyKeys, which emits one entry per duplicated row).
        var duplicatedFulls = validFullXrefs
            .GroupBy(x => x.Full, StringComparer.Ordinal)
            .Where(g => g.Count() >= 2)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (duplicatedFulls.Count > 0)
            foreach (var (rowNumber, full) in validFullXrefs)
                if (duplicatedFulls.Contains(full))
                    malformed.Add(new GdMalformedXref(rowNumber, full, GdMalformedXrefReason.Duplicate));

        var questions = qidOrder
            .Select(qid => byQid[qid].ToQuestion())
            .ToList();

        var orderedMalformed = malformed
            .OrderBy(m => m.RowNumber)
            .ToList();

        return new GdV01ParseResult(questions, orderedMalformed);
    }

    // Trim; treat null/whitespace-only as blank, an empty/blank segment or depth > 3 as
    // un-parseable. On success: qid = first segment, aid = second segment (null if absent),
    // full = the trimmed whole string (the duplicate-detection key). The explanation-id
    // segment is validated for presence/blankness but not returned (explanations are per-row,
    // not keyed by id this chunk).
    private static bool TryParseXref(
        string? raw, out string qid, out string? aid, out string full, out GdMalformedXrefReason reason)
    {
        qid = ""; aid = null; full = ""; reason = default;

        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed)) { reason = GdMalformedXrefReason.Blank; return false; }

        var segments = trimmed.Split(':');
        if (segments.Length > 3 || segments.Any(string.IsNullOrWhiteSpace))
        {
            reason = GdMalformedXrefReason.Unparseable;
            return false;
        }

        qid = segments[0].Trim();
        aid = segments.Length > 1 ? segments[1].Trim() : null;
        full = trimmed;
        return true;
    }

    private static string? NormalizeOrNull(string? raw)
    {
        var trimmed = raw?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string? GetCellText(ExcelRowStructure row, string column)
        => row.CellsByColumn.TryGetValue(column.ToUpperInvariant(), out var cell) ? cell.TextValue : null;

    // ── mutable accumulators (parse-time only; converted to immutable records at the end) ──

    private sealed class QidAccumulator
    {
        public QidAccumulator(string qid, int anchorRow, string sectionName, string? questionNumber, string originalText)
        {
            Qid = qid;
            AnchorRow = anchorRow;
            SectionName = sectionName;
            QuestionNumber = questionNumber;
            OriginalText = originalText;
        }

        public string Qid { get; }
        public int AnchorRow { get; }
        public string SectionName { get; }
        public string? QuestionNumber { get; }
        public string OriginalText { get; }
        public List<AnswerAccumulator> Answers { get; } = [];
        public Dictionary<string, AnswerAccumulator> ByAid { get; } = new(StringComparer.Ordinal);

        public GdV01Question ToQuestion() => new(
            RowNumber:      AnchorRow,
            XrefId:         Qid,
            OriginalText:   OriginalText,
            QuestionText:   OriginalText,   // no number prefix to strip
            SectionName:    SectionName,
            QuestionNumber: QuestionNumber,
            Answers:        Answers.Select(a => a.ToAnswer()).ToList());
    }

    private sealed class AnswerAccumulator
    {
        public AnswerAccumulator(
            string? answerId, int anchorRow, string? previousAnswer, string? answer,
            string? materialChange, string? providedBy)
        {
            AnswerId = answerId;
            AnchorRow = anchorRow;
            PreviousAnswer = previousAnswer;
            Answer = answer;
            MaterialChange = materialChange;
            ProvidedBy = providedBy;
        }

        public string? AnswerId { get; }
        public int AnchorRow { get; }
        public string? PreviousAnswer { get; }
        public string? Answer { get; }
        public string? MaterialChange { get; }
        public string? ProvidedBy { get; }
        public List<GdExplanationRow> Explanations { get; } = [];

        public GdAnswer ToAnswer() => new(
            AnswerId:       AnswerId,
            AnchorRow:      AnchorRow,
            PreviousAnswer: PreviousAnswer,
            Answer:         Answer,
            MaterialChange: MaterialChange,
            ProvidedBy:     ProvidedBy,
            Explanations:   Explanations);
    }
}

/// <summary>
/// GD_v01 parser output: the parsed questions (one per qid, in first-appearance order) plus the
/// malformed-XrefId collection the parser detected (row-ascending). The cross-year alignment +
/// identity gate consume the malformed set in a later chunk.
/// </summary>
public sealed record GdV01ParseResult(
    IReadOnlyList<GdV01Question> Questions,
    IReadOnlyList<GdMalformedXref> Malformed);

public enum GdMalformedXrefReason { Blank, Duplicate, Unparseable }

/// <summary>A malformed XrefId the parser detected. <paramref name="XrefId"/> is null for a
/// blank cell, otherwise the offending (trimmed) value.</summary>
public sealed record GdMalformedXref(int RowNumber, string? XrefId, GdMalformedXrefReason Reason);
