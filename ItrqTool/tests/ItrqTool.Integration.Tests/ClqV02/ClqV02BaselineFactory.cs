using System.IO;
using ItrqTool.Tasks.ControlLevelQuestionValidationV02;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.QuestionnaireValidation.Parsing;

namespace ItrqTool.Integration.Tests.ClqV02;

/// <summary>The three workbooks produced by <see cref="ClqV02BaselineFactory.Build"/>.</summary>
public sealed record ClqV02BaselineTrio(
    ClqV02WorkbookDescriptor Current,
    ClqV02WorkbookDescriptor Template,
    ClqV02WorkbookDescriptor Previous);

/// <summary>
/// Builds a fully-consistent baseline trio against a <see cref="ControlLevelQuestionValidationV02Config"/>.
/// Every question satisfies all validation checks so the baseline produces zero findings.
/// </summary>
/// <remarks>
/// Baseline guarantee (no findings):
/// <list type="bullet">
/// <item>All XrefIds unique and non-empty across all three workbooks.</item>
/// <item>D/E/O and section/chapter text identical in current and template (no ReferenceTextAltered).</item>
/// <item>current.H = "2" ∈ AllowedAnswers; strengths and weaknesses both filled (answer 2 requires both).</item>
/// <item>current.K = "Yes" ∈ AllowedStabilityAnswers (no MissingResponse on K).</item>
/// <item>current.F = "2" = previous.H (PreviousAnswerAltered check passes).</item>
/// <item>|current.H − previous.H| = 0 &lt; DeviationThreshold = 2.</item>
/// <item>Both current and template carry the same H DV ("1,2,3,4") and K DV ("Yes,No"), so no FrozenConstraint fires.</item>
/// <item>Identical texts ensure Hungarian assigns each current question to its XrefId-matched previous (Agree).</item>
/// </list>
/// </remarks>
public static class ClqV02BaselineFactory
{
    private const string Answer          = "2";   // valid; answer "2" requires both strengths and weaknesses
    private const string Strengths       = "Baseline strengths.";
    private const string Weaknesses      = "Baseline weaknesses.";
    private const string ProvidedBy      = "TestOrgUnit";
    private const string AnswerStability = "Yes"; // ∈ AllowedStabilityAnswers ["Yes","No"]

    // Real audit questions, one per line, prefix already stripped.
    // Question index i (0-based, global generation order) gets line i as its base text.
    // Fail loudly on startup if the resource is missing or not exactly 193 lines.
    private static readonly IReadOnlyList<string> _realQuestions = LoadRealQuestions();

    private static IReadOnlyList<string> LoadRealQuestions()
    {
        const string ResourceName =
            "ItrqTool.Integration.Tests.ClqV01.clq-v01-trial-questions.txt";   // shared corpus, reused
        using var stream = typeof(ClqV02BaselineFactory).Assembly
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

    public static ClqV02BaselineTrio Build(ControlLevelQuestionValidationV02Config config)
    {
        // CHANGE A: v02 ChapterRows is IReadOnlyList<string>; parse to int before ordering
        // (string OrderBy would sort lexicographically — wrong for "4" vs "51" vs "145").
        var sortedChapters = config.ChapterRows.Select(int.Parse).OrderBy(r => r).ToList();

        // CHANGE B: v02 config has no ParsedSections; derive sections from the core LayoutParser.
        // Pass TextColumn for all three name-columns (matches what ClqV02Profile does).
        var layout = LayoutParser.Parse(
            config.ChapterRows, config.SectionRows,
            config.TextColumn, config.TextColumn, config.TextColumn);
        var allSections = layout.Sections;

        var chapterHeaders = sortedChapters
            .Select((row, i) => (Row: row, Text: $"Chapter {i + 1}"))
            .ToList();

        var sectionHeaders = new List<(int Row, string Text)>();
        for (int ci = 0; ci < sortedChapters.Count; ci++)
        {
            int thisChapter = sortedChapters[ci];
            int nextChapter = ci + 1 < sortedChapters.Count ? sortedChapters[ci + 1] : int.MaxValue;
            var sectionsInChapter = allSections
                .Where(s => s.SectionRow > thisChapter && s.SectionRow < nextChapter)
                .OrderBy(s => s.SectionRow)
                .ToList();
            for (int si = 0; si < sectionsInChapter.Count; si++)
                sectionHeaders.Add((sectionsInChapter[si].SectionRow, $"Section {ci + 1}.{si + 1}"));
        }

        var currentQs  = new List<ClqV02QuestionSpec>();
        var templateQs = new List<ClqV02QuestionSpec>();
        var previousQs = new List<ClqV02QuestionSpec>();

        int globalQ = 0;
        for (int ci = 0; ci < sortedChapters.Count; ci++)
        {
            int thisChapter = sortedChapters[ci];
            int nextChapter = ci + 1 < sortedChapters.Count ? sortedChapters[ci + 1] : int.MaxValue;
            var sectionsInChapter = allSections
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
                    var xrefId   = $"Q{globalQ:D3}";
                    var origText = QuestionText($"{ci + 1}.{qInChapter}", globalQ);
                    var guidance = $"Guidance {globalQ}.";

                    // CHANGE C: + AnswerStability — "Yes" on current/previous, null (blank K) on template.
                    currentQs.Add(new ClqV02QuestionSpec(
                        RowNumber: row, XrefId: xrefId, OriginalText: origText, Guidance: guidance,
                        PreviousAnswer: Answer, Answer: Answer,
                        Strengths: Strengths, Weaknesses: Weaknesses, ProvidedBy: ProvidedBy,
                        AnswerStability: AnswerStability));

                    templateQs.Add(new ClqV02QuestionSpec(
                        RowNumber: row, XrefId: xrefId, OriginalText: origText, Guidance: guidance,
                        PreviousAnswer: null, Answer: null,
                        Strengths: null, Weaknesses: null, ProvidedBy: null,
                        AnswerStability: null));

                    previousQs.Add(new ClqV02QuestionSpec(
                        RowNumber: row, XrefId: xrefId, OriginalText: origText, Guidance: guidance,
                        PreviousAnswer: null, Answer: Answer,
                        Strengths: Strengths, Weaknesses: Weaknesses, ProvidedBy: ProvidedBy,
                        AnswerStability: AnswerStability));
                }
            }
        }

        return new ClqV02BaselineTrio(
            new ClqV02WorkbookDescriptor(chapterHeaders, sectionHeaders, currentQs),
            new ClqV02WorkbookDescriptor(chapterHeaders, sectionHeaders, templateQs),
            new ClqV02WorkbookDescriptor(chapterHeaders, sectionHeaders, previousQs));
    }
}
