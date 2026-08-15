using ItrqTool.Tasks.GeneralDataDiff;

namespace ItrqTool.Tasks.GeneralDataInject;

/// <summary>
/// The GD inject qid-join's single definition of "is this the same question text?" (BLG-0077).
/// Both the aligner (which classifies) and the mapper (which decides whether to warn) go through
/// here, so the two can never drift apart on what "identical" means.
/// </summary>
/// <remarks>
/// It delegates scoring to the <see cref="ItrqTool.Tasks.GeneralDataDiff"/> copy of
/// <c>TextSimilarity</c> — the same-domain copy the GD-diff precedent itself uses, and the one
/// with a dedicated test file (<c>GeneralDataTextSimilarityTests</c>). The
/// <c>QuestionnaireValidation.Alignment</c> copy is deliberately NOT used: reaching into the
/// validation core's namespace from inject blurs a boundary this work respects, and — concretely —
/// <c>GdInjectAligner</c> already imports that namespace for the alignment model, so an unqualified
/// <c>TextSimilarity</c> there would be an ambiguous reference. Confining the
/// <c>using ItrqTool.Tasks.GeneralDataDiff;</c> to this one file removes that ambiguity at source.
/// No copy of TextSimilarity is made here; this is a null-guarding adapter over the existing one.
/// </remarks>
internal static class GdInjectTextComparison
{
    /// <summary>
    /// Ordinal identity — the "unchanged text" test. Null-safe by construction:
    /// <c>string.Equals(null, null, Ordinal)</c> is true and neither argument is dereferenced,
    /// which is exactly how the pre-BLG-0077 qid-join behaved for null texts.
    /// <para>
    /// Deliberately ORDINAL, not <c>Score(a, b) == 1.0</c>. <c>TextSimilarity.Score</c> normalises
    /// (trim, collapse whitespace runs, lowercase-invariant) before scoring, so a case-only or
    /// spacing-only drift scores exactly 1.0 while still being a real change to the workbook.
    /// Under the conservative-input posture such a drift must still be surfaced, so identity is
    /// byte-level and the similarity score is only ever used to decide whether a NON-identical
    /// pair is close enough to keep.
    /// </para>
    /// </summary>
    public static bool AreIdentical(string? current, string? previous)
        => string.Equals(current, previous, StringComparison.Ordinal);

    /// <summary>
    /// Base similarity of the two question texts, in [0.0, 1.0]. This is the RAW
    /// <c>TextSimilarity.Score</c> value — never bonus-adjusted, never rounded up — so a human can
    /// reproduce it from the two texts alone, matching the never-inflate-a-reported-similarity
    /// invariant the neighbouring diff engines hold.
    /// <para>
    /// Null guard: <c>TextSimilarity.Score</c> dereferences both arguments (<c>s.Trim()</c> inside
    /// its <c>Normalize</c>) and therefore throws on null. A null text is coerced to empty, which
    /// folds it into Score's own convention — both empty scores 1.0, one empty scores 0.0 — so an
    /// absent text can never throw and can never be silently treated as a match against a real one.
    /// </para>
    /// </summary>
    public static double Score(string? current, string? previous)
        => TextSimilarity.Score(current ?? "", previous ?? "");
}
