using System.IO;
using ItrqTool.Tasks.ControlLevelQuestionValidationV01;
using ItrqTool.Tasks.QuestionnaireValidation.Config;

namespace ItrqTool.Integration.Tests.ClqV01;

/// <summary>Describes one workbook: its header rows and its question rows.</summary>
/// <param name="ChapterHeaders">Row → D-cell text for each chapter header row.</param>
/// <param name="SectionHeaders">Row → D-cell text for each section header row.</param>
/// <param name="Questions">Per-question specs in sheet order. 4c replaces entries with <c>with</c> expressions before writing.</param>
public sealed record ClqV01WorkbookDescriptor(
    IReadOnlyList<(int Row, string Text)> ChapterHeaders,
    IReadOnlyList<(int Row, string Text)> SectionHeaders,
    IReadOnlyList<ClqV01QuestionSpec> Questions
);

/// <summary>The three workbooks produced by <see cref="ClqV01BaselineFactory.Build"/>.</summary>
public sealed record ClqV01BaselineTrio(
    ClqV01WorkbookDescriptor Current,
    ClqV01WorkbookDescriptor Template,
    ClqV01WorkbookDescriptor Previous
);

/// <summary>
/// Builds a fully-consistent baseline trio against a <see cref="ClqV01Config"/>.
/// Every question satisfies all validation checks so the baseline produces zero findings.
/// </summary>
/// <remarks>
/// Baseline guarantee (no findings):
/// <list type="bullet">
/// <item>All XrefIds unique and non-empty across all three workbooks.</item>
/// <item>D/E/N and section/chapter text identical in current and template (no ReferenceTextAltered).</item>
/// <item>No DV on H cells in either workbook — both read as null DvType, so IsDvChangedFull returns false.</item>
/// <item>current.H = "2" ∈ AllowedAnswers; strengths and weaknesses both filled (answer 2 requires both).</item>
/// <item>current.F = "2" = previous.H (PreviousAnswerAltered check passes).</item>
/// <item>|current.H − previous.H| = 0 &lt; DeviationThreshold = 2.</item>
/// <item>Identical texts ensure Hungarian assigns each current question to its XrefId-matched previous (Agree).</item>
/// </list>
/// </remarks>
public static class ClqV01BaselineFactory
{
    private const string Answer     = "2";   // valid; answer "2" requires both strengths and weaknesses
    private const string Strengths  = "Baseline strengths.";
    private const string Weaknesses = "Baseline weaknesses.";
    private const string ProvidedBy = "TestOrgUnit";

    // Real audit questions, one per line, prefix already stripped.
    // Question index i (0-based, global generation order) gets line i as its base text.
    // Fail loudly on startup if the resource is missing or not exactly 193 lines.
    private static readonly IReadOnlyList<string> _realQuestions = LoadRealQuestions();

    private static IReadOnlyList<string> LoadRealQuestions()
    {
        const string ResourceName =
            "ItrqTool.Integration.Tests.ClqV01.clq-v01-trial-questions.txt";
        using var stream = typeof(ClqV01BaselineFactory).Assembly
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' not found. " +
                "Ensure 'ClqV01\\clq-v01-trial-questions.txt' is marked EmbeddedResource in the .csproj.");
        using var reader = new StreamReader(stream);
        var lines = reader.ReadToEnd()
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToArray();
        if (lines.Length != 193)
            throw new InvalidOperationException(
                $"Expected exactly 193 questions in embedded resource '{ResourceName}'; got {lines.Length}.");
        return lines;
    }

    private static string QuestionText(string prefix, int globalQ)
        => $"{prefix}) {_realQuestions[globalQ - 1]}";

    public static ClqV01BaselineTrio Build(ClqV01Config config)
    {
        // ChapterRows are strings on the core config; parse to int for numeric row ordering.
        var sortedChapters = config.ChapterRows.Select(int.Parse).OrderBy(r => r).ToList();

        // Sections derive at run via LayoutParser (no ParsedSections property on the core config).
        var parsedSections = LayoutParser.Parse(
            config.ChapterRows, config.SectionRows,
            config.TextColumn, config.TextColumn, config.TextColumn).Sections;

        // Pre-build chapter headers (same text across all three workbooks).
        var chapterHeaders = sortedChapters
            .Select((row, i) => (Row: row, Text: $"Chapter {i + 1}"))
            .ToList();

        // Pre-build section headers: sections belong to the chapter whose row precedes them.
        var sectionHeaders = new List<(int Row, string Text)>();
        for (int ci = 0; ci < sortedChapters.Count; ci++)
        {
            int thisChapter = sortedChapters[ci];
            int nextChapter = ci + 1 < sortedChapters.Count ? sortedChapters[ci + 1] : int.MaxValue;

            var sectionsInChapter = parsedSections
                .Where(s => s.SectionRow > thisChapter && s.SectionRow < nextChapter)
                .OrderBy(s => s.SectionRow)
                .ToList();

            for (int si = 0; si < sectionsInChapter.Count; si++)
                sectionHeaders.Add((sectionsInChapter[si].SectionRow, $"Section {ci + 1}.{si + 1}"));
        }

        // Build question specs.
        var currentQs  = new List<ClqV01QuestionSpec>();
        var templateQs = new List<ClqV01QuestionSpec>();
        var previousQs = new List<ClqV01QuestionSpec>();

        int globalQ = 0;
        for (int ci = 0; ci < sortedChapters.Count; ci++)
        {
            int thisChapter = sortedChapters[ci];
            int nextChapter = ci + 1 < sortedChapters.Count ? sortedChapters[ci + 1] : int.MaxValue;

            var sectionsInChapter = parsedSections
                .Where(s => s.SectionRow > thisChapter && s.SectionRow < nextChapter)
                .OrderBy(s => s.SectionRow)
                .ToList();

            int qInChapter = 0;
            foreach (var section in sectionsInChapter)
            {
                for (int row = section.FirstQuestionRow; row <= section.LastQuestionRow; row++)
                {
                    globalQ++;
                    qInChapter++;
                    var xrefId    = $"Q{globalQ:D3}";
                    var origText  = QuestionText($"{ci + 1}.{qInChapter}", globalQ);
                    var guidance  = $"Guidance {globalQ}.";

                    currentQs.Add(new ClqV01QuestionSpec(
                        RowNumber: row, XrefId: xrefId, OriginalText: origText, Guidance: guidance,
                        PreviousAnswer: Answer, Answer: Answer,
                        Strengths: Strengths, Weaknesses: Weaknesses, ProvidedBy: ProvidedBy));

                    templateQs.Add(new ClqV01QuestionSpec(
                        RowNumber: row, XrefId: xrefId, OriginalText: origText, Guidance: guidance,
                        PreviousAnswer: null, Answer: null,
                        Strengths: null, Weaknesses: null, ProvidedBy: null));

                    previousQs.Add(new ClqV01QuestionSpec(
                        RowNumber: row, XrefId: xrefId, OriginalText: origText, Guidance: guidance,
                        PreviousAnswer: null, Answer: Answer,
                        Strengths: Strengths, Weaknesses: Weaknesses, ProvidedBy: ProvidedBy));
                }
            }
        }

        return new ClqV01BaselineTrio(
            new ClqV01WorkbookDescriptor(chapterHeaders, sectionHeaders, currentQs),
            new ClqV01WorkbookDescriptor(chapterHeaders, sectionHeaders, templateQs),
            new ClqV01WorkbookDescriptor(chapterHeaders, sectionHeaders, previousQs));
    }
}
