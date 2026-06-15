using ItrqTool.Domain;

namespace ItrqTool.Tasks.QuestionnaireValidation.Parsing;

/// <summary>
/// The already-parsed structural map a <see cref="QuestionParser"/> run iterates over:
/// which rows are chapter headers, which are section headers, and the question-row span
/// of each section. Mirrors CLQ_v01's <c>ParsedSections</c> / <c>ChapterRows</c> shape,
/// generalized so each header carries the column its NAME is read from (v01 hard-wired
/// every name to its single text column).
/// </summary>
/// <remarks>
/// Parsing the SectionRows / ChapterRows config <em>strings</em> (and their
/// <see cref="FormatException"/> validation) into this map is NOT this layer's job — that
/// lands with config. Construct this record directly.
/// <para>
/// <see cref="QuestionTextColumn"/> is the structural column the shared parser tests for
/// blankness when deciding whether a row inside a section range is an actual question (the
/// blank-question-row skip + warning). It is distinct from the per-header name columns,
/// though in CLQ_v01 all three happen to be the same column.
/// </para>
/// </remarks>
public sealed record QuestionnaireLayout(
    string QuestionTextColumn,
    IReadOnlyList<LayoutChapter> Chapters,
    IReadOnlyList<LayoutSection> Sections);

/// <summary>A chapter header row and the column its chapter name is read from.</summary>
public sealed record LayoutChapter(int RowNumber, string NameColumn);

/// <summary>
/// A section header row, its question-row span, and the column its section name is read
/// from. Mirrors v01's <c>SectionDefinition(SectionRow, FirstQuestionRow, LastQuestionRow)</c>
/// plus the per-section name column.
/// </summary>
public sealed record LayoutSection(
    int SectionRow,
    int FirstQuestionRow,
    int LastQuestionRow,
    string NameColumn);

/// <summary>
/// One question row handed to the per-version record factory: the raw
/// <see cref="ExcelRowStructure"/>, its row number, and the chapter / section names current
/// at that row. The factory reads its own payload columns and derives identity (e.g. the
/// CLQ prefix scheme via <see cref="QuestionNumberParser"/>) from <see cref="Row"/>.
/// </summary>
public sealed record QuestionRowContext(
    ExcelRowStructure Row,
    int RowNumber,
    string ChapterName,
    string SectionName);
