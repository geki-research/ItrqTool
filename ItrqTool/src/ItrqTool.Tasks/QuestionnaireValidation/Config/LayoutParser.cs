using ItrqTool.Tasks.QuestionnaireValidation.Parsing;

namespace ItrqTool.Tasks.QuestionnaireValidation.Config;

/// <summary>
/// Parses the ChapterRows and SectionRows string formats from a version config into
/// a <see cref="QuestionnaireLayout"/>. Faithfully ports CLQ_v01's parsing rules.
/// Invalid entries throw <see cref="FormatException"/> (same as v01).
/// </summary>
public static class LayoutParser
{
    /// <summary>
    /// Parses chapter-row integer strings and section-row range strings into a layout.
    /// </summary>
    /// <param name="chapterRows">Each entry is a positive-integer row number string.</param>
    /// <param name="sectionRows">Each entry is in <c>"&lt;sectionRow&gt;:&lt;first&gt;-&lt;last&gt;"</c> format.</param>
    /// <param name="chapterNameColumn">Column letter used to read chapter names from chapter header rows.</param>
    /// <param name="sectionNameColumn">Column letter used to read section names from section header rows.</param>
    /// <param name="questionTextColumn">Column the shared parser tests for blankness to detect question rows.</param>
    public static QuestionnaireLayout Parse(
        IReadOnlyList<string> chapterRows,
        IReadOnlyList<string> sectionRows,
        string chapterNameColumn,
        string sectionNameColumn,
        string questionTextColumn)
    {
        var chapters = chapterRows.Select(e => ParseChapterEntry(e, chapterNameColumn)).ToList();
        var sections = sectionRows.Select(e => ParseSectionEntry(e, sectionNameColumn)).ToList();
        return new QuestionnaireLayout(questionTextColumn, chapters, sections);
    }

    private static LayoutChapter ParseChapterEntry(string entry, string nameColumn)
    {
        if (!int.TryParse(entry, out int rowNumber) || rowNumber <= 0)
            throw new FormatException(
                $"Chapter row entry '{entry}' must be a positive integer.");
        return new LayoutChapter(rowNumber, nameColumn);
    }

    private static LayoutSection ParseSectionEntry(string entry, string nameColumn)
    {
        var colonIdx = entry.IndexOf(':');
        if (colonIdx < 1)
            throw new FormatException(
                $"Section entry '{entry}' must be in format '<sectionRow>:<first>-<last>'.");

        var dashIdx = entry.IndexOf('-', colonIdx + 1);
        if (dashIdx < 0)
            throw new FormatException(
                $"Section entry '{entry}' must be in format '<sectionRow>:<first>-<last>'.");

        if (!int.TryParse(entry[..colonIdx], out int sectionRow) || sectionRow <= 0)
            throw new FormatException(
                $"Section entry '{entry}': sectionRow must be a positive integer.");

        if (!int.TryParse(entry[(colonIdx + 1)..dashIdx], out int first) || first <= 0)
            throw new FormatException(
                $"Section entry '{entry}': firstQuestionRow must be a positive integer.");

        if (!int.TryParse(entry[(dashIdx + 1)..], out int last) || last <= 0)
            throw new FormatException(
                $"Section entry '{entry}': lastQuestionRow must be a positive integer.");

        if (first <= sectionRow)
            throw new FormatException(
                $"Section entry '{entry}': firstQuestionRow ({first}) must be greater than sectionRow ({sectionRow}).");

        if (last < first)
            throw new FormatException(
                $"Section entry '{entry}': lastQuestionRow ({last}) must not be less than firstQuestionRow ({first}).");

        return new LayoutSection(sectionRow, first, last, nameColumn);
    }
}
