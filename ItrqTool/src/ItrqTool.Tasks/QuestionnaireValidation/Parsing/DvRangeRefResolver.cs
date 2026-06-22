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
/// directly and dispatches on source kind:
/// <list type="bullet">
///   <item><b>RangeRef</b> — calls <see cref="IExcelStructureReader.ReadCells"/> on the
///     backing A1 range.</item>
///   <item><b>NamedRange</b> — calls
///     <see cref="IExcelStructureReader.ResolveDefinedNameValues"/> with the bare name
///     (leading <c>=</c> stripped); returns <c>null</c> if absent or all-blank →
///     <c>NotCheckable</c>.</item>
///   <item><b>Inline</b> — already resolved in the profile's <c>ApplyDv</c> lambda
///     (5a-iii); the <c>currentListValuesSelector</c> non-null guard skips it here.</item>
/// </list>
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

                // Gate: List DV, not already resolved by the inline path.
                if (!string.Equals(dvType, "List", StringComparison.OrdinalIgnoreCase))
                    return q;
                if (currentListValuesSelector(q) is not null)
                    return q;
                if (string.IsNullOrEmpty(dvFormula))
                    return q;
                var kind = DvListParser.ClassifySource(dvFormula);
                if (kind == DvListSourceKind.RangeRef)
                {
                    var (resolvedSheet, a1Range) = ParseRangeRefFormula(dvFormula, dvCellSheetName);

                    var cells = reader.ReadCells(filePath, resolvedSheet, [a1Range]);
                    var rangeValues = cells.Values
                        .Select(c => c.TextValue)
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .Select(t => t!.Trim())
                        .ToList();

                    // All-empty backing range: leave null → NotCheckable (never false-positive).
                    if (rangeValues.Count == 0) return q;

                    return stampListValues(q, rangeValues);
                }
                else if (kind == DvListSourceKind.NamedRange)
                {
                    var name = dvFormula.Trim();
                    if (name.StartsWith('=')) name = name[1..].Trim();

                    var nameValues = reader.ResolveDefinedNameValues(filePath, dvCellSheetName, name);
                    if (nameValues is null || nameValues.Count == 0) return q;   // absent/empty → NotCheckable
                    return stampListValues(q, nameValues);
                }
                return q;
            })
            .ToList();
    }

    // Parses a (=-stripped, trimmed) DV range-ref formula into (sheetName, a1Range).
    // Handles: "Lists!$A$1:$A$2", "Sheet1!A1:A3", "'My Sheet'!$A$1:$A$2", "'O''Brien'!$A$1:$A$2",
    // "A1:A3" (same-sheet). $-stripping on both sheetPart and rangePart is always applied.
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

            // Unquote: strip surrounding single-quotes (e.g. 'My Sheet' → My Sheet),
            // then collapse escaped '' → ' (e.g. 'O''Brien' → O'Brien).
            if (sheetPart.StartsWith('\'') && sheetPart.EndsWith('\'') && sheetPart.Length >= 2)
                sheetPart = sheetPart[1..^1];
            sheetPart = sheetPart.Replace("''", "'");

            sheetPart = sheetPart.Replace("$", "");
            rangePart = rangePart.Replace("$", "");

            return (sheetPart, rangePart);
        }

        // Bare A1 range with no sheet qualifier — same sheet as the DV cell.
        return (fallbackSheet, s.Replace("$", ""));
    }
}
