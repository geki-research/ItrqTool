using System.Text;
using System.Text.RegularExpressions;

namespace ItrqTool.Tasks.CellRangeInject;

public record CellPair(int SourceRow, string SourceColumn, int TargetRow, string TargetColumn);

public record CellMappingParseResult(IReadOnlyList<CellPair> Pairs, IReadOnlyList<string> Errors);

public static class CellMappingParser
{
    private static readonly Regex MappingTokenRegex = new(
        @"^[A-Za-z]{1,3}[0-9]+(:[A-Za-z]{1,3}[0-9]+)?->[A-Za-z]{1,3}[0-9]+(:[A-Za-z]{1,3}[0-9]+)?$",
        RegexOptions.Compiled);

    private static readonly Regex CellRegex = new(@"^([A-Za-z]{1,3})([0-9]+)$", RegexOptions.Compiled);

    public static CellMappingParseResult Parse(string mappings)
    {
        var tokens = mappings.Split(';')
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToList();

        if (tokens.Count == 0)
            return new CellMappingParseResult([], ["Parameter 'mappings' contains no valid mapping tokens."]);

        // Normalize optional whitespace around "->" before regex validation.
        // Mirrors CellRangeDiff Trim() idiom but applied to the arrow operator.
        var normalizedTokens = tokens.Select(NormalizeArrow).ToList();

        // Collect malformed tokens; report originals so the user sees what they typed.
        var badTokens = new List<string>();
        for (int i = 0; i < tokens.Count; i++)
        {
            if (!MappingTokenRegex.IsMatch(normalizedTokens[i]))
                badTokens.Add(tokens[i]);
        }
        if (badTokens.Count > 0)
            return new CellMappingParseResult([], [$"Malformed mapping token(s): {string.Join(", ", badTokens)}."]);

        // Process well-formed tokens: dimension check + row-major expansion.
        var errors = new List<string>();
        var pairs = new List<CellPair>();

        for (int i = 0; i < tokens.Count; i++)
        {
            // Split on first "->"; regex guarantees exactly one occurrence.
            int arrowPos = normalizedTokens[i].IndexOf("->", StringComparison.Ordinal);
            var srcSide = normalizedTokens[i][..arrowPos];
            var tgtSide = normalizedTokens[i][(arrowPos + 2)..];

            var (srcMinRow, srcMinCol, srcMaxRow, srcMaxCol) = ParseSide(srcSide);
            var (tgtMinRow, tgtMinCol, tgtMaxRow, tgtMaxCol) = ParseSide(tgtSide);

            int srcRowSpan = srcMaxRow - srcMinRow + 1;
            int srcColSpan = srcMaxCol - srcMinCol + 1;
            int tgtRowSpan = tgtMaxRow - tgtMinRow + 1;
            int tgtColSpan = tgtMaxCol - tgtMinCol + 1;

            if (srcRowSpan != tgtRowSpan || srcColSpan != tgtColSpan)
            {
                errors.Add($"Mapping '{srcSide}->{tgtSide}': source is {srcRowSpan}x{srcColSpan} but target is {tgtRowSpan}x{tgtColSpan}.");
                continue;
            }

            for (int dr = 0; dr < srcRowSpan; dr++)
            {
                for (int dc = 0; dc < srcColSpan; dc++)
                {
                    pairs.Add(new CellPair(
                        SourceRow: srcMinRow + dr,
                        SourceColumn: IndexToCol(srcMinCol + dc),
                        TargetRow: tgtMinRow + dr,
                        TargetColumn: IndexToCol(tgtMinCol + dc)));
                }
            }
        }

        return new CellMappingParseResult(pairs, errors);
    }

    // Strip whitespace around "->" so "B2 -> G2" is accepted like "B2->G2".
    private static string NormalizeArrow(string token)
    {
        int idx = token.IndexOf("->", StringComparison.Ordinal);
        if (idx < 0) return token;
        return token[..idx].TrimEnd() + "->" + token[(idx + 2)..].TrimStart();
    }

    private static (int minRow, int minCol, int maxRow, int maxCol) ParseSide(string side)
    {
        int colonIdx = side.IndexOf(':', StringComparison.Ordinal);
        if (colonIdx < 0)
        {
            var (col, row) = ParseCell(side);
            return (row, col, row, col);
        }
        var (col1, row1) = ParseCell(side[..colonIdx]);
        var (col2, row2) = ParseCell(side[(colonIdx + 1)..]);
        return (Math.Min(row1, row2), Math.Min(col1, col2), Math.Max(row1, row2), Math.Max(col1, col2));
    }

    private static (int colIndex, int row) ParseCell(string a1)
    {
        var m = CellRegex.Match(a1);
        return (ColToIndex(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
    }

    // Base-26 bijective: A=1, Z=26, AA=27, AZ=52, BA=53, …
    private static int ColToIndex(string col)
    {
        int idx = 0;
        foreach (char c in col.ToUpperInvariant())
            idx = idx * 26 + (c - 'A' + 1);
        return idx;
    }

    private static string IndexToCol(int idx)
    {
        var sb = new StringBuilder();
        while (idx > 0)
        {
            idx--;
            sb.Insert(0, (char)('A' + idx % 26));
            idx /= 26;
        }
        return sb.ToString();
    }
}
