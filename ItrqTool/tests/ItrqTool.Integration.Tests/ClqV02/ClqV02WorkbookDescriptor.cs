namespace ItrqTool.Integration.Tests.ClqV02;

/// <summary>Describes one v02 workbook: its header rows and its question rows.</summary>
/// <param name="ChapterHeaders">Row → D-cell text for each chapter header row.</param>
/// <param name="SectionHeaders">Row → D-cell text for each section header row.</param>
/// <param name="Questions">Per-question specs in sheet order. Perturbations replace entries with <c>with</c> expressions before writing.</param>
public sealed record ClqV02WorkbookDescriptor(
    IReadOnlyList<(int Row, string Text)> ChapterHeaders,
    IReadOnlyList<(int Row, string Text)> SectionHeaders,
    IReadOnlyList<ClqV02QuestionSpec> Questions
);
