namespace ItrqTool.Tasks.QuestionnaireValidation.Clq;

// ── CLQ baseline config read-surface ─────────────────────────────────────────
//
// The version-neutral config surface the CLQ baseline checks (ClqBaselineChecks)
// consume. Column letters are config-driven — every cell address a check builds is
// composed from one of these letters plus a row number; no column is ever
// hard-coded. v02's concrete config (a later chunk) implements this interface; the
// baseline checks depend only on the interface.
//
// The FULL baseline read-surface is declared here even though D1a uses only a
// subset, so D1b/D1c need not widen it. (D1a consumes none of these directly — it
// reads XrefIdColumn and TextColumn; the rest are declared for the within-year
// structure and input-validity checks that follow.)

public interface IClqBaselineConfig
{
    string TextColumn { get; }
    string GuidanceColumn { get; }
    string XrefIdColumn { get; }
    string AnswerColumn { get; }
    string StrengthsColumn { get; }
    string WeaknessesColumn { get; }
    string PreviousAnswerColumn { get; }

    IReadOnlyList<string> AllowedAnswers { get; }
    int DeviationThreshold { get; }
}
