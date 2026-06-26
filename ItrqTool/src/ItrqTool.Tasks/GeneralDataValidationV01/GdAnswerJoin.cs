using ItrqTool.Tasks.QuestionnaireValidation.Alignment;

namespace ItrqTool.Tasks.GeneralDataValidationV01;

// ── GdAnswerJoin — the GD per-ANSWER counterpart join (the C1 load-bearing novelty) ──
//
// GD aligns at QID grain (one GdV01Question → AlignedQuestion<GdV01Question>), but every
// template/previous-needing per-answer check (frozen-constraint, conformance, deviation) must
// pair each CURRENT answer to its TEMPLATE or PREVIOUS counterpart *by AnswerId* before it can
// compare them. The qid-grain AlignmentResult does NOT carry that answer-grain pairing — so it is
// done here, once, in a pure helper that all the C2 per-answer checks share rather than each
// re-implementing the join (and risking a different key rule).
//
// Pairing contract:
//   - key = AnswerId ?? ""  (the bare-qid collapsed answer keys to ""). AnswerIds are unique
//     within a question (the parser captures one GdAnswer per (qid, aid)), so the counterpart
//     side is a per-id lookup; a defensive first-wins TryAdd tolerates any unexpected dup.
//   - current answer order is preserved — the result is one AnswerPair per current answer.
//   - a current answer with no counterpart on the other side → Counterpart == null.
//   - a counterpart answer with no current match is NOT emitted (checks emit per CURRENT cell).
//   - a null match side (TemplateMatch null when not JoinedByXrefId; PreviousMatch null when not
//     Agree) → every pair has Counterpart == null. Callers that require a counterpart (frozen,
//     conformance, deviation) simply skip the null-counterpart pairs.
//
// Pure: no I/O, no allocation beyond the result list + lookup. The qid-grain gate
// (WithinYear / CrossYear) stays the caller's responsibility — this helper only joins answers.

/// <summary>One current answer paired with its template/previous counterpart (null when absent).</summary>
public readonly record struct GdAnswerPair(GdAnswer Current, GdAnswer? Counterpart);

public static class GdAnswerJoin
{
    /// <summary>
    /// Pairs each current answer to its TEMPLATE counterpart by AnswerId. Counterpart source is
    /// <see cref="AlignedQuestion{T}.TemplateMatch"/> (non-null only for JoinedByXrefId rows);
    /// null/absent template → every pair has a null counterpart.
    /// </summary>
    public static IReadOnlyList<GdAnswerPair> ToTemplate(AlignedQuestion<GdV01Question> aq)
    {
        ArgumentNullException.ThrowIfNull(aq);
        return Pair(aq.Current.Answers, aq.TemplateMatch?.Answers);
    }

    /// <summary>
    /// Pairs each current answer to its PREVIOUS counterpart by AnswerId. Counterpart source is
    /// <see cref="AlignedQuestion{T}.PreviousMatch"/> (non-null only for the cross-year Agree
    /// outcome); null/absent previous → every pair has a null counterpart.
    /// </summary>
    public static IReadOnlyList<GdAnswerPair> ToPrevious(AlignedQuestion<GdV01Question> aq)
    {
        ArgumentNullException.ThrowIfNull(aq);
        return Pair(aq.Current.Answers, aq.PreviousMatch?.Answers);
    }

    private static IReadOnlyList<GdAnswerPair> Pair(
        IReadOnlyList<GdAnswer> current,
        IReadOnlyList<GdAnswer>? counterpart)
    {
        if (counterpart is null || counterpart.Count == 0)
            return current.Select(a => new GdAnswerPair(a, null)).ToList();

        var byId = new Dictionary<string, GdAnswer>(StringComparer.Ordinal);
        foreach (var a in counterpart)
            byId.TryAdd(a.AnswerId ?? "", a);   // unique within a question; first-wins is defensive

        return current
            .Select(a => new GdAnswerPair(a, byId.GetValueOrDefault(a.AnswerId ?? "")))
            .ToList();
    }
}
