using System.Globalization;
using System.Text.RegularExpressions;

namespace ItrqTool.Tasks.Shared;

/// <summary>
/// Shared rendering for the "this question's text drifted since last year" Warning that all three
/// inject mappers raise on a non-identical Agree (BLG-0079). One home, three consumers —
/// <c>GdInjectMapper</c>, <c>RlqInjectMapper</c>, <c>ClqInjectMapper</c> — following the precedent
/// of <see cref="InjectionValueGuard"/>, which already sits here and is already consumed by all
/// three. Only the CONTENT rendering is shared; each track keeps its own message phrasing and
/// addressing idiom (RLQ/CLQ lead with <c>Row {n} (xref …)</c>, GD with <c>Question {xref} (row …)</c>).
/// </summary>
public static class DriftMessageFormatter
{
    /// <summary>Longest excerpt emitted per question text, in characters.</summary>
    private const int MaxExcerpt = 160;

    /// <summary>Context kept either side of the differing middle, in characters.</summary>
    private const int ContextMargin = 60;

    private static readonly Regex WhitespaceRuns = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Ordinal identity — the "unchanged text" test, and the trigger for the Warning.
    /// Null-safe: <c>string.Equals(null, null, Ordinal)</c> is true and neither argument is
    /// dereferenced.
    /// <para>
    /// Deliberately ORDINAL, not <c>score == 1.0</c>. The similarity scorer normalises (trim,
    /// collapse whitespace, lowercase-invariant) before scoring, so a case-only or spacing-only
    /// drift scores exactly 1.0 while still being a real change to the workbook. Under the
    /// conservative-input posture such a drift must still be surfaced.
    /// </para>
    /// </summary>
    public static bool AreIdentical(string? current, string? previous)
        => string.Equals(current, previous, StringComparison.Ordinal);

    /// <summary>
    /// Renders a similarity score for a message. FOUR decimals and INVARIANT culture, both
    /// deliberate: four so a near-boundary score stays distinguishable from the threshold it was
    /// tested against, and invariant so the decimal separator is a point on every machine — these
    /// workbooks are read under comma-decimal locales, and a "0,63" in a log line invites exactly
    /// the misreading the decimal work has been unpicking elsewhere.
    /// A null score renders "unknown" rather than 0, which would read as "completely dissimilar" —
    /// a materially wrong statement.
    /// </summary>
    public static string FormatScore(double? score)
        => score?.ToString("0.0000", CultureInfo.InvariantCulture) ?? "unknown";

    /// <summary>
    /// Excerpts BOTH question texts around the point where they actually differ, so a reader sees
    /// the drift rather than two identical-looking heads.
    /// </summary>
    /// <remarks>
    /// Head-truncation (the original BLG-0077 behaviour) fails exactly where it matters most: for a
    /// long question whose wording changed late in the string — a trailing "as of 2024" → "as of
    /// 2025" — both texts truncate to the same first 160 characters and the message shows the
    /// reader nothing. This centres the window on the differing middle instead.
    /// <para>
    /// Algorithm, per pair:
    /// <list type="number">
    /// <item>Flatten both (collapse whitespace runs to one space, trim). The window indices are
    ///   computed on the FLATTENED forms, so both refer to the same normalization.</item>
    /// <item>Ordinal scan for the common prefix length, then for the common suffix length.</item>
    /// <item>Clamp the suffix so prefix + suffix never exceeds the SHORTER string — otherwise
    ///   "aaa" vs "aaaa" would claim 3 common leading and 3 common trailing characters out of 3.</item>
    /// <item>Per text: if it fits whole within <see cref="MaxExcerpt"/>, return it UNCHANGED — no
    ///   window, no ellipsis. Otherwise take the differing middle plus
    ///   <see cref="ContextMargin"/> either side, clamped to <see cref="MaxExcerpt"/>, and mark
    ///   whichever side was cut with an ellipsis.</item>
    /// </list>
    /// A linear prefix/suffix scan only — no alignment, no edit script, no matcher.
    /// </para>
    /// </remarks>
    public static (string Current, string Previous) Excerpts(string? current, string? previous)
    {
        var c = Flatten(current);
        var p = Flatten(previous);

        var shorter = Math.Min(c.Length, p.Length);

        var prefix = 0;
        while (prefix < shorter && c[prefix] == p[prefix])
            prefix++;

        var suffix = 0;
        while (suffix < shorter - prefix &&
               c[c.Length - 1 - suffix] == p[p.Length - 1 - suffix])
            suffix++;

        return (Window(c, prefix, suffix), Window(p, prefix, suffix));
    }

    /// <summary>Whitespace-flattened form. Exposed for callers that need the text without a window.</summary>
    public static string Flatten(string? text)
        => WhitespaceRuns.Replace(text ?? "", " ").Trim();

    // Cuts one text down to its differing middle plus context. prefix/suffix are the pair's common
    // run lengths, already clamped by the caller, so both windows line up on the same drift.
    private static string Window(string text, int prefix, int suffix)
    {
        if (text.Length <= MaxExcerpt)
            return text;   // fits whole — untouched, per the contract above

        var driftStart = Math.Min(prefix, text.Length);
        var driftEnd   = Math.Max(driftStart, text.Length - suffix);

        var start = Math.Max(0, driftStart - ContextMargin);
        var end   = Math.Min(text.Length, driftEnd + ContextMargin);

        // Keep the overall bound so one message stays one readable line. Trimming from the END
        // preserves the leading context, which is where the drift begins.
        if (end - start > MaxExcerpt)
            end = start + MaxExcerpt;

        var body = text[start..end];
        return (start > 0 ? "…" : "") + body + (end < text.Length ? "…" : "");
    }
}
