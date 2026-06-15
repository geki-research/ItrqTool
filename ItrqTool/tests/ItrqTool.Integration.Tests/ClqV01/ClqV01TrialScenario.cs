using ItrqTool.Domain.Validation;

namespace ItrqTool.Integration.Tests.ClqV01;

/// <summary>
/// One expected finding: matched by Check + Evaluation + CellAddresses,
/// disambiguated by a substring of the CheckResult text.
/// </summary>
public sealed record ClqV01ExpectedFinding(
    ValidationCheck Check,
    FindingEvaluation Evaluation,
    string CellAddresses,
    string CheckResultSubstring);

/// <summary>
/// What <see cref="ClqV01TrialScenario.Build"/> returns: the perturbed trio,
/// any current-workbook DV overrides (key = row number, value = DV formula string),
/// and the findings the exact-set test must assert.
/// </summary>
public sealed record ClqV01TrialResult(
    ClqV01BaselineTrio Trio,
    IReadOnlyDictionary<int, string> CurrentAnswerDvOverrides,
    IReadOnlyList<ClqV01ExpectedFinding> ExpectedFindings);

/// <summary>
/// One row's perturbation across the three workbooks plus the findings it is expected to
/// produce. A null mutation means the question at that row is kept at baseline for that
/// workbook. 4c-2 extends the scenario by passing additional perturbations to
/// <see cref="ClqV01TrialScenario.Build"/>.
/// </summary>
public sealed record ClqV01RowPerturbation(
    int RowNumber,
    Func<ClqV01QuestionSpec, ClqV01QuestionSpec>? CurrentMutation,
    Func<ClqV01QuestionSpec, ClqV01QuestionSpec>? TemplateMutation,
    Func<ClqV01QuestionSpec, ClqV01QuestionSpec>? PreviousMutation,
    string? CurrentAnswerDvOverride,
    IReadOnlyList<ClqV01ExpectedFinding> ExpectedFindings);

/// <summary>
/// Builds the ten-row local trial scenario (4c-1). 4c-2 passes additional structural-row
/// perturbations via <paramref name="extraPerturbations"/>.
/// </summary>
public static class ClqV01TrialScenario
{
    public static ClqV01TrialResult Build(
        ClqV01BaselineTrio baseline,
        IReadOnlyList<ClqV01RowPerturbation>? extraPerturbations = null)
    {
        var local = LocalPerturbations(baseline);
        IReadOnlyList<ClqV01RowPerturbation> all = extraPerturbations is null
            ? local
            : [.. local, .. extraPerturbations];
        return Apply(baseline, all);
    }

    // ── ten local perturbations (4c-1) ──────────────────────────────────────────

    private static List<ClqV01RowPerturbation> LocalPerturbations(ClqV01BaselineTrio baseline)
    {
        const string altText59 =
            "[test] Backup copies of business-critical data are validated through scheduled " +
            "restoration exercises and retained at a geographically separate recovery location.";
        const string altGuidance59 =
            "[test] Confirm that restoration tests cover both full and incremental backups " +
            "and that the outcomes are formally documented and reviewed.";

        // Three-level prefix: "3.1) text" → "3.1.1) text".
        // HasUnsupportedDeeperPrefix triggers on ^\d+(\.\d+){2,} matching "3.1.1".
        var q88   = baseline.Current.Questions.Single(q => q.RowNumber == 88);
        var paren = q88.OriginalText.IndexOf(')');
        var altText88 = paren >= 0
            ? q88.OriginalText.Insert(paren, ".1")
            : q88.OriginalText + ".1";

        return
        [
            // R7 — answer deviation (|4-1|=3 ≥ threshold 2)
            // previous.Answer="1" keeps F-integrity clean (cur.PreviousAnswer="1"==prev.Answer="1").
            new(7,
                CurrentMutation:  q => q with { Answer = "4", PreviousAnswer = "1" },
                TemplateMutation: null,
                PreviousMutation: q => q with { Answer = "1" },
                CurrentAnswerDvOverride: null,
                ExpectedFindings:
                [
                    new(ValidationCheck.Deviation, FindingEvaluation.Warning, "H7", "1 → 4")
                ]),

            // R12 — previous-answer altered (F column frozen-value)
            new(12,
                CurrentMutation:  q => q with { PreviousAnswer = "4" },
                TemplateMutation: null,
                PreviousMutation: null,
                CurrentAnswerDvOverride: null,
                ExpectedFindings:
                [
                    new(ValidationCheck.FrozenValue, FindingEvaluation.Warning, "F12",
                        "('4') differs from the prior year's actual answer ('2')")
                ]),

            // R18 — previous-answer unusable (prev.Answer="" → deviation unevaluable)
            // Both cur.PreviousAnswer="" and prev.Answer="" → F-integrity is clean (both blank).
            new(18,
                CurrentMutation:  q => q with { Answer = "3", PreviousAnswer = "" },
                TemplateMutation: null,
                PreviousMutation: q => q with { Answer = "" },
                CurrentAnswerDvOverride: null,
                ExpectedFindings:
                [
                    new(ValidationCheck.Deviation, FindingEvaluation.Information, "H18",
                        "cannot be evaluated")
                ]),

            // R23 — answer missing
            new(23,
                CurrentMutation:  q => q with { Answer = "" },
                TemplateMutation: null,
                PreviousMutation: null,
                CurrentAnswerDvOverride: null,
                ExpectedFindings:
                [
                    new(ValidationCheck.MissingResponse, FindingEvaluation.Error, "H23",
                        "H23 is empty")
                ]),

            // R30 — answer not in allowed set (Fatal) + previous-answer altered (Warning)
            // Deliberately keeps cur.PreviousAnswer≠prev.Answer to guard the combined check.
            new(30,
                CurrentMutation:  q => q with { Answer = "9", PreviousAnswer = "1" },
                TemplateMutation: null,
                PreviousMutation: q => q with { Answer = "3" },
                CurrentAnswerDvOverride: null,
                ExpectedFindings:
                [
                    new(ValidationCheck.MissingResponse, FindingEvaluation.Fatal, "H30",
                        "'9' at H30 is not in the allowed set"),
                    new(ValidationCheck.FrozenValue, FindingEvaluation.Warning, "F30",
                        "('1') differs from the prior year's actual answer ('3')")
                ]),

            // R42 — strengths missing (answer "1" requires strengths; "1" does not require weaknesses)
            new(42,
                CurrentMutation:  q => q with { Answer = "1", Strengths = "" },
                TemplateMutation: null,
                PreviousMutation: null,
                CurrentAnswerDvOverride: null,
                ExpectedFindings:
                [
                    new(ValidationCheck.MissingResponse, FindingEvaluation.Error, "I42",
                        "'1' requires a strengths")
                ]),

            // R48 — weaknesses missing (answer "4" requires weaknesses); previous aligned
            // prev.Answer="3"=cur.PreviousAnswer → F-integrity clean; |4-3|=1 < 2 → no deviation.
            new(48,
                CurrentMutation:  q => q with { Answer = "4", Weaknesses = "", PreviousAnswer = "3" },
                TemplateMutation: null,
                PreviousMutation: q => q with { Answer = "3" },
                CurrentAnswerDvOverride: null,
                ExpectedFindings:
                [
                    new(ValidationCheck.MissingResponse, FindingEvaluation.Error, "J48",
                        "'4' requires a weaknesses")
                ]),

            // R59 — reference-text altered (text + guidance vs template)
            // previous mirrors current's text so cross-year remains Agree (no SameXrefIdTextDiverged).
            // previous guidance stays baseline; cross-year ignores E.
            new(59,
                CurrentMutation:  q => q with { OriginalText = altText59, Guidance = altGuidance59 },
                TemplateMutation: null,
                PreviousMutation: q => q with { OriginalText = altText59 },
                CurrentAnswerDvOverride: null,
                ExpectedFindings:
                [
                    new(ValidationCheck.FrozenValue, FindingEvaluation.Warning, "D59, E59",
                        "question text, guidance")
                ]),

            // R68 — answer-validation rule changed (narrowed DV "1,2,3" vs template "1,2,3,4")
            new(68,
                CurrentMutation:  null,
                TemplateMutation: null,
                PreviousMutation: null,
                CurrentAnswerDvOverride: "\"1,2,3\"",
                ExpectedFindings:
                [
                    new(ValidationCheck.FrozenConstraint, FindingEvaluation.Error, "H68",
                        "H68 differs from the template")
                ]),

            // R88 — number-format unrecognized (three-level prefix triggers HasUnsupportedDeeperPrefix)
            // All three workbooks get the same altered text → no reference-text or cross-year findings.
            new(88,
                CurrentMutation:  q => q with { OriginalText = altText88 },
                TemplateMutation: q => q with { OriginalText = altText88 },
                PreviousMutation: q => q with { OriginalText = altText88 },
                CurrentAnswerDvOverride: null,
                ExpectedFindings:
                [
                    new(ValidationCheck.Structure, FindingEvaluation.Warning, "D88",
                        "D88 is not a recognised two-level form")
                ]),
        ];
    }

    // ── apply all perturbations to the baseline ──────────────────────────────────

    private static ClqV01TrialResult Apply(
        ClqV01BaselineTrio baseline, IReadOnlyList<ClqV01RowPerturbation> perturbations)
    {
        var cQs = baseline.Current.Questions.ToList();
        var tQs = baseline.Template.Questions.ToList();
        var pQs = baseline.Previous.Questions.ToList();
        var dvOverrides = new Dictionary<int, string>();
        var findings    = new List<ClqV01ExpectedFinding>();

        foreach (var p in perturbations)
        {
            if (p.CurrentMutation  != null) MutateAt(cQs, p.RowNumber, p.CurrentMutation);
            if (p.TemplateMutation != null) MutateAt(tQs, p.RowNumber, p.TemplateMutation);
            if (p.PreviousMutation != null) MutateAt(pQs, p.RowNumber, p.PreviousMutation);
            if (p.CurrentAnswerDvOverride != null) dvOverrides[p.RowNumber] = p.CurrentAnswerDvOverride;
            findings.AddRange(p.ExpectedFindings);
        }

        var trio = new ClqV01BaselineTrio(
            baseline.Current  with { Questions = cQs },
            baseline.Template with { Questions = tQs },
            baseline.Previous with { Questions = pQs });

        return new ClqV01TrialResult(trio, dvOverrides, findings);
    }

    // ── structural perturbations (4c-2) — six rows proving cross-year identity logic ──

    /// <summary>
    /// Builds the six structural-row perturbations added in 4c-2.
    /// Kept here so both the direct-task tests and the end-to-end workflow test
    /// can call <see cref="Build"/> with the same extra perturbations without duplication.
    /// </summary>
    public static IReadOnlyList<ClqV01RowPerturbation> BuildStructuralPerturbations(
        ClqV01BaselineTrio baseline)
    {
        const string NoiseText102 =
            "[test] A documented data classification scheme is applied consistently across all " +
            "information assets and is reviewed at least annually by the information security function.";
        const string NoiseText161 =
            "[test] Logical access to production systems is granted strictly on a least-privilege " +
            "basis and is recertified by the respective asset owners on a defined periodic cadence.";
        const string NewKey179    = "QOLD179";

        var q214XrefId = baseline.Current.Questions.Single(q => q.RowNumber == 214).XrefId!;

        return
        [
            new(102,
                CurrentMutation:  q => q with { XrefId = "QNEW102", OriginalText = NoiseText102 },
                TemplateMutation: null,
                PreviousMutation: null,
                CurrentAnswerDvOverride: null,
                ExpectedFindings:
                [
                    new(ValidationCheck.Structure, FindingEvaluation.Error, "N102",
                        "identity key 'Q083'"),
                    new(ValidationCheck.Structure, FindingEvaluation.Error, "N102",
                        "identity key 'QNEW102'"),
                    new(ValidationCheck.Structure, FindingEvaluation.Information, "N102",
                        "no cross-year baseline"),
                ]),

            new(130,
                CurrentMutation:  null,
                TemplateMutation: null,
                PreviousMutation: q => q with { XrefId = "QOLD130" },
                CurrentAnswerDvOverride: null,
                ExpectedFindings:
                [
                    new(ValidationCheck.Structure, FindingEvaluation.Warning, "N130",
                        "'Q108' (absent from the previous year)"),
                ]),

            new(161,
                CurrentMutation:  q => q with { OriginalText = NoiseText161 },
                TemplateMutation: q => q with { OriginalText = NoiseText161 },
                PreviousMutation: null,
                CurrentAnswerDvOverride: null,
                ExpectedFindings:
                [
                    new(ValidationCheck.Structure, FindingEvaluation.Warning, "N161",
                        "text has diverged beyond the match threshold"),
                ]),

            new(179,
                CurrentMutation:  q => q with { XrefId = NewKey179 },
                TemplateMutation: q => q with { XrefId = NewKey179 },
                PreviousMutation: null,
                CurrentAnswerDvOverride: null,
                ExpectedFindings:
                [
                    new(ValidationCheck.Structure, FindingEvaluation.Error, "N179",
                        "Identity key and question text disagree"),
                ]),

            new(210,
                CurrentMutation:  q => q with { XrefId = "" },
                TemplateMutation: null,
                PreviousMutation: q => q with { XrefId = NewKey179 },
                CurrentAnswerDvOverride: null,
                ExpectedFindings:
                [
                    new(ValidationCheck.Structure, FindingEvaluation.Fatal, "N210",
                        "row 210 is blank"),
                    new(ValidationCheck.Structure, FindingEvaluation.Error, "N210",
                        "identity key 'Q172'"),
                ]),

            new(215,
                CurrentMutation:  q => q with { XrefId = q214XrefId },
                TemplateMutation: null,
                PreviousMutation: null,
                CurrentAnswerDvOverride: null,
                ExpectedFindings:
                [
                    new(ValidationCheck.Structure, FindingEvaluation.Fatal, "N214",
                        "row 214 is duplicated"),
                    new(ValidationCheck.Structure, FindingEvaluation.Fatal, "N215",
                        "row 215 is duplicated"),
                    new(ValidationCheck.Structure, FindingEvaluation.Error, "N214",
                        "identity key 'Q176', template row 214"),
                    new(ValidationCheck.Structure, FindingEvaluation.Error, "N215",
                        "identity key 'Q177'"),
                ]),
        ];
    }

    private static void MutateAt(
        List<ClqV01QuestionSpec> list, int rowNumber,
        Func<ClqV01QuestionSpec, ClqV01QuestionSpec> mutation)
    {
        int i = list.FindIndex(q => q.RowNumber == rowNumber);
        if (i < 0) throw new InvalidOperationException($"No question spec at row {rowNumber}.");
        list[i] = mutation(list[i]);
    }
}
