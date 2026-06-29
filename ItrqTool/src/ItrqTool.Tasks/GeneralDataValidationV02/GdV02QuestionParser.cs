using ItrqTool.Domain;
using ItrqTool.Tasks.GeneralDataValidationV01;
using ItrqTool.Tasks.QuestionnaireValidation.Parsing;

namespace ItrqTool.Tasks.GeneralDataValidationV02;

/// <summary>
/// GD_v02 multi-row question parser. Near-verbatim fork of <c>GdV01QuestionParser</c> with
/// exactly three deltas:
/// 1. reads <see cref="GdV02Config.ProvidedByColumn"/> (P) and <see cref="GdV02Config.XrefIdColumn"/>
///    (R) instead of v01's O/Q;
/// 2. reads <see cref="GdV02Config.HowExplanationColumn"/> (M) per answer at the anchor row into
///    <see cref="GdV02Answer.HowExplanation"/>, mirroring the existing MaterialChange/L once-per-answer read;
/// 3. produces <see cref="GdV02ParseResult"/>/<see cref="GdV02Question"/>/<see cref="GdV02Answer"/>
///    instead of the v01 types.
/// All other logic is byte-for-byte identical to the v01 parser: variable-depth ':'-delimited xref
/// (qid:answer-id:explanation-id, depth&gt;3/blank-segment → Unparseable); non-contiguous qid
/// bucketing in a dictionary; merged display-block C/D resolved from most-recent non-blank at/above
/// the qid's first row, reset at section boundary; once-per-answer G/H/L/M/P at anchor row; per-row
/// I/J/K triplets; malformed (blank/duplicate-full-xref/unparseable) collected separately.
/// </summary>
public static class GdV02QuestionParser
{
    public static GdV02ParseResult Parse(
        IReadOnlyList<ExcelRowStructure> rows,
        QuestionnaireLayout layout,
        GdV02Config config,
        ICollection<TaskMessage> messages)
    {
        var sectionByRow = layout.Sections.ToDictionary(s => s.SectionRow);
        var specByRow = config.Sections.ToDictionary(s => s.HeaderRow);

        var qidOrder = new List<string>();
        var byQid = new Dictionary<string, QidAccumulator>(StringComparer.Ordinal);
        var malformed = new List<GdMalformedXref>();
        var sectionHeaderMismatches = new List<GdSectionHeaderMismatch>();
        var validFullXrefs = new List<(int Row, string Full)>();

        LayoutSection? currentSectionDef = null;
        string currentSection = "";
        string? lastC = null;
        string? lastD = null;

        foreach (var row in rows)
        {
            if (sectionByRow.TryGetValue(row.RowNumber, out var secDef))
            {
                var actualHeader = GetCellText(row, secDef.NameColumn);
                currentSection = actualHeader ?? "";
                currentSectionDef = secDef;

                if (specByRow.TryGetValue(row.RowNumber, out var spec)
                    && !string.Equals(actualHeader ?? "", spec.ExpectedName, StringComparison.Ordinal))
                {
                    sectionHeaderMismatches.Add(new GdSectionHeaderMismatch(
                        row.RowNumber, secDef.NameColumn, spec.ExpectedName, actualHeader));
                }

                lastC = null;
                lastD = null;
                continue;
            }

            if (currentSectionDef is null) continue;
            if (row.RowNumber < currentSectionDef.FirstQuestionRow) continue;
            if (row.RowNumber > currentSectionDef.LastQuestionRow) continue;

            var cText = GetCellText(row, config.QuestionNumberColumn);
            var dText = GetCellText(row, config.TextColumn);
            if (!string.IsNullOrWhiteSpace(cText)) lastC = cText;
            if (!string.IsNullOrWhiteSpace(dText)) lastD = dText;

            // Delta 1: read XrefId from config.XrefIdColumn (R in v02, vs Q in v01)
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

            var aidKey = aid ?? "";
            if (!qAcc.ByAid.TryGetValue(aidKey, out var aAcc))
            {
                // Delta 1 (continued): read ProvidedBy from config.ProvidedByColumn (P in v02, vs O in v01)
                // Delta 2: read HowExplanation from config.HowExplanationColumn (M in v02) at anchor row
                aAcc = new AnswerAccumulator(
                    answerId:        aid,
                    anchorRow:       row.RowNumber,
                    previousAnswer:  GetCellText(row, config.PreviousAnswerColumn),
                    answer:          GetCellText(row, config.AnswerColumn),
                    materialChange:  GetCellText(row, config.MaterialChangeColumn),
                    howExplanation:  GetCellText(row, config.HowExplanationColumn),
                    providedBy:      GetCellText(row, config.ProvidedByColumn));
                qAcc.ByAid[aidKey] = aAcc;
                qAcc.Answers.Add(aAcc);
            }

            aAcc.Explanations.Add(new GdExplanationRow(
                Requested: GetCellText(row, config.RequestedExplanationColumn),
                Previous:  GetCellText(row, config.PreviousExplanationColumn),
                Current:   GetCellText(row, config.CurrentExplanationColumn),
                RowNumber: row.RowNumber));
        }

        var duplicatedFulls = validFullXrefs
            .GroupBy(x => x.Full, StringComparer.Ordinal)
            .Where(g => g.Count() >= 2)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (duplicatedFulls.Count > 0)
            foreach (var (rowNumber, full) in validFullXrefs)
                if (duplicatedFulls.Contains(full))
                    malformed.Add(new GdMalformedXref(rowNumber, full, GdMalformedXrefReason.Duplicate));

        // Delta 3: produce GdV02Question/GdV02ParseResult instead of v01 types
        var questions = qidOrder
            .Select(qid => byQid[qid].ToQuestion())
            .ToList();

        var orderedMalformed = malformed
            .OrderBy(m => m.RowNumber)
            .ToList();

        return new GdV02ParseResult(questions, orderedMalformed, sectionHeaderMismatches);
    }

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

        // Delta 3: produce GdV02Question
        public GdV02Question ToQuestion() => new(
            RowNumber:      AnchorRow,
            XrefId:         Qid,
            OriginalText:   OriginalText,
            QuestionText:   OriginalText,
            SectionName:    SectionName,
            QuestionNumber: QuestionNumber,
            Answers:        Answers.Select(a => a.ToAnswer()).ToList());
    }

    private sealed class AnswerAccumulator
    {
        public AnswerAccumulator(
            string? answerId, int anchorRow, string? previousAnswer, string? answer,
            string? materialChange, string? howExplanation, string? providedBy)
        {
            AnswerId = answerId;
            AnchorRow = anchorRow;
            PreviousAnswer = previousAnswer;
            Answer = answer;
            MaterialChange = materialChange;
            HowExplanation = howExplanation;
            ProvidedBy = providedBy;
        }

        public string? AnswerId { get; }
        public int AnchorRow { get; }
        public string? PreviousAnswer { get; }
        public string? Answer { get; }
        public string? MaterialChange { get; }
        public string? HowExplanation { get; }
        public string? ProvidedBy { get; }
        public List<GdExplanationRow> Explanations { get; } = [];

        // Delta 3: produce GdV02Answer
        public GdV02Answer ToAnswer() => new(
            AnswerId:       AnswerId,
            AnchorRow:      AnchorRow,
            PreviousAnswer: PreviousAnswer,
            Answer:         Answer,
            MaterialChange: MaterialChange,
            HowExplanation: HowExplanation,
            ProvidedBy:     ProvidedBy,
            Explanations:   Explanations);
    }
}

/// <summary>
/// GD_v02 parser output: the parsed questions (one per qid, in first-appearance order), the
/// malformed-XrefId collection (row-ascending), and the declared-section header mismatches
/// (row order) the parser detected.
/// </summary>
public sealed record GdV02ParseResult(
    IReadOnlyList<GdV02Question> Questions,
    IReadOnlyList<GdMalformedXref> Malformed,
    IReadOnlyList<GdSectionHeaderMismatch> SectionHeaderMismatches);
