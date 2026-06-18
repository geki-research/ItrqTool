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
/// original validation checks; column letters come from
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

            // ── Cross-year (D2) ──────────────────────────────────────────────────
            switch (aq.CrossYear)
            {
                case CrossYearOutcome.XrefIdConflict:
                    findings.Add(emitter.Emit(ClqBaselineFinding.XrefIdConflict,
                        $"{config.XrefIdColumn}{row}", cur.QuestionNumber, cur.QuestionText,
                        requestedData: null, providedBy: roles.ProvidedBy(cur),
                        $"Identity key and question text disagree about the previous-year question: the key " +
                        $"'{cur.XrefId}' points to previous {Describe(aq.XrefIdCounterpart)}, but the text best " +
                        $"matches previous {Describe(aq.MatcherCandidate)}. Verify which previous question this is."));
                    break;

                case CrossYearOutcome.NewXrefIdWithLookalike:
                    findings.Add(emitter.Emit(ClqBaselineFinding.NewXrefIdResemblesPrevious,
                        $"{config.XrefIdColumn}{row}", cur.QuestionNumber, cur.QuestionText,
                        requestedData: null, providedBy: roles.ProvidedBy(cur),
                        $"New identity key '{cur.XrefId}' (absent from the previous year) but a textual twin exists: " +
                        $"previous {Describe(aq.MatcherCandidate)}. Verify whether this is a rescope or a mistyped key " +
                        "(not auto-used as a baseline)."));
                    break;

                case CrossYearOutcome.SameXrefIdTextDiverged:
                    findings.Add(emitter.Emit(ClqBaselineFinding.SameXrefIdTextDiverged,
                        $"{config.XrefIdColumn}{row}", cur.QuestionNumber, cur.QuestionText,
                        requestedData: null, providedBy: roles.ProvidedBy(cur),
                        $"Identity key '{cur.XrefId}' is shared with previous {Describe(aq.XrefIdCounterpart)} but the " +
                        "question text has diverged beyond the match threshold. Verify whether this is a heavy rewrite " +
                        "or a reused key."));
                    break;

                case CrossYearOutcome.Neither:
                    findings.Add(emitter.Emit(ClqBaselineFinding.NoPreviousBaseline,
                        $"{config.XrefIdColumn}{row}", cur.QuestionNumber, cur.QuestionText,
                        requestedData: null, providedBy: roles.ProvidedBy(cur),
                        "No previous-year counterpart by key or text; ordinary new/orphan question with no cross-year baseline."));
                    break;

                case CrossYearOutcome.Agree:
                {
                    var prev = aq.PreviousMatch!; // non-null IFF Agree

                    // F-integrity: runs on EVERY Agree row, independent of answerUsable.
                    if (!string.Equals(TrimOrEmpty(roles.PreviousAnswer(cur)), TrimOrEmpty(roles.Answer(prev)), StringComparison.Ordinal))
                        findings.Add(emitter.Emit(ClqBaselineFinding.PreviousAnswerAltered,
                            $"{config.PreviousAnswerColumn}{row}", cur.QuestionNumber, cur.QuestionText,
                            requestedData: null, providedBy: roles.ProvidedBy(cur),
                            $"Injected previous answer at {config.PreviousAnswerColumn}{row} ('{TrimOrEmpty(roles.PreviousAnswer(cur))}') " +
                            $"differs from the prior year's actual answer ('{TrimOrEmpty(roles.Answer(prev))}')."));

                    if (answerUsable)
                    {
                        // "usable" = parses as int AND ∈ AllowedAnswers.
                        bool prevUsable = int.TryParse(roles.Answer(prev), out int prevValue)
                                          && config.AllowedAnswers.Contains(roles.Answer(prev)!, StringComparer.Ordinal);
                        bool curNumeric = int.TryParse(answer, out int curValue);

                        if (!prevUsable)
                        {
                            findings.Add(emitter.Emit(ClqBaselineFinding.PreviousAnswerUnusable,
                                $"{config.AnswerColumn}{row}", cur.QuestionNumber, cur.QuestionText,
                                requestedData: null, providedBy: roles.ProvidedBy(cur),
                                "Year-over-year deviation cannot be evaluated: the confidently-matched previous answer is " +
                                "empty or not in the allowed set."));
                        }
                        else if (curNumeric && Math.Abs(curValue - prevValue) >= config.DeviationThreshold)
                        {
                            findings.Add(emitter.Emit(ClqBaselineFinding.AnswerDeviation,
                                $"{config.AnswerColumn}{row}", cur.QuestionNumber, cur.QuestionText,
                                requestedData: null, providedBy: roles.ProvidedBy(cur),
                                $"Answer deviates from the previous year by {Math.Abs(curValue - prevValue)} " +
                                $"(threshold {config.DeviationThreshold}): {prevValue} → {curValue}."));
                        }
                    }
                    break;
                }
            }
        }

        return findings;
    }

    private static bool RefEqual(string? a, string? b) =>
        string.Equals(a ?? "", b ?? "", StringComparison.Ordinal);

    private static string TrimOrEmpty(string? s) => (s ?? "").Trim();

    private static string Describe<T>(T? q) where T : class, IAlignmentIdentity =>
        q is null
            ? "(none)"
            : $"question at row {q.RowNumber} (number '{q.QuestionNumber}', \"{Truncate(q.QuestionText)}\")";

    private static string Truncate(string s, int max = 60) =>
        s.Length <= max ? s : s[..max] + "…";

    private static string WorkbookName(ValidationWorkbook wb) => wb switch
    {
        ValidationWorkbook.CurrentResponse => "the current response",
        ValidationWorkbook.EmptyTemplate => "the empty template",
        ValidationWorkbook.PreviousResponse => "the previous response",
        _ => wb.ToString()
    };
}
