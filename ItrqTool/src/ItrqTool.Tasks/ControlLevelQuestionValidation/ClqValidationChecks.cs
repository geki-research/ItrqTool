using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.Shared;

namespace ItrqTool.Tasks.ControlLevelQuestionValidation;

/// <summary>
/// Pure checks/report-builder for CLQ_v01. Turns a <see cref="ClqAlignmentResult"/>
/// (plus the config) into a flat list of <see cref="ValidationFinding"/>. It reads no
/// files and throws for no data condition — every anomaly is a finding, never an
/// exception. Severity per finding is resolved at emit time:
/// <c>SeverityOverrides.GetValueOrDefault(id, default)</c>.
/// </summary>
public static class ClqValidationChecks
{
    public static IReadOnlyList<ValidationFinding> Build(
        ClqAlignmentResult alignment,
        ControlLevelQuestionValidationV01Config config)
    {
        var findings = new List<ValidationFinding>();

        // ── 5.1 Malformed keys (all three workbooks) ─────────────────────────────
        foreach (var mk in alignment.MalformedKeys)
        {
            var reason = mk.Reason == MalformedKeyReason.Blank
                ? "blank"
                : $"duplicated ('{mk.XrefId}')";
            findings.Add(Make(ClqFinding.XrefIdEmptyOrDuplicated, config,
                $"{config.XrefIdColumn}{mk.RowNumber}",
                questionNumber: null, questionText: null, providedBy: null,
                $"Identity key in {WorkbookName(mk.Workbook)} at row {mk.RowNumber} is {reason}; " +
                "the question cannot be reliably matched within or across years."));
        }

        // ── 5.2 Within-year removed (template questions absent from the response) ──
        foreach (var removed in alignment.WithinYearRemoved)
        {
            findings.Add(Make(ClqFinding.QuestionRemoved, config,
                $"{config.XrefIdColumn}{removed.RowNumber}",
                questionNumber: removed.QuestionNumber, questionText: removed.QuestionText, providedBy: null,
                $"Template question (identity key '{removed.XrefId}', template row {removed.RowNumber}) " +
                "is absent from the response."));
        }

        // ── Per current-response question ────────────────────────────────────────
        foreach (var aq in alignment.Aligned)
        {
            // A current row whose OWN key is malformed is already covered by the
            // MalformedKeys finding above; skip all its dependent checks (emit once).
            if (aq.WithinYear == WithinYearJoin.NotEvaluatedMalformedKey)
                continue;

            var cur = aq.Current;
            int row = cur.RowNumber;

            // 5.2 Number-format
            if (cur.NumberFormatUnrecognized)
                findings.Add(Make(ClqFinding.NumberFormatUnrecognized, config,
                    $"{config.TextColumn}{row}", cur.QuestionNumber, cur.QuestionText, cur.ProvidedBy,
                    $"Question-number prefix at {config.TextColumn}{row} is not a recognised two-level " +
                    $"form: '{cur.OriginalText}'."));

            // 5.2 Within-year structure
            if (aq.WithinYear == WithinYearJoin.AddedInResponse)
            {
                findings.Add(Make(ClqFinding.QuestionAdded, config,
                    $"{config.XrefIdColumn}{row}", cur.QuestionNumber, cur.QuestionText, cur.ProvidedBy,
                    $"Response question (identity key '{cur.XrefId}', row {row}) is absent from the empty template."));
            }
            else if (aq.WithinYear == WithinYearJoin.JoinedByXrefId)
            {
                var tmpl = aq.TemplateMatch!; // non-null IFF JoinedByXrefId

                if (aq.RowShifted)
                    findings.Add(Make(ClqFinding.QuestionRowShifted, config,
                        $"{config.XrefIdColumn}{row}", cur.QuestionNumber, cur.QuestionText, cur.ProvidedBy,
                        $"Question (identity key '{cur.XrefId}') moved from template row {tmpl.RowNumber} " +
                        $"to response row {row}."));

                // Reference text — WHOLE compare: one finding listing every differing field.
                var alteredFields = new List<string>();
                var alteredCols = new List<string>();
                if (aq.TextMismatched) { alteredFields.Add("question text"); alteredCols.Add(config.TextColumn); }
                if (!RefEqual(cur.Guidance, tmpl.Guidance)) { alteredFields.Add("guidance"); alteredCols.Add(config.GuidanceColumn); }
                if (!RefEqual(cur.ChapterName, tmpl.ChapterName)) { alteredFields.Add("chapter"); alteredCols.Add(config.TextColumn); }
                if (!RefEqual(cur.SectionName, tmpl.SectionName)) { alteredFields.Add("section"); alteredCols.Add(config.TextColumn); }
                if (!RefEqual(cur.XrefId, tmpl.XrefId)) { alteredFields.Add("identity-key text"); alteredCols.Add(config.XrefIdColumn); }

                if (alteredFields.Count > 0)
                {
                    var cells = string.Join(", ",
                        alteredCols.Distinct(StringComparer.Ordinal).Select(c => $"{c}{row}"));
                    findings.Add(Make(ClqFinding.ReferenceTextAltered, config,
                        cells, cur.QuestionNumber, cur.QuestionText, cur.ProvidedBy,
                        $"Frozen reference text differs from the template in: {string.Join(", ", alteredFields)}."));
                }

                // Frozen constraint — answer-cell data-validation rule.
                if (DvComparer.IsDvChangedFull(
                        tmpl.AnswerDvType, tmpl.AnswerDvOperator, tmpl.AnswerDvFormula, tmpl.AnswerDvFormula2,
                        cur.AnswerDvType, cur.AnswerDvOperator, cur.AnswerDvFormula, cur.AnswerDvFormula2))
                {
                    findings.Add(Make(ClqFinding.AnswerValidationRuleChanged, config,
                        $"{config.AnswerColumn}{row}", cur.QuestionNumber, cur.QuestionText, cur.ProvidedBy,
                        $"Answer-cell data-validation rule at {config.AnswerColumn}{row} differs from the template."));
                }
            }

            // ── 5.3 Input validity ───────────────────────────────────────────────
            var answer = cur.Answer;
            bool answerUsable = false;

            if (string.IsNullOrWhiteSpace(answer))
            {
                findings.Add(Make(ClqFinding.AnswerMissing, config,
                    $"{config.AnswerColumn}{row}", cur.QuestionNumber, cur.QuestionText, cur.ProvidedBy,
                    $"Answer cell {config.AnswerColumn}{row} is empty; the question was not answered."));
            }
            else if (!config.AllowedAnswers.Contains(answer, StringComparer.Ordinal))
            {
                findings.Add(Make(ClqFinding.AnswerNotInAllowedSet, config,
                    $"{config.AnswerColumn}{row}", cur.QuestionNumber, cur.QuestionText, cur.ProvidedBy,
                    $"Answer '{answer}' at {config.AnswerColumn}{row} is not in the allowed set " +
                    $"[{string.Join(", ", config.AllowedAnswers)}]."));
            }
            else
            {
                answerUsable = true;

                // Conditional I/J matrix, keyed on the answer string.
                bool requireStrengths = answer is "1" or "2" or "3";
                bool requireWeaknesses = answer is "2" or "3" or "4";

                if (requireStrengths && string.IsNullOrWhiteSpace(cur.Strengths))
                    findings.Add(Make(ClqFinding.StrengthsMissing, config,
                        $"{config.StrengthsColumn}{row}", cur.QuestionNumber, cur.QuestionText, cur.ProvidedBy,
                        $"Answer '{answer}' requires a strengths explanation but {config.StrengthsColumn}{row} is empty."));

                if (requireWeaknesses && string.IsNullOrWhiteSpace(cur.Weaknesses))
                    findings.Add(Make(ClqFinding.WeaknessesMissing, config,
                        $"{config.WeaknessesColumn}{row}", cur.QuestionNumber, cur.QuestionText, cur.ProvidedBy,
                        $"Answer '{answer}' requires a weaknesses explanation but {config.WeaknessesColumn}{row} is empty."));
            }

            // ── 5.4 Cross-year ───────────────────────────────────────────────────
            switch (aq.CrossYear)
            {
                case CrossYearOutcome.XrefIdConflict:
                    findings.Add(Make(ClqFinding.XrefIdConflict, config,
                        $"{config.XrefIdColumn}{row}", cur.QuestionNumber, cur.QuestionText, cur.ProvidedBy,
                        $"Identity key and question text disagree about the previous-year question: the key " +
                        $"'{cur.XrefId}' points to previous {Describe(aq.XrefIdCounterpart)}, but the text best " +
                        $"matches previous {Describe(aq.MatcherCandidate)}. Verify which previous question this is."));
                    break;

                case CrossYearOutcome.NewXrefIdWithLookalike:
                    findings.Add(Make(ClqFinding.NewXrefIdResemblesPrevious, config,
                        $"{config.XrefIdColumn}{row}", cur.QuestionNumber, cur.QuestionText, cur.ProvidedBy,
                        $"New identity key '{cur.XrefId}' (absent from the previous year) but a textual twin exists: " +
                        $"previous {Describe(aq.MatcherCandidate)}. Verify whether this is a rescope or a mistyped key " +
                        "(not auto-used as a baseline)."));
                    break;

                case CrossYearOutcome.SameXrefIdTextDiverged:
                    findings.Add(Make(ClqFinding.SameXrefIdTextDiverged, config,
                        $"{config.XrefIdColumn}{row}", cur.QuestionNumber, cur.QuestionText, cur.ProvidedBy,
                        $"Identity key '{cur.XrefId}' is shared with previous {Describe(aq.XrefIdCounterpart)} but the " +
                        "question text has diverged beyond the match threshold. Verify whether this is a heavy rewrite " +
                        "or a reused key."));
                    break;

                case CrossYearOutcome.Neither:
                    findings.Add(Make(ClqFinding.NoPreviousBaseline, config,
                        $"{config.XrefIdColumn}{row}", cur.QuestionNumber, cur.QuestionText, cur.ProvidedBy,
                        "No previous-year counterpart by key or text; ordinary new/orphan question with no cross-year baseline."));
                    break;

                case CrossYearOutcome.Agree:
                {
                    var prev = aq.PreviousMatch!; // non-null IFF Agree

                    // F-integrity: injected previous answer (col F) vs prior year's actual answer
                    // (prev col H). This is a frozen-value check on column F; it does NOT depend on
                    // whether THIS year's answer (col H) was filled in or is valid — it runs on every
                    // Agree row, independent of the current answer's usability.
                    if (!string.Equals(TrimOrEmpty(cur.PreviousAnswer), TrimOrEmpty(prev.Answer), StringComparison.Ordinal))
                        findings.Add(Make(ClqFinding.PreviousAnswerAltered, config,
                            $"{config.PreviousAnswerColumn}{row}", cur.QuestionNumber, cur.QuestionText, cur.ProvidedBy,
                            $"Injected previous answer at {config.PreviousAnswerColumn}{row} ('{TrimOrEmpty(cur.PreviousAnswer)}') " +
                            $"differs from the prior year's actual answer ('{TrimOrEmpty(prev.Answer)}')."));

                    // Deviation is gated on the CURRENT answer being usable (∈ AllowedAnswers and
                    // numerically comparable) — a deviation can't be computed otherwise.
                    if (answerUsable)
                    {
                        // "usable" = parses as int AND ∈ AllowedAnswers.
                        bool prevUsable = int.TryParse(prev.Answer, out int prevValue)
                                          && config.AllowedAnswers.Contains(prev.Answer!, StringComparer.Ordinal);
                        bool curNumeric = int.TryParse(cur.Answer, out int curValue);

                        if (!prevUsable)
                        {
                            findings.Add(Make(ClqFinding.PreviousAnswerUnusable, config,
                                $"{config.AnswerColumn}{row}", cur.QuestionNumber, cur.QuestionText, cur.ProvidedBy,
                                "Year-over-year deviation cannot be evaluated: the confidently-matched previous answer is " +
                                "empty or not in the allowed set."));
                        }
                        else if (curNumeric && Math.Abs(curValue - prevValue) >= config.DeviationThreshold)
                        {
                            findings.Add(Make(ClqFinding.AnswerDeviation, config,
                                $"{config.AnswerColumn}{row}", cur.QuestionNumber, cur.QuestionText, cur.ProvidedBy,
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

    // ── helpers ──────────────────────────────────────────────────────────────────

    private static ValidationFinding Make(
        ClqFinding finding,
        ControlLevelQuestionValidationV01Config config,
        string cellAddresses,
        string? questionNumber,
        string? questionText,
        string? providedBy,
        string checkResult)
    {
        var d = ClqFindings.Descriptor(finding);
        var evaluation = config.SeverityOverrides.GetValueOrDefault(d.Id, d.DefaultEvaluation);
        return new ValidationFinding(
            Check: d.Check,
            Evaluation: evaluation,
            CellAddresses: cellAddresses,
            QuestionNumber: questionNumber,
            QuestionText: questionText,
            RequestedData: null,            // CLQ leaves col F (requested data) blank
            ProvidedBy: providedBy,
            CheckResult: checkResult);
    }

    private static bool RefEqual(string? a, string? b) =>
        string.Equals(a ?? "", b ?? "", StringComparison.Ordinal);

    private static string TrimOrEmpty(string? s) => (s ?? "").Trim();

    private static string WorkbookName(ClqWorkbook wb) => wb switch
    {
        ClqWorkbook.CurrentResponse => "the current response",
        ClqWorkbook.EmptyTemplate => "the empty template",
        ClqWorkbook.PreviousResponse => "the previous response",
        _ => wb.ToString()
    };

    private static string Describe(InternalClqQuestion? q) =>
        q is null
            ? "(none)"
            : $"question at row {q.RowNumber} (number '{q.QuestionNumber}', \"{Truncate(q.QuestionText)}\")";

    private static string Truncate(string s, int max = 60) =>
        s.Length <= max ? s : s[..max] + "…";
}
