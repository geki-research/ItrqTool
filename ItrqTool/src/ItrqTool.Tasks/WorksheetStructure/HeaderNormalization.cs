using System.Text.RegularExpressions;

namespace ItrqTool.Tasks.WorksheetStructure;

/// <summary>
/// Header comparison normalization (locked design): collapse ALL whitespace runs — spaces, tabs, and
/// embedded newlines (<c>\r</c>, <c>\n</c>, <c>\r\n</c>) — to a single space, trim, then ToUpperInvariant.
/// This absorbs the template's CR/LF inconsistency, double-spaces, and trailing spaces so a header
/// matches regardless of those. The "↓" glyph (U+2193) is NOT whitespace and is preserved on both sides.
/// </summary>
public static class HeaderNormalization
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static string Normalize(string? value) =>
        Whitespace.Replace(value ?? "", " ").Trim().ToUpperInvariant();
}
