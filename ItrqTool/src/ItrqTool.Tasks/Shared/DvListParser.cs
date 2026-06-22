using System.Text.RegularExpressions;

namespace ItrqTool.Tasks.Shared;

public enum DvListSourceKind { Inline, RangeRef, NamedRange }

/// <summary>
/// Shared parsing/classification for a data-validation List source string
/// (<c>ExcelCellStructure.DataValidationFormula</c> / ClosedXML <c>dv.Value</c>). Lifts the
/// inline-list parse idiom previously private to <c>DvComparer</c> / <c>DvDisplayFormatter</c>
/// into one place, and adds a PRECISE source classifier.
/// <para>
/// RESOLUTION of range-ref / named-range sources to their values is deliberately NOT here —
/// that is patch-phase IO (finding 5a-ii / 5b). This helper only classifies a source and parses
/// the inline case; the classifier is what the patch phase will branch on.
/// </para>
/// </summary>
public static class DvListParser
{
    // A1[:A1] range shape WITHOUT '$'/'!' — e.g. "A1", "D1:D3". Sheet-qualified or absolute
    // refs are caught earlier by the '!'/'$' test, so this only needs the bare-range form.
    private static readonly Regex A1RangeShape =
        new(@"^[A-Za-z]{1,3}[0-9]+(:[A-Za-z]{1,3}[0-9]+)?$", RegexOptions.Compiled);

    /// <summary>
    /// Parses an INLINE list source (e.g. <c>"\"Yes,No\""</c> or <c>"Yes, No"</c>) into its
    /// members: strips surrounding double-quotes, splits on <c>','</c>, trims each, drops empties.
    /// Source order is preserved (membership testing is order-insensitive anyway).
    /// </summary>
    public static IReadOnlyList<string> ParseInline(string dvFormula)
    {
        if (string.IsNullOrEmpty(dvFormula)) return [];
        var s = dvFormula.Trim().Trim('"');
        return s.Split(',')
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToList();
    }

    /// <summary>
    /// Classifies a List source string. The rule is PRECISE — deliberately NOT
    /// <c>DvComparer.IsInlineList</c>'s loose <c>'$'</c>-only heuristic, which misclassifies a
    /// bare named range (no <c>'$'</c>) as inline:
    /// <list type="bullet">
    ///   <item><b>RangeRef</b> — contains <c>'!'</c> (sheet-qualified) or <c>'$'</c> (absolute
    ///     ref), or matches a bare A1[:A1] range shape (e.g. <c>"D1:D3"</c>). These point AT
    ///     cells.</item>
    ///   <item><b>Inline</b> — a quoted literal, or contains <c>','</c> with no <c>'!'</c>/<c>'$'</c>
    ///     (an explicit value list).</item>
    ///   <item><b>NamedRange</b> — anything else: a bare identifier (e.g. <c>"MyList"</c>) with no
    ///     separators and no A1 shape.</item>
    /// </list>
    /// A leading <c>'='</c> (defensive — <c>dv.Value</c> usually omits it) is stripped first.
    /// </summary>
    public static DvListSourceKind ClassifySource(string dvFormula)
    {
        if (string.IsNullOrEmpty(dvFormula)) return DvListSourceKind.Inline;

        var s = dvFormula.Trim();
        if (s.StartsWith('=')) s = s[1..].Trim();

        if (s.Contains('!') || s.Contains('$'))
            return DvListSourceKind.RangeRef;

        if (s.StartsWith('"') || s.Contains(','))
            return DvListSourceKind.Inline;

        if (A1RangeShape.IsMatch(s))
            return DvListSourceKind.RangeRef;

        return DvListSourceKind.NamedRange;
    }
}
