using ItrqTool.Domain.Validation;

namespace ItrqTool.Tasks.ControlLevelQuestionValidation;

// ── CLQ_v01 finding catalogue — the SINGLE SOURCE OF TRUTH ────────────────────
//
// Every finding the validation emits is identified by a ClqFinding enum member.
// Findings are emitted BY ENUM MEMBER, never by magic string. A finding-id is a
// semantic, config-stable string: it must NEVER embed anything configurable (no
// column letters, sheet names, thresholds, or algorithm internals). The id is the
// stable key for SeverityOverrides in the config.
//
// The descriptor table below is the only place the id string, default severity,
// and the ValidationCheck mapping live. The Check is stored here (not derived by
// id prefix) because cross-year.* findings split across two ValidationCheck values
// (Structure and Deviation), so a prefix rule would be wrong.

public enum ClqFinding
{
    XrefIdEmptyOrDuplicated,
    QuestionRemoved,
    QuestionAdded,
    QuestionRowShifted,
    NumberFormatUnrecognized,
    ReferenceTextAltered,
    PreviousAnswerAltered,
    AnswerValidationRuleChanged,
    AnswerMissing,
    AnswerNotInAllowedSet,
    StrengthsMissing,
    WeaknessesMissing,
    XrefIdConflict,
    NewXrefIdResemblesPrevious,
    SameXrefIdTextDiverged,
    NoPreviousBaseline,
    AnswerDeviation,
    PreviousAnswerUnusable
}

public sealed record ClqFindingDescriptor(
    string Id,
    FindingEvaluation DefaultEvaluation,
    ValidationCheck Check,
    string Description);

public static class ClqFindings
{
    private static readonly IReadOnlyDictionary<ClqFinding, ClqFindingDescriptor> ByFinding =
        new Dictionary<ClqFinding, ClqFindingDescriptor>
        {
            [ClqFinding.XrefIdEmptyOrDuplicated] = new(
                "structure.xrefid-empty-or-duplicated",
                FindingEvaluation.Fatal,
                ValidationCheck.Structure,
                "The cross-reference identity key in a question row is blank or duplicated within its workbook, so the question cannot be reliably matched within or across years."),

            [ClqFinding.QuestionRemoved] = new(
                "structure.question-removed",
                FindingEvaluation.Error,
                ValidationCheck.Structure,
                "A question present in the empty template (by identity key) is absent from the organisational unit's response."),

            [ClqFinding.QuestionAdded] = new(
                "structure.question-added",
                FindingEvaluation.Error,
                ValidationCheck.Structure,
                "A question present in the response (by identity key) is absent from the empty template; the response introduced a row the template did not declare."),

            [ClqFinding.QuestionRowShifted] = new(
                "structure.question-row-shifted",
                FindingEvaluation.Error,
                ValidationCheck.Structure,
                "A question matched the template by identity key but appears on a different worksheet row than the template placed it."),

            [ClqFinding.NumberFormatUnrecognized] = new(
                "structure.number-format-unrecognized",
                FindingEvaluation.Warning,
                ValidationCheck.Structure,
                "The question-number prefix is not a recognised two-level form and could not be parsed into a structured number."),

            [ClqFinding.ReferenceTextAltered] = new(
                "static-cell.reference-text-altered",
                FindingEvaluation.Warning,
                ValidationCheck.FrozenValue,
                "Frozen reference text (question text, guidance, identity-key text, chapter, or section) differs from the empty template; these cells are supplied by the auditor and should not be edited by the responder."),

            [ClqFinding.PreviousAnswerAltered] = new(
                "static-cell.previous-answer-altered",
                FindingEvaluation.Warning,
                ValidationCheck.FrozenValue,
                "The injected previous-year answer in the response differs from the prior year's actual answer for the same question."),

            [ClqFinding.AnswerValidationRuleChanged] = new(
                "constraint.answer-validation-rule-changed",
                FindingEvaluation.Error,
                ValidationCheck.FrozenConstraint,
                "The data-validation rule on the answer cell differs from the empty template; the answer constraint was altered."),

            [ClqFinding.AnswerMissing] = new(
                "input-cell.answer-missing",
                FindingEvaluation.Error,
                ValidationCheck.MissingResponse,
                "The answer cell is empty; the question was not answered."),

            [ClqFinding.AnswerNotInAllowedSet] = new(
                "input-cell.answer-not-in-allowed-set",
                FindingEvaluation.Fatal,
                ValidationCheck.MissingResponse,
                "The answer is present but is not one of the configured allowed answers; dependent checks for the row cannot be evaluated."),

            [ClqFinding.StrengthsMissing] = new(
                "input-cell.strengths-missing",
                FindingEvaluation.Error,
                ValidationCheck.MissingResponse,
                "The answer requires a strengths explanation but the strengths cell is empty."),

            [ClqFinding.WeaknessesMissing] = new(
                "input-cell.weaknesses-missing",
                FindingEvaluation.Error,
                ValidationCheck.MissingResponse,
                "The answer requires a weaknesses explanation but the weaknesses cell is empty."),

            [ClqFinding.XrefIdConflict] = new(
                "cross-year.xrefid-conflict",
                FindingEvaluation.Error,
                ValidationCheck.Structure,
                "The identity key and the question text point to different previous-year questions; key and text disagree about which prior question this is."),

            [ClqFinding.NewXrefIdResemblesPrevious] = new(
                "cross-year.new-xrefid-resembles-previous",
                FindingEvaluation.Warning,
                ValidationCheck.Structure,
                "The question carries a new identity key absent from the previous year, yet a textually near-identical previous question exists — possibly a legitimate rescope or a mistyped key."),

            [ClqFinding.SameXrefIdTextDiverged] = new(
                "cross-year.same-xrefid-text-diverged",
                FindingEvaluation.Warning,
                ValidationCheck.Structure,
                "The question shares an identity key with a previous-year question but its text has diverged beyond the match threshold — possibly a heavy rewrite or a reused key that should have been retired."),

            [ClqFinding.NoPreviousBaseline] = new(
                "cross-year.no-previous-baseline",
                FindingEvaluation.Information,
                ValidationCheck.Structure,
                "No previous-year counterpart exists by key or by text; this is an ordinary new or orphan question with no cross-year baseline."),

            [ClqFinding.AnswerDeviation] = new(
                "cross-year.answer-deviation",
                FindingEvaluation.Warning,
                ValidationCheck.Deviation,
                "The answer changed from the confidently-matched previous year by at least the configured deviation threshold."),

            [ClqFinding.PreviousAnswerUnusable] = new(
                "cross-year.previous-answer-unusable",
                FindingEvaluation.Information,
                ValidationCheck.Deviation,
                "A confident previous-year match exists and the current answer is usable, but the previous answer is empty or not in the allowed set, so the year-over-year deviation cannot be evaluated."),
        };

    private static readonly IReadOnlyDictionary<string, ClqFindingDescriptor> ById =
        ByFinding.Values.ToDictionary(d => d.Id, StringComparer.Ordinal);

    /// <summary>The descriptor for a finding (id string, default severity, check, description).</summary>
    public static ClqFindingDescriptor Descriptor(ClqFinding finding) => ByFinding[finding];

    /// <summary>The config-stable id string for a finding.</summary>
    public static string Id(ClqFinding finding) => ByFinding[finding].Id;

    /// <summary>True when <paramref name="id"/> is one of the catalogue's finding-ids.</summary>
    public static bool IsValidId(string id) => ById.ContainsKey(id);

    /// <summary>All valid finding-ids, in catalogue order — for fail-loud diagnostics.</summary>
    public static IReadOnlyList<string> AllIds() => ByFinding.Values.Select(d => d.Id).ToList();
}
