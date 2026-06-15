using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;

namespace ItrqTool.Tasks.QuestionnaireValidation.Clq;

// ── CLQ baseline descriptor table (CLQ specialization) ───────────────────────
//
// Maps each ClqBaselineFinding to its generic FindingDescriptor. The Id string,
// DefaultEvaluation, Check, and Description are copied VERBATIM from CLQ_v01's
// ClqFindings table — this is an independent faithful copy and does NOT reference
// v01's ClqFinding/ClqFindings.
//
// The Check is stored here (not derived by id prefix) because cross-year.* findings
// split across two ValidationCheck values (Structure and Deviation).

public static class ClqBaselineFindings
{
    private static readonly IReadOnlyDictionary<ClqBaselineFinding, FindingDescriptor> ByFinding =
        new Dictionary<ClqBaselineFinding, FindingDescriptor>
        {
            [ClqBaselineFinding.XrefIdEmptyOrDuplicated] = new(
                "structure.xrefid-empty-or-duplicated",
                FindingEvaluation.Fatal,
                ValidationCheck.Structure,
                "The cross-reference identity key in a question row is blank or duplicated within its workbook, so the question cannot be reliably matched within or across years."),

            [ClqBaselineFinding.QuestionRemoved] = new(
                "structure.question-removed",
                FindingEvaluation.Error,
                ValidationCheck.Structure,
                "A question present in the empty template (by identity key) is absent from the organisational unit's response."),

            [ClqBaselineFinding.QuestionAdded] = new(
                "structure.question-added",
                FindingEvaluation.Error,
                ValidationCheck.Structure,
                "A question present in the response (by identity key) is absent from the empty template; the response introduced a row the template did not declare."),

            [ClqBaselineFinding.QuestionRowShifted] = new(
                "structure.question-row-shifted",
                FindingEvaluation.Error,
                ValidationCheck.Structure,
                "A question matched the template by identity key but appears on a different worksheet row than the template placed it."),

            [ClqBaselineFinding.NumberFormatUnrecognized] = new(
                "structure.number-format-unrecognized",
                FindingEvaluation.Warning,
                ValidationCheck.Structure,
                "The question-number prefix is not a recognised two-level form and could not be parsed into a structured number."),

            [ClqBaselineFinding.ReferenceTextAltered] = new(
                "static-cell.reference-text-altered",
                FindingEvaluation.Warning,
                ValidationCheck.FrozenValue,
                "Frozen reference text (question text, guidance, identity-key text, chapter, or section) differs from the empty template; these cells are supplied by the auditor and should not be edited by the responder."),

            [ClqBaselineFinding.PreviousAnswerAltered] = new(
                "static-cell.previous-answer-altered",
                FindingEvaluation.Warning,
                ValidationCheck.FrozenValue,
                "The injected previous-year answer in the response differs from the prior year's actual answer for the same question."),

            [ClqBaselineFinding.AnswerValidationRuleChanged] = new(
                "constraint.answer-validation-rule-changed",
                FindingEvaluation.Error,
                ValidationCheck.FrozenConstraint,
                "The data-validation rule on the answer cell differs from the empty template; the answer constraint was altered."),

            [ClqBaselineFinding.AnswerMissing] = new(
                "input-cell.answer-missing",
                FindingEvaluation.Error,
                ValidationCheck.MissingResponse,
                "The answer cell is empty; the question was not answered."),

            [ClqBaselineFinding.AnswerNotInAllowedSet] = new(
                "input-cell.answer-not-in-allowed-set",
                FindingEvaluation.Fatal,
                ValidationCheck.MissingResponse,
                "The answer is present but is not one of the configured allowed answers; dependent checks for the row cannot be evaluated."),

            [ClqBaselineFinding.StrengthsMissing] = new(
                "input-cell.strengths-missing",
                FindingEvaluation.Error,
                ValidationCheck.MissingResponse,
                "The answer requires a strengths explanation but the strengths cell is empty."),

            [ClqBaselineFinding.WeaknessesMissing] = new(
                "input-cell.weaknesses-missing",
                FindingEvaluation.Error,
                ValidationCheck.MissingResponse,
                "The answer requires a weaknesses explanation but the weaknesses cell is empty."),

            [ClqBaselineFinding.XrefIdConflict] = new(
                "cross-year.xrefid-conflict",
                FindingEvaluation.Error,
                ValidationCheck.Structure,
                "The identity key and the question text point to different previous-year questions; key and text disagree about which prior question this is."),

            [ClqBaselineFinding.NewXrefIdResemblesPrevious] = new(
                "cross-year.new-xrefid-resembles-previous",
                FindingEvaluation.Warning,
                ValidationCheck.Structure,
                "The question carries a new identity key absent from the previous year, yet a textually near-identical previous question exists — possibly a legitimate rescope or a mistyped key."),

            [ClqBaselineFinding.SameXrefIdTextDiverged] = new(
                "cross-year.same-xrefid-text-diverged",
                FindingEvaluation.Warning,
                ValidationCheck.Structure,
                "The question shares an identity key with a previous-year question but its text has diverged beyond the match threshold — possibly a heavy rewrite or a reused key that should have been retired."),

            [ClqBaselineFinding.NoPreviousBaseline] = new(
                "cross-year.no-previous-baseline",
                FindingEvaluation.Information,
                ValidationCheck.Structure,
                "No previous-year counterpart exists by key or by text; this is an ordinary new or orphan question with no cross-year baseline."),

            [ClqBaselineFinding.AnswerDeviation] = new(
                "cross-year.answer-deviation",
                FindingEvaluation.Warning,
                ValidationCheck.Deviation,
                "The answer changed from the confidently-matched previous year by at least the configured deviation threshold."),

            [ClqBaselineFinding.PreviousAnswerUnusable] = new(
                "cross-year.previous-answer-unusable",
                FindingEvaluation.Information,
                ValidationCheck.Deviation,
                "A confident previous-year match exists and the current answer is usable, but the previous answer is empty or not in the allowed set, so the year-over-year deviation cannot be evaluated."),
        };

    /// <summary>The descriptor for a CLQ baseline finding (id string, default severity, check, description).</summary>
    public static FindingDescriptor Descriptor(ClqBaselineFinding finding) => ByFinding[finding];

    /// <summary>All CLQ baseline descriptors, in enum order — the set handed to a FindingCatalogue.</summary>
    public static IReadOnlyList<FindingDescriptor> All { get; } =
        Enum.GetValues<ClqBaselineFinding>().Select(f => ByFinding[f]).ToList();
}
