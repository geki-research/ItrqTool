using ItrqTool.Tasks.QuestionnaireValidation.Alignment;

namespace ItrqTool.Tasks.GeneralDataValidationV01;

// ── GdV01Aligner ─────────────────────────────────────────────────────────────
//
// Cross-year alignment for GD_v01: a pure qid-join fork — no Hungarian matcher,
// no similarity matrix. Every question in a GdV01ParseResult has a valid, unique
// qid (the parser routes blank/duplicate/unparseable rows to Malformed), so the
// dictionary lookups are safe and duplicate-free.
//
// Within-year  : current vs template, qid-join.
// Cross-year   : current vs previous, qid-join only; Agree / SameXrefIdTextDiverged / Neither.
// Malformed    : maps all three workbooks' GdMalformedXref lists to MalformedKey (1:1 Reason).
//
// XrefIdConflict and NewXrefIdWithLookalike are structurally impossible without the
// Hungarian matcher and are never emitted.
public static class GdV01Aligner
{
    public static AlignmentResult<GdV01Question> Align(
        GdV01ParseResult current,
        GdV01ParseResult template,
        GdV01ParseResult previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(previous);

        // ── Lookup tables ─────────────────────────────────────────────────
        var templateByQid = template.Questions
            .ToDictionary(q => q.XrefId!, StringComparer.Ordinal);
        var previousByQid = previous.Questions
            .ToDictionary(q => q.XrefId!, StringComparer.Ordinal);
        var currentQids   = current.Questions
            .Select(q => q.XrefId!)
            .ToHashSet(StringComparer.Ordinal);

        // ── Aligned (one per current question, input order) ───────────────
        var aligned = new List<AlignedQuestion<GdV01Question>>(current.Questions.Count);
        foreach (var q in current.Questions)
        {
            // Within-year
            WithinYearJoin withinYear;
            GdV01Question? templateMatch;
            bool rowShifted;
            bool textMismatched;

            if (templateByQid.TryGetValue(q.XrefId!, out var tq))
            {
                withinYear    = WithinYearJoin.JoinedByXrefId;
                templateMatch = tq;
                rowShifted    = tq.RowNumber != q.RowNumber;
                textMismatched = !string.Equals(q.OriginalText, tq.OriginalText, StringComparison.Ordinal);
            }
            else
            {
                withinYear    = WithinYearJoin.AddedInResponse;
                templateMatch = null;
                rowShifted    = false;
                textMismatched = false;
            }

            // Cross-year
            CrossYearOutcome crossYear;
            GdV01Question? previousMatch;
            GdV01Question? xrefIdCounterpart;

            if (previousByQid.TryGetValue(q.XrefId!, out var pv))
            {
                xrefIdCounterpart = pv;
                if (string.Equals(q.OriginalText, pv.OriginalText, StringComparison.Ordinal))
                {
                    crossYear     = CrossYearOutcome.Agree;
                    previousMatch = pv;
                }
                else
                {
                    crossYear     = CrossYearOutcome.SameXrefIdTextDiverged;
                    previousMatch = null;
                }
            }
            else
            {
                crossYear         = CrossYearOutcome.Neither;
                previousMatch     = null;
                xrefIdCounterpart = null;
            }

            aligned.Add(new AlignedQuestion<GdV01Question>(
                Current:          q,
                WithinYear:       withinYear,
                TemplateMatch:    templateMatch,
                RowShifted:       rowShifted,
                TextMismatched:   textMismatched,
                CrossYear:        crossYear,
                PreviousMatch:    previousMatch,
                XrefIdCounterpart: xrefIdCounterpart,
                MatcherCandidate: null,
                MatcherBaseScore: null));
        }

        // ── WithinYearRemoved (template questions absent from current, template order) ──
        var withinYearRemoved = template.Questions
            .Where(q => !currentQids.Contains(q.XrefId!))
            .ToList();

        // ── Malformed keys (current, then template, then previous) ────────
        var malformedKeys = new List<MalformedKey>(
            current.Malformed.Count + template.Malformed.Count + previous.Malformed.Count);
        foreach (var x in current.Malformed)
            malformedKeys.Add(new MalformedKey(ValidationWorkbook.CurrentResponse,  x.RowNumber, x.XrefId, Map(x.Reason)));
        foreach (var x in template.Malformed)
            malformedKeys.Add(new MalformedKey(ValidationWorkbook.EmptyTemplate,    x.RowNumber, x.XrefId, Map(x.Reason)));
        foreach (var x in previous.Malformed)
            malformedKeys.Add(new MalformedKey(ValidationWorkbook.PreviousResponse, x.RowNumber, x.XrefId, Map(x.Reason)));

        return new AlignmentResult<GdV01Question>(aligned, withinYearRemoved, malformedKeys);
    }

    private static MalformedKeyReason Map(GdMalformedXrefReason r) => r switch
    {
        GdMalformedXrefReason.Blank       => MalformedKeyReason.Blank,
        GdMalformedXrefReason.Duplicate   => MalformedKeyReason.Duplicate,
        GdMalformedXrefReason.Unparseable => MalformedKeyReason.Unparseable,
        _ => throw new ArgumentOutOfRangeException(nameof(r), r, null)
    };
}
