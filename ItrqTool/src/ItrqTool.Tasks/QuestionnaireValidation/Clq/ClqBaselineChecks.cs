using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using ItrqTool.Tasks.Shared;

namespace ItrqTool.Tasks.QuestionnaireValidation.Clq;

/// <summary>
/// Version-neutral CLQ baseline checks. Turns an <see cref="AlignmentResult{T}"/>
/// (plus the role map and config) into a flat list of <see cref="ValidationFinding"/>.
/// Pure static, reads no files, never throws for a no-data condition — every anomaly
/// is a finding, never an exception. Severity per finding is resolved by the
/// <see cref="FindingEmitter"/> at emit time. Faithful port of CLQ_v01's
/// <c>ClqValidationChecks.Build</c>; column letters come from
/// <see cref="IClqBaselineConfig"/> (never hard-coded), the non-identity payload
/// from <see cref="ClqBaselineRoleMap{T}"/>, the identity fields from
/// <see cref="IAlignmentIdentity"/>.
/// </summary>
public static class ClqBaselineChecks
{
    public static IReadOnlyList<ValidationFinding> Run<T>(
        AlignmentResult<T> alignment,
        ClqBaselineRoleMap<T> roles,
        IClqBaselineConfig config,
        FindingEmitter emitter)
        where T : class, IAlignmentIdentity
    {
        ArgumentNullException.ThrowIfNull(alignment);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(emitter);

        var findings = new List<ValidationFinding>();

        // ── Phase 1: malformed keys (all workbooks) ──────────────────────────────
        foreach (var mk in alignment.MalformedKeys)
        {
            var reason = mk.Reason == MalformedKeyReason.Blank
                ? "blank"
                : $"duplicated ('{mk.XrefId}')";
            findings.Add(emitter.Emit(ClqBaselineFinding.XrefIdEmptyOrDuplicated,
                $"{config.XrefIdColumn}{mk.RowNumber}",
                questionNumber: null, questionText: null, requestedData: null, providedBy: null,
                $"Identity key in {WorkbookName(mk.Workbook)} at row {mk.RowNumber} is {reason}; " +
                "the question cannot be reliably matched within or across years."));
        }

        // ── Phase 2: within-year removed (template questions absent from response) ─
        foreach (var removed in alignment.WithinYearRemoved)
        {
            findings.Add(emitter.Emit(ClqBaselineFinding.QuestionRemoved,
                $"{config.XrefIdColumn}{removed.RowNumber}",
                questionNumber: removed.QuestionNumber, questionText: removed.QuestionText,
                requestedData: null, providedBy: null,   // template-side: no responder
                $"Template question (identity key '{removed.XrefId}', template row {removed.RowNumber}) " +
                "is absent from the response."));
        }

        // ── Phase 3: per current-response question ───────────────────────────────
        foreach (var aq in alignment.Aligned)
        {
            // A current row whose OWN key is malformed is already covered by the
            // MalformedKeys finding above; skip all its dependent checks (emit once).
            if (aq.WithinYear == WithinYearJoin.NotEvaluatedMalformedKey)
                continue;

            var cur = aq.Current;
            int row = cur.RowNumber;

            // Number-format (per-row flag; message uses the RAW OriginalText, not the
            // stripped QuestionText).
            if (roles.NumberFormatUnrecognized(cur))
                findings.Add(emitter.Emit(ClqBaselineFinding.NumberFormatUnrecognized,
                    $"{config.TextColumn}{row}", cur.QuestionNumber, cur.QuestionText,
                    requestedData: null, providedBy: roles.ProvidedBy(cur),
                    $"Question-number prefix at {config.TextColumn}{row} is not a recognised two-level " +
                    $"form: '{cur.OriginalText}'."));

            // D1b: within-year row structure (Added / RowShifted / ReferenceTextAltered / AnswerValidationRuleChanged)
            if (aq.WithinYear == WithinYearJoin.AddedInResponse)
            {
                findings.Add(emitter.Emit(ClqBaselineFinding.QuestionAdded,
                    $"{config.XrefIdColumn}{row}", cur.QuestionNumber, cur.QuestionText,
                    requestedData: null, providedBy: roles.ProvidedBy(cur),
                    $"Response question (identity key '{cur.XrefId}', row {row}) is absent from the empty template."));
            }
            else if (aq.WithinYear == WithinYearJoin.JoinedByXrefId)
            {
                var tmpl = aq.TemplateMatch!; // non-null IFF JoinedByXrefId

                if (aq.RowShifted)
                    findings.Add(emitter.Emit(ClqBaselineFinding.QuestionRowShifted,
                        $"{config.XrefIdColumn}{row}", cur.QuestionNumber, cur.QuestionText,
                        requestedData: null, providedBy: roles.ProvidedBy(cur),
                        $"Question (identity key '{cur.XrefId}') moved from template row {tmpl.RowNumber} " +
                        $"to response row {row}."));

                // Reference text — compound finding listing every differing field.
                var alteredFields = new List<string>();
                var alteredCols   = new List<string>();
                if (aq.TextMismatched)                                                 { alteredFields.Add("question text");     alteredCols.Add(config.TextColumn); }
                if (!RefEqual(roles.Guidance(cur),    roles.Guidance(tmpl)))          { alteredFields.Add("guidance");          alteredCols.Add(config.GuidanceColumn); }
                if (!RefEqual(roles.ChapterName(cur), roles.ChapterName(tmpl)))       { alteredFields.Add("chapter");           alteredCols.Add(config.TextColumn); }
                if (!RefEqual(cur.SectionName,        tmpl.SectionName))              { alteredFields.Add("section");           alteredCols.Add(config.TextColumn); }
                if (!RefEqual(cur.XrefId,             tmpl.XrefId))                   { alteredFields.Add("identity-key text"); alteredCols.Add(config.XrefIdColumn); }

                if (alteredFields.Count > 0)
                {
                    var cells = string.Join(", ",
                        alteredCols.Distinct(StringComparer.Ordinal).Select(c => $"{c}{row}"));
                    findings.Add(emitter.Emit(ClqBaselineFinding.ReferenceTextAltered,
                        cells, cur.QuestionNumber, cur.QuestionText,
                        requestedData: null, providedBy: roles.ProvidedBy(cur),
                        $"Frozen reference text differs from the template in: {string.Join(", ", alteredFields)}."));
                }

                // Frozen constraint — answer-cell data-validation rule.
                if (DvComparer.IsDvChangedFull(
                        roles.AnswerDvType(tmpl), roles.AnswerDvOperator(tmpl), roles.AnswerDvFormula(tmpl), roles.AnswerDvFormula2(tmpl),
                        roles.AnswerDvType(cur),  roles.AnswerDvOperator(cur),  roles.AnswerDvFormula(cur),  roles.AnswerDvFormula2(cur)))
                {
                    findings.Add(emitter.Emit(ClqBaselineFinding.AnswerValidationRuleChanged,
                        $"{config.AnswerColumn}{row}", cur.QuestionNumber, cur.QuestionText,
                        requestedData: null, providedBy: roles.ProvidedBy(cur),
                        $"Answer-cell data-validation rule at {config.AnswerColumn}{row} differs from the template."));
                }
            }

            // Input validity.
            var answer = roles.Answer(cur);
            bool answerUsable = false;

            if (string.IsNullOrWhiteSpace(answer))
            {
                findings.Add(emitter.Emit(ClqBaselineFinding.AnswerMissing,
                    $"{config.AnswerColumn}{row}", cur.QuestionNumber, cur.QuestionText,
                    requestedData: null, providedBy: roles.ProvidedBy(cur),
                    $"Answer cell {config.AnswerColumn}{row} is empty; the question was not answered."));
            }
            else if (!config.AllowedAnswers.Contains(answer, StringComparer.Ordinal))
            {
                findings.Add(emitter.Emit(ClqBaselineFinding.AnswerNotInAllowedSet,
                    $"{config.AnswerColumn}{row}", cur.QuestionNumber, cur.QuestionText,
                    requestedData: null, providedBy: roles.ProvidedBy(cur),
                    $"Answer '{answer}' at {config.AnswerColumn}{row} is not in the allowed set " +
                    $"[{string.Join(", ", config.AllowedAnswers)}]."));
            }
            else
            {
                answerUsable = true;
            }

            if (answerUsable)
            {
                bool requireStrengths = answer is "1" or "2" or "3";
                bool requireWeaknesses = answer is "2" or "3" or "4";

                if (requireStrengths && string.IsNullOrWhiteSpace(roles.Strengths(cur)))
                    findings.Add(emitter.Emit(ClqBaselineFinding.StrengthsMissing,
                        $"{config.StrengthsColumn}{row}", cur.QuestionNumber, cur.QuestionText,
                        requestedData: null, providedBy: roles.ProvidedBy(cur),
                        $"Answer '{answer}' requires a strengths explanation but {config.StrengthsColumn}{row} is empty."));

                if (requireWeaknesses && string.IsNullOrWhiteSpace(roles.Weaknesses(cur)))
                    findings.Add(emitter.Emit(ClqBaselineFinding.WeaknessesMissing,
                        $"{config.WeaknessesColumn}{row}", cur.QuestionNumber, cur.QuestionText,
                        requestedData: null, providedBy: roles.ProvidedBy(cur),
                        $"Answer '{answer}' requires a weaknesses explanation but {config.WeaknessesColumn}{row} is empty."));
            }

            // D2: cross-year switch (XrefIdConflict / NewXrefIdResemblesPrevious / SameXrefIdTextDiverged /
            //     NoPreviousBaseline / Agree → F-integrity + deviation)
        }

        return findings;
    }

    private static bool RefEqual(string? a, string? b) =>
        string.Equals(a ?? "", b ?? "", StringComparison.Ordinal);

    private static string WorkbookName(ValidationWorkbook wb) => wb switch
    {
        ValidationWorkbook.CurrentResponse => "the current response",
        ValidationWorkbook.EmptyTemplate => "the empty template",
        ValidationWorkbook.PreviousResponse => "the previous response",
        _ => wb.ToString()
    };
}
