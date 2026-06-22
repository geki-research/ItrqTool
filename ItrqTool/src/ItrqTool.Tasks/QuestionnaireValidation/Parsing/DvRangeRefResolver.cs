using ItrqTool.Domain;
using ItrqTool.Tasks.Shared;

namespace ItrqTool.Tasks.QuestionnaireValidation.Parsing;

/// <summary>
/// Resolves range-ref List DV sources (e.g. <c>Lists!$A$1:$A$2</c>) to their backing-cell
/// text values and stamps them onto the question record, mirroring <see cref="DvPatcher"/>'s
/// shape. Complements the inline-List path wired in the profile's <c>ApplyDv</c> lambda
/// (5a-iii) — that path yields <c>null</c> for range-ref sources, which this pass then fills.
/// </summary>
/// <remarks>
/// Designed for a post-<see cref="DvPatcher"/> pass inside a per-workbook read/parse/patch
/// method. At call time the question records already carry their patched DV fields
/// (<c>DvType</c>, <c>DvFormula</c>, …); this class reads the DV formula off the record
/// directly and calls <see cref="IExcelStructureReader.ReadCells"/> only for the backing cells.
/// <para>
/// <b>NamedRange is left to 5b.</b> <see cref="DvListParser.ClassifySource"/> is used as the
/// gate; NamedRange formulas pass through unchanged, leaving <c>*DvListValues</c> null →
/// <c>DvConformanceResult.NotCheckable</c> — never a false positive.
/// </para>
/// </remarks>
public static class DvRangeRefResolver
{
    /// <summary>
    /// For each question whose List DV source is a <see cref="DvListSourceKind.RangeRef"/>,
    /// reads the backing cells and stamps the resolved values via <paramref name="stampListValues"/>.
    /// Questions where the inline path already resolved the list (non-null
    /// <paramref name="currentListValuesSelector"/> result) are left unchanged.
    /// Questions with an empty backing range (all blank cells) are also left unchanged so the
    /// conformance evaluator treats them as NotCheckable rather than always-conformant.
    /// </summary>
    public static IReadOnlyList<T> Resolve<T>(
        IExcelStructureReader reader,
        string filePath,
        string dvCellSheetName,
        IReadOnlyList<T> questions,
        Func<T, string?> dvTypeSelector,
        Func<T, string?> dvFormulaSelector,
        Func<T, IReadOnlyList<string>?> currentListValuesSelector,
        Func<T, IReadOnlyList<string>, T> stampListValues)
        where T : class
    {
        if (questions.Count == 0) return questions;

        return questions
            .Select(q =>
            {
                var dvType    = dvTypeSelector(q);
                var dvFormula = dvFormulaSelector(q);

                // Gate: List DV, not already resolved by inline path, and source is a RangeRef.
                if (!string.Equals(dvType, "List", StringComparison.OrdinalIgnoreCase))
                    return q;
                if (currentListValuesSelector(q) is not null)
                    return q;
                if (string.IsNullOrEmpty(dvFormula))
                    return q;
                if (DvListParser.ClassifySource(dvFormula) != DvListSourceKind.RangeRef)
                    return q;

                var (resolvedSheet, a1Range) = ParseRangeRefFormula(dvFormula, dvCellSheetName);

                var cells = reader.ReadCells(filePath, resolvedSheet, [a1Range]);
                var values = cells.Values
                    .Select(c => c.TextValue)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t!.Trim())
                    .ToList();

                // All-empty backing range: leave null → NotCheckable (never false-positive).
                if (values.Count == 0) return q;

                return stampListValues(q, values);
            })
            .ToList();
    }

    // Parses a (=-stripped, trimmed) DV range-ref formula into (sheetName, a1Range).
    // Handles: "Lists!$A$1:$A$2", "Sheet1!A1:A3", "'My Sheet'!$A$1:$A$2", "A1:A3" (same-sheet).
    // $-stripping on both sheetPart and rangePart is always applied.
    // BL-023: doubled '' escape inside quoted sheet names is not handled; only the outer
    // '…' quotes are stripped. A sheet name containing a literal apostrophe is not supported.
    public static (string Sheet, string Range) ParseRangeRefFormula(
        string dvFormula, string fallbackSheet)
    {
        var s = dvFormula.Trim();
        if (s.StartsWith('=')) s = s[1..].Trim();

        if (s.Contains('!'))
        {
            var bangIdx = s.LastIndexOf('!');
            var sheetPart = s[..bangIdx];
            var rangePart = s[(bangIdx + 1)..];

            // Unquote: strip surrounding single-quotes (e.g. 'My Sheet' → My Sheet).
            // TODO(BL-023): doubled '' escape inside quoted sheet names not handled.
            if (sheetPart.StartsWith('\'') && sheetPart.EndsWith('\'') && sheetPart.Length >= 2)
                sheetPart = sheetPart[1..^1];

            sheetPart = sheetPart.Replace("$", "");
            rangePart = rangePart.Replace("$", "");

            return (sheetPart, rangePart);
        }

        // Bare A1 range with no sheet qualifier — same sheet as the DV cell.
        return (fallbackSheet, s.Replace("$", ""));
    }
}
